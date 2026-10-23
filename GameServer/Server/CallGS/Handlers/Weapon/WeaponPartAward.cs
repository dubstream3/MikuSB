using MikuSB.Data;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Weapon;

/// <summary>
/// Helpers for the "part that unlocks when the weapon reaches its break cap" line.
/// Client side (Item/Weapon.lua:1490-1526) the state of that part lives in two 5 bit fields of
/// the player attribute group 112 (Weapon.PART_LOCK_GID), addressed by tonumber(detail .. particular):
/// bits 0-4 = unlocked, bits 5-8 = claimed.
/// </summary>
internal static class WeaponPartAward
{
    private const uint LockGid = AttrIds.Item.WeaponPartLockGid;
    private const uint FieldMask = 0x1Fu;
    private const int UnlockShift = 0;
    private const int ClaimedShift = 5;
    private const uint UnlockValue = 1u;
    private const uint ClaimedValue = 1u;

    /// <summary>
    /// Break stage whose level cap unlocks the award. The official server hands the part out as soon
    /// as the weapon reaches the cap of its *initial* stage (break_level_limit.json Break1, 30 for
    /// every row of the live table), not the cap of the stage the weapon has been broken up to:
    /// gating on the current cap made a Break3 weapon (cap 50) unlock the part 20 levels too late.
    /// </summary>
    public const uint AwardUnlockBreak = 1;

    /// <summary>Level the weapon has to reach for its award to unlock, or 0 when the row is unknown.</summary>
    public static uint GetAwardUnlockLevel(WeaponUpgradeConfig config, int breakLimitId) =>
        config.GetWeaponMaxLevel(breakLimitId, AwardUnlockBreak);

    /// <summary>
    /// 4 element GDPL of the part a weapon hands out at its max level, or null when the weapon
    /// has no award (41 rows of item/templates/weapon.json) or an incomplete one.
    /// </summary>
    public static List<int>? GetAward(ulong weaponTemplateId)
    {
        var template = GameData.WeaponData.Values.FirstOrDefault(
            x => GameResourceTemplateId.FromGdpl(x.Genre, x.Detail, x.Particular, x.Level) == weaponTemplateId);

        var award = template?.WeaponPartsAward;
        if (award == null || award.Count < 4)
        {
            return null;
        }

        return award[0] > 0 && award[1] > 0 && award[2] > 0 && award[3] > 0 ? award : null;
    }

    /// <summary>Attribute sid the client derives through tonumber(detail .. particular).</summary>
    public static uint GetSid(List<int> award) => uint.Parse($"{award[1]}{award[2]}");

    public static bool IsUnlocked(PlayerInstance player, uint sid) =>
        ((player.Attributes.GetValue(LockGid, sid) >> UnlockShift) & FieldMask) == UnlockValue;

    public static bool IsClaimed(PlayerInstance player, uint sid) =>
        ((player.Attributes.GetValue(LockGid, sid) >> ClaimedShift) & FieldMask) == ClaimedValue;

    public static bool MarkUnlocked(PlayerInstance player, ulong weaponTemplateId, NtfSyncPlayer sync)
    {
        var award = GetAward(weaponTemplateId);
        return award != null && MarkUnlocked(player, GetSid(award), sync);
    }

    public static bool MarkUnlocked(PlayerInstance player, uint sid, NtfSyncPlayer sync)
    {
        var value = player.Attributes.GetValue(LockGid, sid);
        if (((value >> UnlockShift) & FieldMask) == UnlockValue)
        {
            return false;
        }

        value = (value & ~(FieldMask << UnlockShift)) | (UnlockValue << UnlockShift);
        player.Attributes.Set(LockGid, sid, value);
        player.Attributes.SyncTo(sync, LockGid, sid, value);
        return true;
    }

    public static bool MarkClaimed(PlayerInstance player, uint sid, NtfSyncPlayer sync)
    {
        var value = player.Attributes.GetValue(LockGid, sid);
        if (((value >> ClaimedShift) & FieldMask) == ClaimedValue)
        {
            return false;
        }

        value = (value & ~(FieldMask << ClaimedShift)) | (ClaimedValue << ClaimedShift);
        player.Attributes.Set(LockGid, sid, value);
        player.Attributes.SyncTo(sync, LockGid, sid, value);
        return true;
    }

    /// <summary>
    /// Sets the unlock bit for every award the player has already earned. Attribute group 112 is
    /// never written by the client and the arms panel only renders the claim button once the bit is
    /// set (UMG/Arms/Widgets/uw_arms_partslock.lua:15), so a save that levelled the weapon before
    /// this line existed would keep the part invisible forever. Called from the login path before
    /// the login snapshot is serialized, like the other Ensure*/Roll* repairs there.
    /// </summary>
    public static bool RepairUnlocked(PlayerInstance player)
    {
        var config = WeaponUpgradeConfig.Load();
        var sync = new NtfSyncPlayer();
        var changed = false;

        foreach (var weapon in player.InventoryManager.InventoryData.Weapons.Values)
        {
            if (!config.TryGetWeaponTemplate(weapon.TemplateId, out var template))
            {
                continue;
            }

            var unlockLevel = GetAwardUnlockLevel(config, template.BreakLimitId);
            if (unlockLevel == 0 || weapon.Level < unlockLevel)
            {
                continue;
            }

            changed |= MarkUnlocked(player, weapon.TemplateId, sync);
        }

        return changed;
    }
}
