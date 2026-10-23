using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Achievement;

// DLC mission board claim (Achievement_DLC.lua:224 GetReward -> AchievementDLC_GetReward).
// The client checks IsReceive/IsFinished locally first, then expects the granted rows back as
// tbParam.tbRewards, which it feeds straight into UI.Open('GainItem', tbParam.tbRewards).
// Request:  { nId }
// Response: { tbRewards: [ [G, D, P, L, Count], ... ] }
[CallGSApi("AchievementDLC_GetReward")]
public class AchievementDLC_GetReward : CallGSHandler<AchievementDlcGetRewardParam>
{
    protected override async Task<CallGSResult> HandleAsync(
        CallGSContext context,
        AchievementDlcGetRewardParam request)
    {
        var player = context.Connection.Player!;

        if (request.Id == 0 || !GameData.DlcMissionData.TryGetValue(request.Id, out var mission))
            return CallGSResult.Error("error.BadParam");

        var sync = new NtfSyncPlayer();
        var rewards = await DlcMissionService.ClaimAsync(player, mission, sync);
        if (rewards == null)
            return CallGSResult.Error("achievement.NotFinished");

        var payload = new JsonObject { ["tbRewards"] = rewards };
        return CallGSResult.Ok(payload.ToJsonString(), sync);
    }
}

public sealed class AchievementDlcGetRewardParam
{
    [JsonPropertyName("nId")] public uint Id { get; set; }
}
