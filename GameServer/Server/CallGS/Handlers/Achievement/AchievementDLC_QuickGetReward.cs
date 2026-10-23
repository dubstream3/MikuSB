using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Achievement;

// DLC mission board "claim all" (Achievement_DLC.lua:250 QuickGetReward). The client flattens one
// level of nesting out of tbParam.tbRewards (Achievement_DLC.lua:286-292), so the response is a
// list of per-mission reward lists rather than a single flat list.
// Request:  { tbIdList: [nId, ...] }
// Response: { tbRewards: [ [ [G, D, P, L, Count], ... ], ... ] }
[CallGSApi("AchievementDLC_QuickGetReward")]
public class AchievementDLC_QuickGetReward : CallGSHandler<AchievementDlcQuickGetRewardParam>
{
    protected override async Task<CallGSResult> HandleAsync(
        CallGSContext context,
        AchievementDlcQuickGetRewardParam request)
    {
        var player = context.Connection.Player!;
        var sync = new NtfSyncPlayer();
        var all = new JsonArray();

        foreach (var id in request.IdList ?? [])
        {
            if (!GameData.DlcMissionData.TryGetValue(id, out var mission))
                continue;

            var rewards = await DlcMissionService.ClaimAsync(player, mission, sync);
            if (rewards == null)
                continue;

            all.Add(rewards);
        }

        var payload = new JsonObject { ["tbRewards"] = all };
        return CallGSResult.Ok(payload.ToJsonString(), sync);
    }
}

public sealed class AchievementDlcQuickGetRewardParam
{
    [JsonPropertyName("tbIdList")] public List<uint>? IdList { get; set; }
}
