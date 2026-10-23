using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Achievement;
using MikuSB.GameServer.Game.Activity;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using System.Text.Json.Nodes;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

// 领取活动任务奖励 (Activity/Activity.lua:1360 Activity.Quest_GetAward). The client shows its
// connection spinner first (:1370 UI.ShowConnection(); :1371 me:CallGS("Activity_GetAward")) and only
// the matching s2c callback closes it again, so the answer name depends on what was asked for:
//
//   nType == 1  Sign.AWARD_TYPE_DAY    (Activity/Sign.lua:16) -> "DaySign"   (registered :407)
//   nType == 2  Sign.AWARD_TYPE_AMOUNT (Activity/Sign.lua:17) -> "ShortSign" (registered :499)
//   otherwise                                                  -> "ActivityQuest_GetReward" (:1444)
//
// The daily sign-in MUST NOT be answered with ActivityQuest_GetReward. That callback forwards the
// payload to sUI:OnReceiveUpdate, which ends in umg_activity:OnServerRefresh -> OnClickItem ->
// ShowRightInfo -> Widget:OnOpen - it re-opens the sign-in widget, whose OnOpen
// (UMG/Activitydate/uw_activitydate.lua:50) sends another request while the status is still below 2.
// With no state change on the server that is an unbounded loop that allocates a fresh connection
// widget per round trip and eventually kills the client with
// "Ran out of memory allocating 1576960 bytes" (2026-10-05 22:50 crash).
[CallGSApi("Activity_GetAward")]
public class Activity_GetAward : CallGSHandler
{
    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player!;
        var request = JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject
                      ?? new JsonObject();

        var activityId = ReadUint(request["nId"]);
        var awardType = ReadUint(request["nType"]);

        Logger.GetByClassName().Info(
            $"Activity reward request nId={activityId} nType={awardType} for uid={player.Uid}.");

        var response = new JsonObject();
        foreach (var pair in request)
            response[pair.Key] = pair.Value?.DeepClone();

        // 特别邀请 / 自选 SSR (Class=free_items): those widgets send nId only and wait for
        // Event.ActivityReward, which only the Activity_UpdateActiveWidget callback fires - see
        // ActivityFreeItemsService for the full contract and for why the splash re-opened 89 times.
        if (awardType == 0 && request["nQuestId"] is null &&
            GameData.ActivityData.TryGetValue(activityId, out var freeItems) &&
            ActivityFreeItemsService.IsFreeItems(freeItems))
        {
            var claimSync = new NtfSyncPlayer();
            var granted = await ActivityFreeItemsService.ClaimAsync(player, freeItems, claimSync);
            response["tbRewards"] = granted;
            return CallGSResult.Ok(response.ToJsonString(), claimSync, "Activity_UpdateActiveWidget");
        }

        return awardType switch
        {
            ActivitySignService.AwardTypeDay => await GrantDailySignAsync(player, activityId, response),
            ActivitySignService.AwardTypeAmount => await GrantShortSignAsync(player, activityId, response),
            _ => await ClaimQuestAsync(player, activityId, request, response)
        };
    }

    /// <summary>
    /// Short sign-in (短签) - the 7 day check-in card every DLC era event ships
    /// (activity/sign/signshort.json, DayId 1..7, day 7 being the Special=2 "big" row).
    ///
    /// Why this branch exists: the widget auto-sends this request the moment it opens
    /// (UMG/ActivitySign14/umg_weeksign1.lua:113-115 AutoSign while Sign.GetSginDayStatus &lt; 2) and
    /// before this branch the handler answered "ShortSign" with the request echoed back - the spinner
    /// closed, Item.Gain showed nothing, the day counter never moved and the widget re-sent on every
    /// open (the "活动打脸奖励领不到" report). The client also refuses to send at all while
    /// Sign.GetSginTag(id) is 0 (:467 requires tbReward[tag] to exist), so the counter has to be
    /// advanced by the server.
    ///
    /// Day bookkeeping is the client's own mapping:
    ///   (50, id*10+3) = signed-day counter   (Sign.SignTag_TaskId = 1)
    ///   (50, id*10+4) = today's status       (Sign.SignDay_TaskId = 2, 0 pending / 2 shown)
    /// Sign.CheckEnd (:230-254) only advances the day while the status is 0 or 3.
    ///
    /// A DLC era card is 7 days long, but a replicated era never hands the player 7 real days (and
    /// several windows are already over), so one claim grants every remaining day at once - up to and
    /// including day 7 - and parks the counter on the last day. That is the requested "直接把奖励发到
    /// 第七天": a single click fills the whole card. Re-claiming afterwards re-enters this method with
    /// tag == maxDay and answers an empty tbRewards, so re-opening the widget, re-pinning an era or
    /// logging in again can never pay out twice - the counter lives in group 50 and no era switch
    /// resets it (DlcRuntimeOverrides only rewrites windows).
    /// </summary>
    private static async Task<CallGSResult> GrantShortSignAsync(
        PlayerInstance player,
        uint activityId,
        JsonObject response)
    {
        var sync = new NtfSyncPlayer();
        var rewards = new JsonArray();

        var days = new SortedDictionary<uint, Newtonsoft.Json.Linq.JToken?>();
        foreach (var pair in GameData.SignShortData)
        {
            if (pair.Key.ActivityId != activityId)
                continue;

            days[pair.Key.DayId] = pair.Value.SignReward;
        }

        if (days.Count == 0)
        {
            // Not a short-sign card, or the table did not load: still answer on the ShortSign callback
            // so the client's spinner closes instead of staying up forever.
            Logger.GetByClassName().Warn(
                $"Activity {activityId} has no signshort row; answering ShortSign without a reward.");
            response["tbRewards"] = rewards;
            return CallGSResult.Ok(response.ToJsonString(), sync, "ShortSign");
        }

        var maxDay = days.Keys.Max();
        var tag = player.Attributes.GetValue(ActivitySignService.GroupId,
            ActivitySignService.SignedDaySid(activityId));
        var status = player.Attributes.GetValue(ActivitySignService.GroupId,
            ActivitySignService.TodayStatusSid(activityId));

        if (tag >= maxDay)
        {
            // Card already full. Keep the status at "shown" so the widget stops auto-sending and stay
            // silent about rewards - this is the path a reopened window / replicated era hits.
            ActivitySignService.SetAttr(player, ActivitySignService.TodayStatusSid(activityId),
                ActivitySignService.StatusSigned, sync);
            response["tbRewards"] = rewards;

            Logger.GetByClassName().Info(
                $"Short sign activity {activityId} already complete for uid={player.Uid} " +
                $"(day {tag}/{maxDay}); no reward.");
            return CallGSResult.Ok(response.ToJsonString(), sync, "ShortSign");
        }

        var startDay = status is ActivitySignService.StatusPending or 3 ? tag + 1 : tag;
        if (startDay == 0)
            startDay = 1;
        if (startDay > maxDay)
            startDay = maxDay;

        foreach (var (day, rewardJson) in days)
        {
            if (day < startDay)
                continue;

            var rows = DlcRuntimeOverrides.ReadRewardRows(rewardJson);
            var granted = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);
            foreach (var row in granted)
                rewards.Add(row?.DeepClone());
        }

        ActivitySignService.SetAttr(player, ActivitySignService.SignedDaySid(activityId), maxDay, sync);
        ActivitySignService.SetAttr(player, ActivitySignService.TodayStatusSid(activityId),
            ActivitySignService.StatusSigned, sync);
        ActivitySignService.SetAttr(player, ActivitySignService.LastSignDateSid(activityId),
            ActivitySignService.TodayStamp(), sync);

        response["tbRewards"] = rewards;

        Logger.GetByClassName().Info(
            $"Short sign activity {activityId} for uid={player.Uid}: granted day {startDay}..{maxDay} " +
            $"({rewards.Count} reward row(s)), counter now {maxDay}.");

        DatabaseHelper.SaveDatabaseType(player.Data);
        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
        DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);

        return CallGSResult.Ok(response.ToJsonString(), sync, "ShortSign");
    }

    /// <summary>
    /// Activity quest board single claim. Activity/Activity.lua:1359 documents the request as
    /// {nId(activity id), nType(optional), nQuestId(quest id)} and the widget sends exactly that
    /// (UMG/Activity/uw_activity_template21_list.lua:109-113 builds
    /// {nId = tbParam.nActivityId, nQuestId = tbParam.tbQuestInfo.nId}). The rows live in
    /// achievement/activity_quest.json, i.e. group 7, and use the same bit packing as the
    /// achievement board - so this reuses AchievementClaimService.
    ///
    /// Before this branch existed every nQuestId request fell into the echo-only default: the
    /// connection spinner closed on ActivityQuest_GetReward, but nothing was granted and the row
    /// stayed unclaimed. A refused claim (already claimed / not finished yet) answers with an
    /// empty tbRewards on the same callback, which is the client's honest-refusal shape.
    /// </summary>
    private static async ValueTask<CallGSResult> ClaimQuestAsync(
        PlayerInstance player,
        uint activityId,
        JsonObject request,
        JsonObject response)
    {
        var questId = ReadUint(request["nQuestId"]);
        var sync = new NtfSyncPlayer();
        var granted = new JsonArray();

        if (questId == 0)
        {
            Logger.GetByClassName().Warn(
                $"Activity reward request for activity {activityId} carried no usable nQuestId; " +
                "answering with an empty reward list.");
        }
        else if (GameData.ActivityQuestData.TryGetValue(questId, out var quest))
        {
            var rows = AchievementClaimService.ReadRewardRows(quest.Rewards);
            granted = await AchievementClaimService.TryClaimAsync(
                          player, AchievementClaimService.QuestGroupId, questId, quest.CheckValue, rows, sync)
                      ?? new JsonArray();

            Logger.GetByClassName().Info(
                $"Activity quest {questId} (activity {activityId}) claimed by uid={player.Uid}: " +
                $"{granted.Count} reward row(s), alreadyClaimed=" +
                $"{AchievementClaimService.IsClaimed(player, AchievementClaimService.QuestGroupId, questId)}.");
        }
        else
        {
            Logger.GetByClassName().Warn(
                $"No activity_quest row {questId} for activity {activityId}; answering without a reward.");
        }

        response["tbRewards"] = granted;
        return CallGSResult.Ok(response.ToJsonString(), sync, "ActivityQuest_GetReward");
    }

    /// <summary>
    /// Daily sign-in. Grants the row for "the next unsigned day" and marks today as done, matching
    /// Sign.CheckEnd (Activity/Sign.lua:230): while the status is 0 or 3 the day number is the stored
    /// counter + 1, so the counter itself is what the client calls Sign.GetSginTag.
    /// </summary>
    private static async Task<CallGSResult> GrantDailySignAsync(
        PlayerInstance player,
        uint activityId,
        JsonObject response)
    {
        var sync = new NtfSyncPlayer();
        var stamp = ActivitySignService.TodayStamp();
        var lastSid = ActivitySignService.LastSignDateSid(activityId);
        var rewarded = new JsonArray();

        if (player.Attributes.GetValue(ActivitySignService.GroupId, lastSid) == stamp)
        {
            // Already granted today - the client re-sent the request (reconnect, re-opened window).
            // Report the day as done so the loop stops, but never hand the reward out twice.
            ActivitySignService.SetAttr(player, ActivitySignService.TodayStatusSid(activityId),
                ActivitySignService.StatusSigned, sync);
        }
        else
        {
            var day = player.Attributes.GetValue(ActivitySignService.GroupId,
                ActivitySignService.SignedDaySid(activityId)) + 1;

            if (GameData.SignDayData.TryGetValue((activityId, day), out var row))
            {
                var rows = DlcRuntimeOverrides.ReadRewardRows(row.SignReward);
                rewarded = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);
            }
            else
            {
                Logger.GetByClassName().Warn(
                    $"No signday row for activity {activityId} day {day}; answering without a reward.");
            }

            ActivitySignService.SetAttr(player, ActivitySignService.SignedDaySid(activityId), day, sync);
            ActivitySignService.SetAttr(player, ActivitySignService.TodayStatusSid(activityId),
                ActivitySignService.StatusSigned, sync);
            ActivitySignService.SetAttr(player, lastSid, stamp, sync);
        }

        response["tbRewards"] = rewarded;

        Logger.GetByClassName().Info(
            $"Daily sign-in activity {activityId} for uid={player.Uid}: granted {rewarded.Count} " +
            $"reward row(s), status={player.Attributes.GetValue(ActivitySignService.GroupId, ActivitySignService.TodayStatusSid(activityId))}, " +
            $"day={player.Attributes.GetValue(ActivitySignService.GroupId, ActivitySignService.SignedDaySid(activityId))}.");

        DatabaseHelper.SaveDatabaseType(player.Data);
        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
        DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);

        return CallGSResult.Ok(response.ToJsonString(), sync, "DaySign");
    }

    private static uint ReadUint(JsonNode? node)
    {
        if (node is not JsonValue value)
            return 0;

        return value.TryGetValue<uint>(out var parsed) ? parsed : 0;
    }
}
