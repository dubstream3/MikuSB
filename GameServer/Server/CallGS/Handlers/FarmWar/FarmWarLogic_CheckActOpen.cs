using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.GameServer.Game.FarmWar;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.FarmWar;

// FarmWarActivity.Req_CheckActOpen (FarmWarActivity.lua:1080-1094) runs behind a connection overlay.
// umg_farmwar_main.lua:OnCheckActOpen closes the whole panel when bOpen is false and re-opens the
// module when nId differs from the period the widget was built with, so both keys are mandatory.
[CallGSApi("FarmWarLogic_CheckActOpen")]
public class FarmWarLogic_CheckActOpen : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player!;
        var act = FarmWarActivityService.CurrentAct(player);

        return Task.FromResult(CallGSResult.Ok(new JsonObject
        {
            ["bOpen"] = act != null,
            ["nId"] = act?.Id ?? 0
        }.ToJsonString(), null, "FarmWarLogic_CheckActOpen"));
    }
}
