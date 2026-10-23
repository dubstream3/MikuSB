using Newtonsoft.Json.Linq;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using System.Text.Json.Nodes;

namespace MikuSB.GameServer.Game.Achievement;

/// <summary>
/// Shared claim path for the two achievement tables that use the same attribute packing:
///   * AttrIds.Achievement.GroupId = 2 — achievement/achievement.json (AchievementExcel),
///     claimed by Achievement_GetReward / Achievement_QuickGetReward;
///   * AttrIds.Achievement.QuestGroupId = 7 — achievement/activity_quest.json (ActivityQuestExcel),
///     claimed per module (arcade -> ArcadeGameGetAchReward, 异星守护 -> AlienDefense_GetQuestAward,
///     弑神斗场 -> SeasonBoss_GetReward, ...).
///
/// Layout (identical in both groups, see Achievement.lua): bit 0 = reward claimed,
/// bits 1..31 = progress, and the row is finished when progress >= CheckValue
/// (Achievement.lua:743 GetProgres / :783 IsReceive / :670 IsFinished).
///
/// The client always re-reads these attributes from the sync payload, so every writer here pushes
/// through the NtfSyncPlayer it is given — a reply body alone would leave the panel stale (042/046).
/// </summary>
public static class AchievementClaimService
{
    /// <summary>Achievement.GroupID (Achievement.lua:15) — the standard achievement table.</summary>
    public const uint NormalGroupId = AttrIds.Achievement.GroupId;

    /// <summary>Achievement.GroupID_Quest (Achievement.lua:16) — the activity quest table.</summary>
    public const uint QuestGroupId = AttrIds.Achievement.QuestGroupId;

    private const uint ClaimedBit = 1u;
    private const int ProgressShift = 1;
    private const uint MaxProgress = 0x7FFFFFFFu;

    public static bool IsClaimed(PlayerInstance player, uint groupId, uint id)
        => (player.Attributes.GetValue(groupId, id) & ClaimedBit) != 0;

    public static uint GetProgress(PlayerInstance player, uint groupId, uint id)
        => (player.Attributes.GetValue(groupId, id) >> ProgressShift) & MaxProgress;

    public static bool IsFinished(PlayerInstance player, uint groupId, uint id, uint checkValue)
    {
        var target = checkValue == 0 ? 1u : checkValue;
        return GetProgress(player, groupId, id) >= target;
    }

    /// <summary>
    /// Marks a row finished (progress = CheckValue) without claiming it. Used by the modules that can
    /// derive the condition from state they already keep, so the panel's red dot appears before the
    /// player presses claim (the client refuses to send a claim for a row it thinks is unfinished).
    /// </summary>
    public static bool Complete(PlayerInstance player, uint groupId, uint id, uint checkValue, NtfSyncPlayer sync)
    {
        var target = checkValue == 0 ? 1u : checkValue;
        var attr = player.Attributes.GetOrCreate(groupId, id);
        if ((attr.Val >> ProgressShift) >= target)
            return false;

        attr.Val = (attr.Val & ClaimedBit) | (target << ProgressShift);
        player.Attributes.SyncTo(sync, attr);
        DatabaseHelper.SaveDatabaseType(player.Data);
        return true;
    }

    /// <summary>
    /// Claims a finished row: grants every reward row, sets the claimed bit and saves.
    /// Returns the granted rows (the client feeds them into Item.Gain), or null when the row is
    /// already claimed or not finished yet.
    /// </summary>
    public static async ValueTask<JsonArray?> TryClaimAsync(
        PlayerInstance player,
        uint groupId,
        uint id,
        uint checkValue,
        IReadOnlyList<IReadOnlyList<uint>> rewardRows,
        NtfSyncPlayer sync)
    {
        if (IsClaimed(player, groupId, id))
            return null;

        if (!IsFinished(player, groupId, id, checkValue))
            return null;

        var granted = rewardRows.Count > 0
            ? await player.RewardManager.GrantConfiguredRewardsAsync(rewardRows, sync)
            : new JsonArray();

        var attr = player.Attributes.GetOrCreate(groupId, id);
        attr.Val |= ClaimedBit;
        player.Attributes.SyncTo(sync, attr);
        DatabaseHelper.SaveDatabaseType(player.Data);
        return granted;
    }

    /// <summary>
    /// Reads the [ [G, D, P, L, Count], ... ] reward column shared by achievement.json /
    /// activity_quest.json / dlc_mission.json. Rows that are not five numbers or carry a zero count
    /// are dropped (the tables mix empty strings into the column).
    /// </summary>
    public static List<List<uint>> ReadRewardRows(JToken? token)
    {
        var rows = new List<List<uint>>();
        if (token is not JArray outer)
            return rows;

        foreach (var rowNode in outer)
        {
            if (rowNode is not JArray row || row.Count < 5)
                continue;

            var parsed = new List<uint>(5);
            var ok = true;
            for (var i = 0; i < 5; i++)
            {
                var raw = row[i]?.ToString().Trim().Trim('[', ']', '"') ?? string.Empty;
                if (!uint.TryParse(raw, out var value))
                {
                    ok = false;
                    break;
                }

                parsed.Add(value);
            }

            if (ok && parsed[4] > 0)
                rows.Add(parsed);
        }

        return rows;
    }
}
