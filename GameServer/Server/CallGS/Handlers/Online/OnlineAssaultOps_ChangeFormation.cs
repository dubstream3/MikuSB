using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Online;

// 进击行动 切换出战编队.
//
// Client origin: OnlineAssaultOps:ReqChangeFormation (Launch/Online/Classes/OnlineAssaultOps.lua:1455-1489)
// sends { nOnlineId, nFormationId } behind UI.ShowConnection(); the callback (:1473-1489) closes the
// connection, fails on sError and only refreshes the line-up when the answer carries bFormation.
//
// Request:  { "nFormationId": 1..3 }
// Response: { "bFormation": true } (+ attribute sync of (183, 2))
[CallGSApi("OnlineAssaultOps_ChangeFormation")]
public class OnlineAssaultOps_ChangeFormation : CallGSHandler
{
    private static readonly Logger Log = new(nameof(OnlineAssaultOps_ChangeFormation));

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player!;
        var formationId = CallGSParam.ReadUint(CallGSParam.Parse(param)?["nFormationId"]);

        if (formationId < 1 || formationId > AttrIds.AssaultOps.FormationCount)
        {
            Log.Warn($"Assault ops change-formation with unknown formation {formationId} from uid={player.Uid}.");
            return Task.FromResult(CallGSParam.Failure("error.BadParam"));
        }

        var sync = new NtfSyncPlayer();
        AssaultOpsBag.SetTakeFormation(player, formationId, sync);
        AssaultOpsBag.Save(player);
        Log.Info($"Assault ops formation {formationId} selected by uid={player.Uid}.");

        return Task.FromResult(CallGSResult.Ok(new JsonObject { ["bFormation"] = true }, sync));
    }
}
