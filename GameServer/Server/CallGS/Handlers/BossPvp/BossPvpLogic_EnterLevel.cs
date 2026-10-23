using MikuSB.GameServer.Game.BossPvp;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BossPvp;

[CallGSApi("BossPvpLogic_EnterLevel")]
public class BossPvpLogic_EnterLevel : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var response = BossPvpService.HandleEnterLevel(param);
        return Task.FromResult(CallGSResult.Ok(System.Text.Json.JsonSerializer.Serialize(response)));
    }
}
