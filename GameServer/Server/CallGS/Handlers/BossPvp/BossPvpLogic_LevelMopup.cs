using MikuSB.GameServer.Game.BossPvp;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BossPvp;

[CallGSApi("BossPvpLogic_LevelMopup")]
public class BossPvpLogic_LevelMopup : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var (response, sync) = BossPvpService.HandleMopup(context.Connection.Player!, param);
        return Task.FromResult(CallGSResult.Ok(System.Text.Json.JsonSerializer.Serialize(response), sync));
    }
}
