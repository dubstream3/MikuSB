using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Activity;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

// 心愿寄语 领取寄语奖励 (Dorm / RoleFestival).
//
// Client origin: RoleFestivalMessage.GetLevelReward (Activity/RoleFestivalMessage.lua:289-313) checks
// the act id, the claimed flag and the read count locally, then shows the connection spinner and
// sends { nId, nMessageNum }. The spinner is only closed by the s2c callback
// "GetRoleFestivalLevelRewardSuccess" (:317-327), which hands tbParam.tbRewards straight to
// Item.Gain (:320 -> Item/Item.lua:639 opens the "GainItem" UI) and refreshes the reward window, so
// the body must carry the GDPL rows in the shape RewardManager already returns.
//
// Claimed tiers are remembered as (72, 100 + MessageNum) - the exact attribute the reward list and
// the module red point read (:91, umg_role_festival_reward.lua:38).
//
// Request:  { "nId": <actId>, "nMessageNum": <required read count> }
// Response: { "nId": ..., "nMessageNum": ..., "tbRewards": [[g,d,p,l,count], ...] }
//           dispatched as GetRoleFestivalLevelRewardSuccess
[CallGSApi("Activity_GetRoleFestivalLevelReward")]
public class Activity_GetRoleFestivalLevelReward : CallGSHandler
{
    private static readonly Logger Log = new(nameof(Activity_GetRoleFestivalLevelReward));

    private const string SuccessApi = "GetRoleFestivalLevelRewardSuccess";

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player!;
        var request = ParseObject(param);
        var actId = ReadUint(request?["nId"]);
        var messageNum = ReadUint(request?["nMessageNum"]);

        var currentAct = RoleFestivalService.GetActId(player);
        if (actId == 0 || actId != currentAct || messageNum == 0)
        {
            Log.Warn(
                $"Role festival reward request act={actId} (current {currentAct}) tier={messageNum} " +
                $"from uid={player.Uid} rejected.");
            return CallGSResult.Ok(
                new JsonObject { ["sError"] = "error.BadParam" }.ToJsonString(), null, SuccessApi);
        }

        var response = new JsonObject
        {
            ["nId"] = actId,
            ["nMessageNum"] = messageNum,
            ["tbRewards"] = new JsonArray()
        };

        if (player.Attributes.GetValue(RoleFestivalService.GroupId,
                RoleFestivalService.RewardSid(messageNum)) == 1)
        {
            // The client hides claimed tiers, but a reconnect or a stale window can re-send the
            // request; answer with an empty reward list so nothing is handed out twice.
            Log.Info($"Role festival tier {messageNum} already claimed by uid={player.Uid}.");
            return CallGSResult.Ok(response.ToJsonString(), null, SuccessApi);
        }

        var row = RoleFestivalService.FindReward(actId, messageNum);
        if (row == null)
        {
            Log.Warn(
                $"Role festival act {actId} has no reward row for tier {messageNum} (uid={player.Uid}).");
            return CallGSResult.Ok(
                new JsonObject { ["sError"] = "error.BadParam" }.ToJsonString(), null, SuccessApi);
        }

        var read = RoleFestivalService.CountRead(player, actId);
        if (read < messageNum)
        {
            Log.Warn(
                $"Role festival tier {messageNum} needs {messageNum} read letters, uid={player.Uid} " +
                $"has {read}; refusing to grant.");
            return CallGSResult.Ok(
                new JsonObject { ["sError"] = "error.BadParam" }.ToJsonString(), null, SuccessApi);
        }

        var sync = new NtfSyncPlayer();
        var rows = DlcRuntimeOverrides.ReadRewardRows(row.Rewards);
        var granted = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);

        var claimAttr = player.Attributes.Set(RoleFestivalService.GroupId,
            RoleFestivalService.RewardSid(messageNum), 1);
        player.Attributes.SyncTo(sync, claimAttr);
        DatabaseHelper.SaveDatabaseType(player.Data);

        response["tbRewards"] = granted;

        Log.Info(
            $"Role festival act {actId} tier {messageNum} claimed by uid={player.Uid}: " +
            $"{rows.Count} configured row(s), {granted.Count} granted, {read} letter(s) read.");

        return CallGSResult.Ok(response.ToJsonString(), sync, SuccessApi);
    }

    private static JsonObject? ParseObject(string param)
    {
        try
        {
            return string.IsNullOrWhiteSpace(param) ? null : JsonNode.Parse(param) as JsonObject;
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not parse the role festival reward request: {ex.Message}");
            return null;
        }
    }

    private static uint ReadUint(JsonNode? node)
    {
        if (node is not JsonValue value)
            return 0;

        try
        {
            if (value.TryGetValue<uint>(out var number))
                return number;

            if (value.TryGetValue<string>(out var text))
                return uint.TryParse(text.Trim(), out var parsed) ? parsed : 0;
        }
        catch
        {
            // unreadable -> 0
        }

        return 0;
    }
}
