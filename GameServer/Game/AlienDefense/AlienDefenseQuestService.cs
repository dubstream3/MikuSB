using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using Newtonsoft.Json.Linq;

namespace MikuSB.GameServer.Game.AlienDefense;

/// <summary>
/// 异星守护 (alien defense / Mow) quest state. Unlike 弑神斗场 (whose seasonlist pairs levels to
/// quests positionally), alienlist only carries a flat NormalMission allowlist, so the per-level
/// quest is resolved through the achievement table itself: rows with Type == 2 name their level in
/// Param (e.g. quest 20001 -> Param [6231]). Other types (296/259/260/258/340/341/397/398) track
/// cumulative counters the server does not simulate yet and are left for the client-side
/// achievement events.
///
/// State lives in attribute (AttrIds.Achievement.QuestGroupId = 7, questId) packed exactly like
/// the DLC mission board: bit 0 = reward claimed, bits 1..31 = progress
/// (Achievement/Achievement.lua:743 GetProgres, :783 IsReceive, :670 IsFinished).
///
/// AlienDefense_LevelSettlement calls MarkLevelCleared and AlienDefense_GetQuestAward calls
/// ClaimAsync; both write the very attributes Achievement.lua reads, so the client's own mission
/// panel stays consistent.
/// </summary>
public static class AlienDefenseQuestService
{
    private const int ProgressShift = 1;
    private const uint ProgressMask = 0x7FFFFFFFu;

    /// <summary>activity_quest Type whose Param names the cleared level id.</summary>
    private const uint LevelClearQuestType = 2;

    private static readonly Logger Log = new(nameof(AlienDefenseQuestService));

    public static uint GetProgress(uint raw) => (raw >> ProgressShift) & ProgressMask;

    public static bool IsReceived(uint raw) => (raw & 1u) != 0;

    public static uint Pack(uint progress, bool received) =>
        (Math.Min(progress, ProgressMask) << ProgressShift) | (received ? 1u : 0u);

    /// <summary>
    /// Quest finished by clearing <paramref name="levelId"/>, or 0 when no Type-2 row names it.
    /// </summary>
    public static uint FindQuestForLevel(uint levelId)
    {
        if (levelId == 0)
            return 0;

        foreach (var quest in GameData.ActivityQuestData.Values)
        {
            if (quest.Type != LevelClearQuestType)
                continue;

            if (ParamContains(quest.Param, levelId))
                return quest.Id;
        }

        return 0;
    }

    /// <summary>
    /// True when <paramref name="questId"/> belongs to the act's NormalMission allowlist. Guards
    /// AlienDefense_GetQuestAward against being used as a generic "grant me any activity quest"
    /// endpoint.
    /// </summary>
    public static bool IsAlienDefenseQuest(uint questId, uint actId)
    {
        if (questId == 0)
            return false;

        if (actId != 0 && GameData.AlienDefenseData.TryGetValue(actId, out var row))
            return row.NormalMissions.Contains(questId);

        foreach (var actRow in GameData.AlienDefenseData.Values)
        {
            if (actRow.NormalMissions.Contains(questId))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Marks the quest behind a cleared alien defense level as finished. Returns false when no
    /// Type-2 row names the level, the quest row is unknown, or it was already finished.
    /// </summary>
    public static bool MarkLevelCleared(PlayerInstance player, uint levelId, NtfSyncPlayer sync)
    {
        var questId = FindQuestForLevel(levelId);
        if (questId == 0)
        {
            Log.Warn($"Alien defense level {levelId} has no quest row for uid={player.Uid}.");
            return false;
        }

        if (!GameData.ActivityQuestData.TryGetValue(questId, out var quest))
        {
            Log.Warn($"Alien defense level {levelId} references unknown quest {questId} for uid={player.Uid}.");
            return false;
        }

        var attr = player.Attributes.GetOrCreate(AttrIds.Achievement.QuestGroupId, questId);
        if (GetProgress(attr.Val) >= quest.CheckValue)
            return false;

        attr.Val = Pack(quest.CheckValue, IsReceived(attr.Val));
        player.Attributes.SyncTo(sync, attr);

        Log.Info($"Alien defense level {levelId} cleared by uid={player.Uid}, quest {questId} finished.");
        return true;
    }

    /// <summary>
    /// Pays out every claimable quest of the request. Rows outside the act's allowlist, already
    /// claimed rows and unfinished rows are skipped (the client checks the same attributes first,
    /// so a mismatch only means a stale UI), and the claimed bit is flipped on the ones that paid.
    /// </summary>
    public static async Task<JsonArray> ClaimAsync(
        PlayerInstance player,
        IEnumerable<uint> questIds,
        uint actId,
        NtfSyncPlayer sync)
    {
        var granted = new JsonArray();
        var claimedAny = false;

        foreach (var questId in questIds.Distinct())
        {
            if (!GameData.ActivityQuestData.TryGetValue(questId, out var quest))
            {
                Log.Warn($"Alien defense reward: unknown quest {questId} requested by uid={player.Uid}.");
                continue;
            }

            if (!IsAlienDefenseQuest(questId, actId))
            {
                Log.Warn($"Alien defense reward: quest {questId} is not an alien defense row, ignored for uid={player.Uid}.");
                continue;
            }

            var attr = player.Attributes.GetOrCreate(AttrIds.Achievement.QuestGroupId, questId);
            if (IsReceived(attr.Val))
            {
                Log.Warn($"Alien defense reward: quest {questId} already claimed for uid={player.Uid}.");
                continue;
            }

            var progress = GetProgress(attr.Val);
            if (progress < quest.CheckValue)
            {
                Log.Warn($"Alien defense reward: quest {questId} not finished " +
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

            Log.Info($"Alien defense reward: quest {questId} claimed, {rewards.Count} row(s) granted for uid={player.Uid}.");
        }

        if (claimedAny)
        {
            DatabaseHelper.SaveDatabaseType(player.Data);
            DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
            DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
        }

        return granted;
    }

    private static bool ParamContains(JToken? param, uint levelId)
    {
        if (param is JArray array)
        {
            foreach (var entry in array)
            {
                var raw = entry?.ToString().Trim().Trim('[', ']', '"') ?? string.Empty;
                if (uint.TryParse(raw, out var value) && value == levelId)
                    return true;
            }
        }

        return false;
    }
}
