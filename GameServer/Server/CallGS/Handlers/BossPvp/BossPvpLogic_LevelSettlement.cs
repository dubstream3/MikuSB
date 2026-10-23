using MikuSB.GameServer.Game.BossPvp;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BossPvp;

[CallGSApi("BossPvpLogic_LevelSettlement")]
public class BossPvpLogic_LevelSettlement : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(param);
        var (response, sync) = BossPvpService.HandleSettlement(context.Connection.Player!, node);
        return Task.FromResult(CallGSResult.Ok(response.ToJsonString(), sync));
    }
}
