using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Game.SeasonBoss;

/// <summary>
/// 弑神斗场 (season boss) quest state. The mode reuses the activity achievement table
/// (achievement/activity_quest.json) for its twenty per-level rows 19001..19020, so the state lives
/// in attribute (AttrIds.Achievement.QuestGroupId = 7, questId) packed exactly like the DLC mission
/// board: bit 0 = reward claimed, bits 1..31 = progress
/// (Achievement/Achievement.lua:743 GetProgres, :783 IsReceive, :670 IsFinished).
///
/// The level -> quest mapping comes from seasonlist's positional LevelList/TaskID pairing, NOT from
/// seasonleveldes.ReferAward: that column is misaligned for several acts (for ActID 10 the 3411-3415
/// block names 19016-19020 where seasonlist says 19006-19010), so using it strands quests and credits
/// the wrong block. See SeasonBossExcel for the full explanation.
///
/// SeasonBoss_LevelSettlement calls MarkLevelCleared and SeasonBoss_GetReward calls ClaimAsync; both
/// keep the client's own view of the row consistent because they write the very attributes
/// Achievement.lua reads.
/// </summary>
public static class SeasonBossQuestService
{
    private const int ProgressShift = 1;
    private const uint ProgressMask = 0x7FFFFFFFu;
    private static readonly Logger Log = new(nameof(SeasonBossQuestService));

    public static uint GetProgress(uint raw) => (raw >> ProgressShift) & ProgressMask;

    public static bool IsReceived(uint raw) => (raw & 1u) != 0;

    public static uint Pack(uint progress, bool received) =>
        (Math.Min(progress, ProgressMask) << ProgressShift) | (received ? 1u : 0u);

    /// <summary>
    /// seasonlist row owning <paramref name="levelId"/>, or null when no act lists that level. Level ids
    /// are unique across the whole seasonlist, so no act filter is needed.
    /// </summary>
    public static SeasonBossExcel? ResolveRow(uint levelId) => SeasonBossExcel.FindByLevel(levelId, 0);

    /// <summary>
    /// True when some season boss level row pays out this quest. Guards SeasonBoss_GetReward against
    /// being used as a generic "grant me any activity quest" endpoint.
    /// </summary>
    public static bool IsSeasonBossQuest(uint questId)
    {
        if (questId == 0)
            return false;

        foreach (var row in GameData.SeasonBossData.Values)
        {
            if (row.TaskList.Contains(questId))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Marks the quest behind a cleared season boss level as finished. Returns false when the level
    /// belongs to no act row, has no paired quest, or the quest was already finished.
    /// </summary>
    public static bool MarkLevelCleared(PlayerInstance player, uint levelId, NtfSyncPlayer sync)
    {
        var row = ResolveRow(levelId);
        var questId = row?.FindQuestForLevel(levelId) ?? 0;
        if (questId == 0)
        {
            Log.Warn($"SeasonBoss level {levelId} has no quest row for uid={player.Uid}.");
            return false;
        }

        if (!GameData.ActivityQuestData.TryGetValue(questId, out var quest))
        {
            Log.Warn($"SeasonBoss level {levelId} references unknown quest {questId} for uid={player.Uid}.");
            return false;
        }

        var attr = player.Attributes.GetOrCreate(AttrIds.Achievement.QuestGroupId, questId);
        if (GetProgress(attr.Val) >= quest.CheckValue)
            return false;

        attr.Val = Pack(quest.CheckValue, IsReceived(attr.Val));
        player.Attributes.SyncTo(sync, attr);

        Log.Info($"SeasonBoss level {levelId} cleared by uid={player.Uid}, quest {questId} finished.");
        return true;
    }

    /// <summary>
    /// Pays out every claimable quest of the request. Already claimed or unfinished rows are skipped
    /// (the client checks the same attributes first, so a mismatch only means a stale UI), and the
    /// claimed bit is flipped on the ones that paid out.
    /// </summary>
    public static async Task<JsonArray> ClaimAsync(
        PlayerInstance player,
        IEnumerable<uint> questIds,
        NtfSyncPlayer sync)
    {
        var granted = new JsonArray();
        var claimedAny = false;

        foreach (var questId in questIds.Distinct())
        {
            if (!GameData.ActivityQuestData.TryGetValue(questId, out var quest))
            {
                Log.Warn($"SeasonBoss reward: unknown quest {questId} requested by uid={player.Uid}.");
                continue;
            }

            if (!IsSeasonBossQuest(questId))
            {
                Log.Warn($"SeasonBoss reward: quest {questId} is not a season boss row, ignored for uid={player.Uid}.");
                continue;
            }

            var attr = player.Attributes.GetOrCreate(AttrIds.Achievement.QuestGroupId, questId);
            if (IsReceived(attr.Val))
            {
                Log.Warn($"SeasonBoss reward: quest {questId} already claimed for uid={player.Uid}.");
                continue;
            }

            var progress = GetProgress(attr.Val);
            if (progress < quest.CheckValue)
            {
                Log.Warn($"SeasonBoss reward: quest {questId} not finished " +
                         $"({progress}/{quest.CheckValue}) for uid={player.Uid}.");
                continue;
            }

            var rows = DlcRuntimeOverrides.ReadRewardRows(quest.Rewards);
            var rewards = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);

            // JsonNode cannot have two parents, so the rows have to be cloned out of the manager's
            // array before they can be collected into the response.
            foreach (var row in rewards)
                granted.Add(row?.DeepClone());

            attr.Val = Pack(progress, true);
            player.Attributes.SyncTo(sync, attr);
            claimedAny = true;

            Log.Info($"SeasonBoss reward: quest {questId} claimed, {rewards.Count} row(s) granted for uid={player.Uid}.");
        }

        if (claimedAny)
        {
            DatabaseHelper.SaveDatabaseType(player.Data);
            DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
            DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
        }

        return granted;
    }
}
