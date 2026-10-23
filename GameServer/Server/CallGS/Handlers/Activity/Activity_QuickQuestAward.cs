using MikuSB.Data;
using MikuSB.GameServer.Game.Achievement;
using MikuSB.Proto;
using MikuSB.Util;
using System.Text.Json.Nodes;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

/// <summary>
/// 活动任务一键领取 (Activity quest board quick claim).
///
/// Client send point: Activity/Activity.lua:1602-1628 Activity.Quick_QuestAward(tbList, nActivityId)
/// builds tbCmd = { nId = nActivityId, tbIdList = tbList } and calls
/// me:CallGS("Activity_QuickQuestAward", json.encode(tbCmd)) at :1628. The callers are
/// Activity.lua:1581 (one completed row: Activity.Quick_QuestAward({info[2].nId}, tbConfig.nId)),
/// Activity.lua:1598 (tbList = nil -> the client collects every claimable quest of the board
/// itself, :1607-1626) and UMG/House/Mode/Widgets/uw_dorm_task.lua:14 (宿舍任务页).
///
/// Reply contract: Activity/Activity.lua:1631-1669 s2c.Register('Activity_QuickQuestAward', ...)
/// reads tbParam.tbRewards as ONE FLAT list of 5-element rows [G, D, P, L, Count]: it dedups on
/// string.format("%d-%d-%d-%d", info[1], info[2], info[3], info[4]) and sums info[5], then hands
/// the same payload to Activity:OnReceiveUpdate, DormTask:OnReceiveUpdate and
/// NewTask:OnReceiveUpdate. A nested per-quest array of rows - the shape
/// Achievement_QuickGetReward uses - would make the client index a table as a number here, so the
/// answer has to concatenate every granted row into a single array.
///
/// The rows live in achievement/activity_quest.json, i.e. the activity quest board is packed with
/// the achievement bit layout in group 7 (AttrIds.Achievement.QuestGroupId), NOT the activity
/// group 50 (AttrIds.Activity.QuestGroupId). AchievementClaimService owns that packing and is
/// reused as-is, so a quest that is not finished yet or was already claimed answers with an empty
/// tbRewards - the client's honest-refusal shape, which keeps the "一键领取" button enabled but
/// grants nothing.
/// </summary>
[CallGSApi("Activity_QuickQuestAward")]
public class Activity_QuickQuestAward : CallGSHandler
{
    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player!;
        var request = JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject
                      ?? new JsonObject();

        var activityId = ReadUint(request["nId"]);
        var questIds = ReadQuestIds(request["tbIdList"]);

        Logger.GetByClassName().Info(
            $"Activity quick quest award nId={activityId} tbIdList=[{string.Join(",", questIds)}] for uid={player.Uid}.");

        var response = new JsonObject();
        foreach (var pair in request)
            response[pair.Key] = pair.Value?.DeepClone();

        var sync = new NtfSyncPlayer();
        var granted = new JsonArray();
        var claimed = 0;
        var refused = 0;

        foreach (var questId in questIds)
        {
            if (!GameData.ActivityQuestData.TryGetValue(questId, out var quest))
            {
                refused++;
                Logger.GetByClassName().Warn(
                    $"Quick quest award: no achievement/activity_quest row {questId} (activity {activityId}).");
                continue;
            }

            var rows = AchievementClaimService.ReadRewardRows(quest.Rewards);
            var questRewards = await AchievementClaimService.TryClaimAsync(
                player, AchievementClaimService.QuestGroupId, questId, quest.CheckValue, rows, sync);

            // null means "already claimed" or "progress below CheckValue" - never a failure to report,
            // the client simply does not see that row in the flat list.
            if (questRewards == null)
            {
                refused++;
                continue;
            }

            claimed++;
            foreach (var row in questRewards)
                granted.Add(row?.DeepClone());
        }

        response["tbRewards"] = granted;

        Logger.GetByClassName().Info(
            $"Activity {activityId} quick quest award for uid={player.Uid}: claimed={claimed} " +
            $"refused={refused} reward row(s)={granted.Count}.");

        return CallGSResult.Ok(response.ToJsonString(), sync, "Activity_QuickQuestAward");
    }

    /// <summary>
    /// tbIdList is a plain array of quest ids in every call site (Activity.lua:1581 wraps a single id,
    /// :1607-1626 collects ids from the board, uw_dorm_task.lua:14 passes its own collected list).
    /// Objects carrying nId are accepted too, and duplicates are collapsed so one quest is never
    /// claimed twice in a single request.
    /// </summary>
    private static List<uint> ReadQuestIds(JsonNode? node)
    {
        var ids = new List<uint>();
        if (node is not JsonArray array)
            return ids;

        foreach (var item in array)
        {
            var id = item is JsonObject json ? ReadUint(json["nId"]) : ReadUint(item);
            if (id != 0 && !ids.Contains(id))
                ids.Add(id);
        }

        return ids;
    }

    private static uint ReadUint(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<uint>(out var parsed))
            return parsed;

        return uint.TryParse(node?.ToString(), out var text) ? text : 0;
    }
}
