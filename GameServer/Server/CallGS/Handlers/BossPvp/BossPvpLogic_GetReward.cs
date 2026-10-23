using MikuSB.GameServer.Game.BossPvp;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BossPvp;

[CallGSApi("BossPvpLogic_GetReward")]
public class BossPvpLogic_GetReward : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var response = BossPvpService.HandleGetReward(param);
        return Task.FromResult(CallGSResult.Ok(System.Text.Json.JsonSerializer.Serialize(response)));
    }
}
