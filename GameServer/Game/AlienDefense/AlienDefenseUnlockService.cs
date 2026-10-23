using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Game.AlienDefense;

/// <summary>
/// Writes the companion-handbook ("伙伴手册") and support-girl unlock bits for 异星守护.
///
/// The client's state is only ever read from the three bit pairs of
/// (143, PalUnlockStartSid + Id) and (143, GirlUnlockStartSid + Id):
/// AlienDefense.GetPalUnlockState (AlienDefense.lua:329-359) does
/// me:GetAttribute(143, nAttrStart + nId) and tests bit (nType-1)*2, where nType 1 is the entry
/// itself, 2 the active skill and 3 the passive one; the sibling bit +1 is the "seen" flag the
/// client clears through AlienDefense_SetPalFlag, whose server handler
/// (AlienDefense_SetPalFlag.cs:65) writes that flag only. Nothing in the client scripts and nothing
/// else on the server ever writes the unlock bits, so the handbook stayed locked forever and the
/// unlock popups (umg_mow_map1.CheckNewUnlock) could never fire.
///
/// Unlock requirements live in aliendefense_pal_main.json / aliendefense_girl.json and are evaluated
/// by AlienDefense.CheckPalCondition (AlienDefense.lua:169-242). All four forms are supported now
/// that AlienDefense_LevelSettlement maintains the counters they read:
///
///   [1, level, n]     -> n clears of `level`             reads (22, level)
///   [1, level, n, 1]  -> n clears of `level` BY THIS ROW reads (143, 111..250 / 451..550)
///   [2, type, n]      -> n kills of level type `type`    reads (143, 10..110 / 351..450)
///   [3, type, n]      -> n clears across that type's levels (same packed block, summed)
///   [4, n]            -> n girl-skill casts (girls only)  reads (143, 601..610)
///
/// Entry bits are set from Condition; skill bits follow the client's gating in GetGirlLevel
/// (:618-628), which walks 1..3 and stops at the first missing bit: skill 1 (bit2) needs the entry
/// bit, skill 2 (bit4) needs skill 1.
/// </summary>
internal static class AlienDefenseUnlockService
{
    private static readonly Logger Log = new(nameof(AlienDefenseUnlockService));

    /// <summary>
    /// Unlocks every roster entry and skill of the player's current act whose requirement is
    /// satisfied. Returns the roster ids that flipped, shaped exactly like the settlement reply
    /// fields (elements are roster ids -- uw_mow_team_unlock.lua:12 / umg_mow_girl_unlock.lua:15),
    /// plus whether anything changed (the caller decides about re-saving).
    /// </summary>
    public static (JsonArray UnlockedPals, JsonArray UnlockedGirls, bool Changed) GrantUnlocks(
        PlayerInstance player,
        NtfSyncPlayer? sync)
    {
        var unlockedPals = new JsonArray();
        var unlockedGirls = new JsonArray();

        var actId = player.Attributes.GetValue(AttrIds.AlienDefense.Gid, AttrIds.AlienDefense.ActivitySid);
        if (actId == 0)
            return (unlockedPals, unlockedGirls, false);

        var row = GameData.AlienDefenseData.TryGetValue(actId, out var act) ? act : null;
        var changed = false;

        if (GameData.AlienDefensePalData.TryGetValue(actId, out var pals))
        {
            foreach (var pal in pals.Values.OrderBy(x => x.Id))
            {
                // Pal_Type 1 is the handbook entry itself (uw_mow_team.lua:34-40); type 2 is an
                // alternate row of the same pal and type 3 a combined pal whose state the client
                // derives from its members (AlienDefense.GetPalUnlockState), so neither is written.
                if (pal.PalType != 1)
                    continue;

                var entryBit = SetUnlockBit(player, sync,
                    AttrIds.AlienDefense.PalUnlockStartSid + pal.Id, 0,
                    ConditionMet(player, row, pal.Condition, pal.Id, false));
                if (entryBit)
                {
                    unlockedPals.Add(pal.Id);
                    changed = true;
                    Log.Info($"Alien defense pal {pal.Id} unlocked for uid={player.Uid} (act={actId}, " +
                             $"condition=[{string.Join(',', pal.Condition)}]).");
                }

                // Skill bits are only reachable once the entry itself is unlocked (GetGirlLevel
                // breaks at the first missing bit), so each one is gated on the previous.
                var entrySet = entryBit || IsBitSet(player, AttrIds.AlienDefense.PalUnlockStartSid + pal.Id, 0);
                var skill1 = entrySet && SetUnlockBit(player, sync,
                    AttrIds.AlienDefense.PalUnlockStartSid + pal.Id, 2,
                    ConditionMet(player, row, pal.ExtraUpgradeCondition, pal.Id, false));
                if (skill1)
                    changed = true;

                var skill1Set = skill1 || IsBitSet(player, AttrIds.AlienDefense.PalUnlockStartSid + pal.Id, 2);
                var skill2 = skill1Set && SetUnlockBit(player, sync,
                    AttrIds.AlienDefense.PalUnlockStartSid + pal.Id, 4,
                    ConditionMet(player, row, pal.ExtraPassiveCondition, pal.Id, false));
                if (skill2)
                    changed = true;
            }
        }

        if (GameData.AlienDefenseGirlData.TryGetValue(actId, out var girls))
        {
            foreach (var girl in girls.Values.OrderBy(x => x.Id))
            {
                var entryBit = SetUnlockBit(player, sync,
                    AttrIds.AlienDefense.GirlUnlockStartSid + girl.Id, 0,
                    ConditionMet(player, row, girl.Condition, girl.Id, true));
                if (entryBit)
                {
                    unlockedGirls.Add(girl.Id);
                    changed = true;
                    Log.Info($"Alien defense support girl {girl.Id} unlocked for uid={player.Uid} " +
                             $"(act={actId}, condition=[{string.Join(',', girl.Condition)}]).");
                }

                var entrySet = entryBit || IsBitSet(player, AttrIds.AlienDefense.GirlUnlockStartSid + girl.Id, 0);
                var skill1 = entrySet && SetUnlockBit(player, sync,
                    AttrIds.AlienDefense.GirlUnlockStartSid + girl.Id, 2,
                    ConditionMet(player, row, girl.SkillCondition1, girl.Id, true));
                if (skill1)
                    changed = true;

                var skill1Set = skill1 || IsBitSet(player, AttrIds.AlienDefense.GirlUnlockStartSid + girl.Id, 2);
                var skill2 = skill1Set && SetUnlockBit(player, sync,
                    AttrIds.AlienDefense.GirlUnlockStartSid + girl.Id, 4,
                    ConditionMet(player, row, girl.SkillCondition2, girl.Id, true));
                if (skill2)
                    changed = true;
            }
        }

        if (changed)
            Database.DatabaseHelper.SaveDatabaseType(player.Data);

        return (unlockedPals, unlockedGirls, changed);
    }

    /// <summary>
    /// AlienDefense.CheckPalCondition (AlienDefense.lua:169-242). An empty requirement means
    /// "always unlocked" (the client returns true immediately); the four non-empty forms are
    /// documented on the class. An unknown form stays unmet rather than being guessed at.
    /// </summary>
    private static bool ConditionMet(
        PlayerInstance player,
        AlienDefenseExcel? row,
        IReadOnlyList<uint> condition,
        uint rosterIdx,
        bool isGirl)
    {
        if (condition.Count == 0)
            return true;

        if (condition.Count < 3)
            return false;

        var kind = condition[0];
        var needed = condition[2];
        if (needed == 0)
            return true;

        switch (kind)
        {
            case 1:
            {
                var levelId = condition[1];
                if (levelId == 0)
                    return false;

                // e == 1 counts this roster entry's own clears (GetPassTime) instead of the level's.
                if (condition.Count >= 4 && condition[3] == 1)
                    return ReadPassTime(player, row, levelId, rosterIdx, isGirl) >= needed;

                return player.Attributes.GetValue(AttrIds.Launch.LevelPassGid, levelId) >= needed;
            }
            case 2:
                return ReadKillNum(player, rosterIdx, condition[1], isGirl) >= needed;
            case 3:
                return ReadTypePassTime(player, row, condition[1], rosterIdx, isGirl) >= needed;
            case 4:
                return isGirl && ReadGirlSkillNum(player, rosterIdx) >= needed;
            default:
                return false;
        }
    }

    /// <summary>
    /// AlienDefense.GetPassTime (AlienDefense.lua:148-166): step = ceil(maxNum / 4) slots per level,
    /// subId = start + (levelIdx-1) * step + idx / 4, value = 8 bits at ((idx-1) % 4) * 8. A level
    /// that is not part of the act's LevelList reads 0 on both sides (GetLevelIdx returns 0).
    /// </summary>
    private static uint ReadPassTime(
        PlayerInstance player,
        AlienDefenseExcel? row,
        uint levelId,
        uint rosterIdx,
        bool isGirl)
    {
        if (row == null || levelId == 0)
            return 0;

        var maxNum = isGirl ? AttrIds.AlienDefense.MaxGirlNum : AttrIds.AlienDefense.MaxPalNum;
        if (rosterIdx == 0 || rosterIdx > maxNum)
            return 0;

        var levelIdx = row.GetLevelIdx(levelId);
        if (levelIdx == 0)
            return 0;

        var startSid = isGirl
            ? AttrIds.AlienDefense.GirlPassTimeStartSid
            : AttrIds.AlienDefense.PalPassTimeStartSid;
        var endSid = isGirl
            ? AttrIds.AlienDefense.GirlPassTimeEndSid
            : AttrIds.AlienDefense.PalPassTimeEndSid;

        var step = (maxNum + 3) / 4;
        var subId = startSid + (levelIdx - 1) * step + rosterIdx / 4;
        if (subId > endSid)
            return 0;

        var bitStart = (int)((rosterIdx - 1) % 4) * 8;
        return (player.Attributes.GetValue(AttrIds.AlienDefense.Gid, subId) >> bitStart) & 0xFFu;
    }

    /// <summary>
    /// AlienDefense.CheckPalCondition form 3 (:210-228): the entry's own clears summed over every
    /// level of one level type.
    /// </summary>
    private static uint ReadTypePassTime(
        PlayerInstance player,
        AlienDefenseExcel? row,
        uint levelType,
        uint rosterIdx,
        bool isGirl)
    {
        if (row == null || levelType == 0 || levelType > (uint)row.LevelGroups.Count)
            return 0;

        var total = 0u;
        foreach (var levelId in row.LevelGroups[(int)levelType - 1])
            total += ReadPassTime(player, row, levelId, rosterIdx, isGirl);

        return total;
    }

    /// <summary>
    /// AlienDefense.GetKillNum (AlienDefense.lua:120-145 / :301-326): level type 0 sums every type,
    /// subId = idx + start + (levelType-1) * maxNum.
    /// </summary>
    private static uint ReadKillNum(PlayerInstance player, uint rosterIdx, uint levelType, bool isGirl)
    {
        var maxNum = isGirl ? AttrIds.AlienDefense.MaxGirlNum : AttrIds.AlienDefense.MaxPalNum;
        if (rosterIdx == 0 || rosterIdx > maxNum)
            return 0;

        if (levelType == 0)
        {
            var total = 0u;
            for (var type = 1u; type <= AttrIds.AlienDefense.MaxTypeNum; type++)
                total += ReadKillNum(player, rosterIdx, type, isGirl);

            return total;
        }

        if (levelType > AttrIds.AlienDefense.MaxTypeNum)
            return 0;

        var startSid = isGirl
            ? AttrIds.AlienDefense.GirlKillNumStartSid
            : AttrIds.AlienDefense.PalKillNumStartSid;
        var endSid = isGirl
            ? AttrIds.AlienDefense.GirlKillNumEndSid
            : AttrIds.AlienDefense.PalKillNumEndSid;

        var subId = rosterIdx + startSid + (levelType - 1) * maxNum;
        if (subId > endSid)
            return 0;

        return player.Attributes.GetValue(AttrIds.AlienDefense.Gid, subId);
    }

    /// <summary>AlienDefense.GetGirlSkillNum (:294-298): (143, 601 + idx - 1), girls only.</summary>
    private static uint ReadGirlSkillNum(PlayerInstance player, uint rosterIdx)
    {
        if (rosterIdx == 0 || rosterIdx > AttrIds.AlienDefense.MaxGirlNum)
            return 0;

        var subId = AttrIds.AlienDefense.GirlSkillNumStartSid + rosterIdx - 1;
        if (subId > AttrIds.AlienDefense.GirlSkillNumEndSid)
            return 0;

        return player.Attributes.GetValue(AttrIds.AlienDefense.Gid, subId);
    }

    private static bool IsBitSet(PlayerInstance player, uint sid, int bit)
        => (player.Attributes.GetValue(AttrIds.AlienDefense.Gid, sid) & (1u << bit)) != 0;

    private static bool SetUnlockBit(PlayerInstance player, NtfSyncPlayer? sync, uint sid, int bit, bool met)
    {
        if (!met)
            return false;

        var attr = player.Attributes.GetOrCreate(AttrIds.AlienDefense.Gid, sid);
        var mask = 1u << bit;
        if ((attr.Val & mask) != 0)
            return false;

        attr.Val |= mask;
        if (sync != null)
            player.Attributes.SyncTo(sync, attr);

        return true;
    }
}
