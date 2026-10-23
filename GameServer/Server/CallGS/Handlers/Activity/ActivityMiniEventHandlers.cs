using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Achievement;
using MikuSB.GameServer.Game.Activity;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using Newtonsoft.Json.Linq;
using System.Text.Json.Nodes;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

// 7 天乐 (new-player board, activity 5) + the two mini activities that only exist behind the activity
// list. None of them is reachable through a DlcFunc, all of them are reached from the activity entry,
// and every one of them raises a spinner before sending:
//
//   Seven_day_getAchievementAward  Activity/SevenDay.lua:265  {achievementId, actId, index} -> GetAchievementAwardRsp_SevenDay
//   Seven_day_getIndexAward        Activity/SevenDay.lua:273  {actId, index}                -> GetIndexAwardRsp_SevenDay
//   Seven_day_getAllRewardCanGet   Activity/SevenDay.lua:281  {actId, tbAchieveInfo, tbIndexId} -> GetAllAwardRsp_SevenDay
//   VigourSupply_CheckRefresh      Activity/VigourSupply.lua:70  (empty)   -> VigourSupply_CheckRefresh
//   VigourSupply_ReceiveVigour     Activity/VigourSupply.lua:84  {nId}     -> VigourSupply_ReceiveVigour
//   PlayerCharge_GetAward          Activity/PlayerCharge.lua:53  (empty)   -> PlayerCharge_GetAward
//   PlayerCharge_Switch            Activity/PlayerCharge.lua:61  bare 0|1  -> PlayerCharge_Switch

/// <summary>
/// One achievement of the 7 day board. The reward rows come from achievement/activity_quest.json
/// (group 7, sid = quest id, bit0 claimed / bits1..31 progress) and the client displays them from its
/// own copy (SevenDay.lua:289-292), so the reply only has to echo the id it just paid for.
///
/// index is the day id (1..7), not an array position - SevenDay.lua:259-266 passes it straight from
/// the tab, and the achievements table is keyed (ActId, DayId).
/// </summary>
[CallGSApi("Seven_day_getAchievementAward")]
public class Seven_day_getAchievementAward : CallGSHandler<SevenDayAchievementParam>
{
    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, SevenDayAchievementParam request)
    {
        var player = context.Connection.Player!;
        var sync = new NtfSyncPlayer();
        var response = new JsonObject { ["achievementId"] = request.AchievementId };
        var granted = new JsonArray();

        if (GameData.ActivityQuestData.TryGetValue(request.AchievementId, out var quest))
        {
            var rows = AchievementClaimService.ReadRewardRows(quest.Rewards);
            var result = await AchievementClaimService.TryClaimAsync(
                player, AchievementClaimService.QuestGroupId, request.AchievementId, quest.CheckValue, rows, sync);
            if (result != null)
                foreach (var row in result)
                    granted.Add(row?.DeepClone());
        }
        else
        {
            Logger.GetByClassName().Warn(
                $"Seven_day_getAchievementAward: no activity_quest row {request.AchievementId}.");
        }

        if (granted.Count > 0)
        {
            response["tbRewards"] = granted;
            DatabaseHelper.SaveDatabaseType(player.Data);
            DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
            DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
        }

        Logger.GetByClassName().Info(
            $"Seven_day achievement {request.AchievementId} (act {request.ActId}, day {request.Index}) " +
            $"for uid={player.Uid}: {granted.Count} reward row(s).");

        return CallGSResult.Ok(response.ToJsonString(), sync, "GetAchievementAwardRsp_SevenDay");
    }
}

public sealed class SevenDayAchievementParam
{
    [System.Text.Json.Serialization.JsonPropertyName("achievementId")]
    public uint AchievementId { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("actId")]
    public uint ActId { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("index")]
    public uint Index { get; set; }
}

/// <summary>
/// One stage of the point bar. index is the stage number 1..7 (equal to AwardId because
/// seven_day_awards.json happens to number them 1..7), NeedPoint comes from that row and the point
/// value is the 雪之形 balance (SevenDay.lua:134).
///
/// The client displays cfg.itemAward from its own table after the callback (SevenDay.lua:298-301), so
/// echoing actId + index is enough - but both keys are required: GetIndexAwardRsp_SevenDay reads
/// tbParam.actId and tbParam.index, and a missing index silently skips the reward panel.
/// </summary>
[CallGSApi("Seven_day_getIndexAward")]
public class Seven_day_getIndexAward : CallGSHandler<SevenDayIndexParam>
{
    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, SevenDayIndexParam request)
    {
        var player = context.Connection.Player!;
        var sync = new NtfSyncPlayer();
        var response = new JsonObject { ["actId"] = request.ActId, ["index"] = request.Index };
        var granted = new JsonArray();

        var actId = request.ActId == 0 ? SevenDayService.ActId : request.ActId;

        if (!GameData.SevenDayAwardData.TryGetValue((actId, request.Index), out var award))
        {
            return CallGSResult.Ok(response.ToJsonString(), sync, "GetIndexAwardRsp_SevenDay");
        }

        var points = SevenDayService.Points(player);
        if (points < award.NeedPoint || SevenDayService.IsStageAwarded(player, request.Index))
        {
            Logger.GetByClassName().Info(
                $"Seven_day stage {request.Index} not claimable for uid={player.Uid}: " +
                $"points={points}/{award.NeedPoint}, already={SevenDayService.IsStageAwarded(player, request.Index)}.");
            return CallGSResult.Ok(response.ToJsonString(), sync, "GetIndexAwardRsp_SevenDay");
        }

        // seven_day_awards.json stores ItemAward as ONE flat 5 element row ([4,3,1,1,10000]), not as a
        // list of rows like every other reward column in the project. ReadRewardRows only walks a list
        // of rows, so wrapping the row is what actually makes the stage pay out.
        var rows = award.ItemAward is JArray
            ? DlcRuntimeOverrides.ReadRewardRows(new JArray(award.ItemAward))
            : [];
        var result = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);
        foreach (var row in result)
            granted.Add(row?.DeepClone());

        SevenDayService.MarkStageAwarded(player, request.Index, sync);

        if (granted.Count > 0)
        {
            response["tbRewards"] = granted;
            DatabaseHelper.SaveDatabaseType(player.Data);
            DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
            DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
        }

        Logger.GetByClassName().Info(
            $"Seven_day stage {request.Index} (act {actId}) claimed by uid={player.Uid}: " +
            $"{granted.Count} reward row(s), points={points}.");

        return CallGSResult.Ok(response.ToJsonString(), sync, "GetIndexAwardRsp_SevenDay");
    }
}

public sealed class SevenDayIndexParam
{
    [System.Text.Json.Serialization.JsonPropertyName("actId")]
    public uint ActId { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("index")]
    public uint Index { get; set; }
}

/// <summary>
/// "一键领取" of the board. The client passes the ids it believes are claimable but the server re-scans
/// the whole board, so a stale client list can never skip or duplicate a row.
///
/// Reply shape is a MAP, not a list: GetAllAwardRsp_SevenDay does "for _, tbReward in pairs(tbParam)"
/// and then indexes v[1..4] of every inner element (SevenDay.lua:313-328), so each key must map to an
/// array of 5 element rows. A flat list would make it index a number and throw
/// "attempt to concatenate a nil value".
/// </summary>
[CallGSApi("Seven_day_getAllRewardCanGet")]
public class Seven_day_getAllRewardCanGet : CallGSHandler
{
    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player!;
        var sync = new NtfSyncPlayer();
        var response = new JsonObject();

        var claimed = 0;
        foreach (var questId in SevenDayService.QuestIds())
        {
            if (!GameData.ActivityQuestData.TryGetValue(questId, out var quest))
                continue;

            var rows = AchievementClaimService.ReadRewardRows(quest.Rewards);
            var result = await AchievementClaimService.TryClaimAsync(
                player, AchievementClaimService.QuestGroupId, questId, quest.CheckValue, rows, sync);
            if (result == null)
                continue;

            response[questId.ToString()] = result;
            claimed++;
        }

        if (claimed > 0)
        {
            DatabaseHelper.SaveDatabaseType(player.Data);
            DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
            DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
        }

        Logger.GetByClassName().Info(
            $"Seven_day all-reward claim for uid={player.Uid}: {claimed} quest(s) paid out " +
            $"(request='{(string.IsNullOrWhiteSpace(param) ? "{}" : param)}').");

        return CallGSResult.Ok(response.ToJsonString(), sync, "GetAllAwardRsp_SevenDay");
    }
}

/// <summary>
/// Vigour meal refresh. The reply payload is never read (VigourSupply.lua:72-78 only closes the
/// spinner), but it MUST be sent: template08:149-155 re-sends the request every second while the
/// stored next-reset stamp is 0, so the handler also seeds that stamp.
/// </summary>
[CallGSApi("VigourSupply_CheckRefresh")]
public class VigourSupply_CheckRefresh : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player!;
        var sync = new NtfSyncPlayer();

        VigourSupplyService.Seed(player, sync);

        return Task.FromResult(CallGSResult.Ok(new JsonObject().ToJsonString(), sync, "VigourSupply_CheckRefresh"));
    }
}

/// <summary>
/// Claim one free vigour meal.
///
/// Reply contract is unusual: the client does "if #tbParam > 0 then UI.Open('GainItem', {tbParam})"
/// (VigourSupply.lua:92), i.e. it wraps the payload in ANOTHER table, so the payload itself must be
/// the single flat row [MoneyType_Vigour, amount] - a list of rows would nest one level too deep and
/// make umg_general_gain_tips index a table as a number.
/// </summary>
[CallGSApi("VigourSupply_ReceiveVigour")]
public class VigourSupply_ReceiveVigour : CallGSHandler<VigourSupplyReceiveParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, VigourSupplyReceiveParam request)
    {
        var player = context.Connection.Player!;
        var sync = new NtfSyncPlayer();

        if (!GameData.VigourSupplyData.TryGetValue(request.Id, out var row))
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        if (VigourSupplyService.IsClaimed(player, request.Id))
            return Task.FromResult(CallGSResult.Error("ui.TxtOverGet"));

        if (!VigourSupplyService.InWindow(row, DateTime.Now))
            return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));

        var row2 = new JsonArray { AttrIds.Currency.Vigor, row.VigourNum };
        player.AddCurrency(AttrIds.Currency.Vigor, row.VigourNum, sync);
        VigourSupplyService.MarkClaimed(player, request.Id, sync);
        VigourSupplyService.Seed(player, sync);

        DatabaseHelper.SaveDatabaseType(player.Data);

        Logger.GetByClassName().Info(
            $"VigourSupply meal {request.Id}: uid={player.Uid} +{row.VigourNum} vigour.");

        return Task.FromResult(CallGSResult.Ok(row2.ToJsonString(), sync, "VigourSupply_ReceiveVigour"));
    }
}

public sealed class VigourSupplyReceiveParam
{
    [System.Text.Json.Serialization.JsonPropertyName("nId")]
    public uint Id { get; set; }
}

/// <summary>
/// Cumulative top-up claim. Nothing in the MallCharge page can be claimed on this server because no
/// real payment is ever recorded, so the answer is an empty tbRewards - but with the right key, so the
/// panel closes instead of surfacing ui.TxtNotOpen (PlayerCharge.lua:67-74 tolerates an empty list).
/// The same routine pays out when (0,18) is non-zero, e.g. after a GM grant.
/// </summary>
[CallGSApi("PlayerCharge_GetAward")]
public class PlayerCharge_GetAward : CallGSHandler
{
    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player!;
        var sync = new NtfSyncPlayer();
        var rewards = new JsonArray();
        var charged = PlayerChargeService.Charged(player);

        foreach (var pair in GameData.PlayerChargeData.OrderBy(p => p.Value.Num))
        {
            var tier = pair.Value;
            if (tier.Num > charged || PlayerChargeService.IsClaimed(player, tier.Index))
                continue;

            var rows = DlcRuntimeOverrides.ReadRewardRows(tier.Item);
            var granted = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);
            foreach (var row in granted)
                rewards.Add(row?.DeepClone());

            PlayerChargeService.MarkClaimed(player, tier.Index, sync);
        }

        if (rewards.Count > 0)
        {
            DatabaseHelper.SaveDatabaseType(player.Data);
            DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
            DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
        }

        Logger.GetByClassName().Info(
            $"PlayerCharge_GetAward uid={player.Uid}: charged={charged}, {rewards.Count} reward row(s).");

        return CallGSResult.Ok(new JsonObject { ["tbRewards"] = rewards }.ToJsonString(),
            sync, "PlayerCharge_GetAward");
    }
}

/// <summary>
/// Toggles "打码/显示金额" in the top-up page. The request body is a BARE NUMBER
/// (PlayerCharge.lua:61 json.encode(0|1)), so the generic CallGSHandler&lt;T&gt; path would fail to
/// deserialize it and answer error.BadParam - hence the non-generic handler reading RawParam.
/// The reply is ignored by the client, but it still has to be sent to close the page's own flow.
/// </summary>
[CallGSApi("PlayerCharge_Switch")]
public class PlayerCharge_Switch : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player!;
        var sync = new NtfSyncPlayer();
        var raw = (param ?? string.Empty).Trim();
        var on = raw is "1" or "true";

        PlayerChargeService.SetShowAmounts(player, on, sync);
        DatabaseHelper.SaveDatabaseType(player.Data);

        Logger.GetByClassName().Info(
            $"PlayerCharge_Switch uid={player.Uid}: raw='{raw}' -> showAmounts={on}.");

        return Task.FromResult(CallGSResult.Ok(new JsonObject().ToJsonString(), sync, "PlayerCharge_Switch"));
    }
}
