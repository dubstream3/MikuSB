namespace MikuSB.GameServer.Server.CallGS.Handlers.Preview;

// Returns the green (unlocked) level for each skin manifestation.
// Response: [{skinId, greenLevel}, ...]
[CallGSApi("ReqEntranceGreenLevel")]
public class ReqEntranceGreenLevel : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        // TODO: return actual skin green levels from player data
        return Task.FromResult(CallGSResult.Ok("[]"));
    }
}
