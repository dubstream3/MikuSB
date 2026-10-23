using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.GameServer.Game.Activity;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

// 回归活动 (player return) reward claim. Activity/PlayerReturn.lua sends one request for all three
// tabs and picks the branch with nType:
//
//   {nId = actId, nType = 1}                                   first/trigger reward (:172)
//   {nId = actId, nType = 2}                                   sign-in reward (:180)
//   {nId = actId, bQuick = bQuick, missionId = nId, nType = 3} mission/achievement reward (:209)
//
// and the callback registered under the very same name (:212) pops the reward window when
// bShowReward is set and always forwards tbParam to umg_actvityreturn.Refresh -> the active tab's
// Show(tbParam). The sign tab renders tbParam.tbSignDay (the day ids just signed) and shows the
// items itself after a 0.6 s delay (uw_activityreturn01.lua:48), so the sign branch answers with
// bShowReward = false to avoid opening the reward window twice.
//
// Request:  { nId, nType, bQuick, missionId }
// Response: { bShowReward, tbRewards, tbSignDay }
[CallGSApi("PlayerReturn_GetReward")]
public class PlayerReturn_GetReward : CallGSHandler<PlayerReturnGetRewardParam>
{
    private static readonly Logger Log = new(nameof(PlayerReturn_GetReward));

    protected override async Task<CallGSResult> HandleAsync(
        CallGSContext context,
        PlayerReturnGetRewardParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return CallGSResult.Error("error.BadParam");

        var sync = new NtfSyncPlayer();

        // Re-seed and re-roll first: the claim below reads the very attributes this writes, and a
        // player who reached the window without a login roll (hot reload) would otherwise claim
        // against a zeroed act id.
        PlayerReturnService.Ensure(player, sync);

        var actId = PlayerReturnService.GetActId(player);
        if (actId == 0 || (request.ActId != 0 && request.ActId != actId))
        {
            Log.Warn($"Return reward: request for act {request.ActId} but stored act is {actId} " +
                     $"for uid={player.Uid}.");
            return CallGSResult.Error("error.BadParam", sync);
        }

        var payload = new JsonObject { ["bShowReward"] = false };
        var signDays = new JsonArray();

        switch (request.Type)
        {
            case 1:
            {
                var granted = await PlayerReturnService.ClaimFirstAsync(player, actId, sync);
                payload["bShowReward"] = granted.Count > 0;
                payload["tbRewards"] = granted;
                break;
            }

            case 2:
            {
                var (granted, days) = await PlayerReturnService.ClaimSignAsync(player, actId, sync);
                foreach (var day in days)
                    signDays.Add(day);

                // The sign tab shows the items itself; only the payload has to carry them.
                payload["tbRewards"] = granted;
                break;
            }

            case 3:
            {
                var granted = await PlayerReturnService.ClaimMissionsAsync(
                    player, actId, request.Quick, request.MissionId, sync);
                payload["bShowReward"] = granted.Count > 0;
                payload["tbRewards"] = granted;
                break;
            }

            default:
                Log.Warn($"Return reward: unknown nType {request.Type} for uid={player.Uid}.");
                return CallGSResult.Error("error.BadParam", sync);
        }

        payload["tbSignDay"] = signDays;

        return CallGSResult.Ok(payload, sync);
    }
}

public sealed class PlayerReturnGetRewardParam
{
    [JsonPropertyName("nId")] public uint ActId { get; set; }

    /// <summary>1 = trigger reward, 2 = sign-in reward, 3 = mission/achievement reward.</summary>
    [JsonPropertyName("nType")] public uint Type { get; set; }

    /// <summary>Mission tab's quick-claim flag; only meaningful for nType 3.</summary>
    [JsonPropertyName("bQuick")] public bool Quick { get; set; }

    /// <summary>Achievement quest id for a single mission claim; 0 with bQuick.</summary>
    [JsonPropertyName("missionId")] public uint MissionId { get; set; }
}
