using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.GameServer.Game.SeasonBoss;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.SeasonBoss;

// 弑神斗场 (season boss) reward claim. SeasonBossLogic.GetReward (Challenge/SeasonBoss/SeasonBoss.lua
// :1571-1578) is called with either a single task id (the per row button, umg_dungeonsseasonboss_main
// .lua:610) or the whole term's list (the quick claim button, :629):
//
//     { tbTaskID: [19001, 19002, ...] }
//
// and the callback (:1580-1589) feeds the response straight into Item.Gain and then refreshes the
// panel:
//
//     s2c.Register('SeasonBoss_GetReward', function(tbParam)
//         UI.CloseConnection()
//         if tbParam.tbAward then Item.Gain(tbParam.tbAward) end
//         ...
//     end)
//
// The field is tbAward and it is a FLAT list of [Genre, Detail, Particular, Level, Count] rows (what
// RewardEntry.ToJson produces), not the nested tbRewards shape the DLC achievement endpoint uses.
// A response is mandatory: the client opens a connection spinner first and only closes it here.
//
// Request:  { tbTaskID: [...] }
// Response: { tbAward: [[g, d, p, l, count], ...] }
[CallGSApi("SeasonBoss_GetReward")]
public class SeasonBoss_GetReward : CallGSHandler<SeasonBossGetRewardParam>
{
    private static readonly Logger Log = new(nameof(SeasonBoss_GetReward));

    protected override async Task<CallGSResult> HandleAsync(
        CallGSContext context,
        SeasonBossGetRewardParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return CallGSResult.Error("error.BadParam");

        var ids = request.TaskIds ?? [];
        if (ids.Count == 0)
            return CallGSResult.Ok(new JsonObject { ["tbAward"] = new JsonArray() });

        var sync = new NtfSyncPlayer();
        var granted = await SeasonBossQuestService.ClaimAsync(player, ids, sync);

        return CallGSResult.Ok(new JsonObject { ["tbAward"] = granted }, sync);
    }
}

public sealed class SeasonBossGetRewardParam
{
    [JsonPropertyName("tbTaskID")] public List<uint>? TaskIds { get; set; }
}
