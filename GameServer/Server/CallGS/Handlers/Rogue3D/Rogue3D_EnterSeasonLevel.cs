using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;

// Enters the Rogue3D season level. Returns a random seed used by the client for map generation.
// Persists SeasonGameplayId (sid=1006) and SeasonEnterFlag (sid=1008) as player attributes (GroupId=124).
// param: {"nDiffId", "nTeamID", "tbTeam", "tbBuffList", "tbLog"}
// Response: {"nSeed": int} on success, {"sErr": "key"} on failure
[CallGSApi("Rogue3D_EnterSeasonLevel")]
public class Rogue3D_EnterSeasonLevel : CallGSHandler<EnterSeasonLevelParam>
{
    private static readonly Random Random = new();

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, EnterSeasonLevelParam req)
    {

        if (req == null)
        {
            return Task.FromResult(CallGSResult.Ok("{\"nSeed\":0}"));
        }

        if (!context.Player.Rogue3DManager.TryEnterSeasonLevel(req.DiffId, out var sync))
        {
            return Task.FromResult(CallGSResult.Error("rogue3.massage_gameProcessError"));
        }

        var seed = Random.Next(1, 1_000_000_000);
        return Task.FromResult(CallGSResult.Ok($"{{\"nSeed\":{seed}}}", sync));
    }
}

public sealed class EnterSeasonLevelParam
{
    [JsonPropertyName("nDiffId")]
    public uint DiffId { get; set; }
}
