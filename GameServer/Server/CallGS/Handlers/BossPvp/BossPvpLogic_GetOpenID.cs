using MikuSB.GameServer.Game.BossPvp;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BossPvp;

[CallGSApi("BossPvpLogic_GetOpenID")]
public class BossPvpLogic_GetOpenID : CallGSHandler
{
    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var (response, sync) = await BossPvpService.HandleGetOpenIdAsync(context.Connection.Player!);
        return CallGSResult.Ok(System.Text.Json.JsonSerializer.Serialize(response), sync);
    }
}
