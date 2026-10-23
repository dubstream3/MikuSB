using MikuSB.GameServer.Game.BossPvp;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BossPvp;

[CallGSApi("BossPvpLogic_LevelFail")]
public class BossPvpLogic_LevelFail : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(param);
        var (response, sync) = BossPvpService.HandleFail(context.Connection.Player!, node);
        return Task.FromResult(CallGSResult.Ok(response.ToJsonString(), sync));
    }
}
