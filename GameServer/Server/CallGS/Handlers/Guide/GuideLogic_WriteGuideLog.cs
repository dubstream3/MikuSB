using System.Text.Json.Nodes;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Guide;

// The client queues guide-step log lines and re-sends the head of the queue until the server
// echoes it back: Guide/Guide.lua:1146 removes every queued entry equal to tbParam.logstr.
// The router's fallback payload carries neither logstr nor error, so the queue never drained and
// the client looped forever (thousands of identical requests). Answer by echoing the line.
[CallGSApi("GuideLogic_WriteGuideLog")]
public class GuideLogic_WriteGuideLog : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var logstr = (JsonNode.Parse(param) as JsonObject)?["logstr"]?.ToString() ?? "";
        var response = new JsonObject
        {
            ["logstr"] = logstr
        };

        return Task.FromResult(CallGSResult.Ok(response.ToJsonString()));
    }
}
