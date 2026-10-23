namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;

// Enters the Rogue3D level. Returns a random seed used by the client for map generation.
// param: {"nDiffId", "nTeamID", "tbTeam", "tbBuffList", "tbLog"}
// Response: {"nSeed": int}
[CallGSApi("Rogue3D_EnterLevel")]
public class Rogue3D_EnterLevel : CallGSHandler
{
    private static readonly Random Random = new();

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var seed = Random.Next(1, 1_000_000_000);
        return Task.FromResult(CallGSResult.Ok($"{{\"nSeed\":{seed}}}"));
    }
}
