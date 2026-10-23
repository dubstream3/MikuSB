namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;

// Saves in-progress season fight data. The client does not process the response.
// param: {"tbData": {...}, "chaosWave": int}
[CallGSApi("Rogue3D_SaveSeasonFightData")]
public class Rogue3D_SaveSeasonFightData : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        return Task.FromResult(CallGSResult.Ok("{}"));
    }
}
