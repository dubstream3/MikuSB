using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;

// Selects the Rogue3D difficulty.
// Persists CurDiff (sid=5) and GameplayId (sid=6) as player attributes (GroupId=124).
// param: {"nDiffId": int}
// Response: {} on success, {"sErr": "key"} on failure
[CallGSApi("Rogue3D_SelectDiff")]
public class Rogue3D_SelectDiff : CallGSHandler<SelectDiffParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, SelectDiffParam req)
    {
        if (req == null)
        {
            return Task.FromResult(CallGSResult.Ok("{}"));
        }

        if (!context.Player.Rogue3DManager.TrySelectDifficulty(req.DiffId, out var sync))
        {
            return Task.FromResult(CallGSResult.Error("rogue3.massage_gameProcessError"));
        }

        return Task.FromResult(CallGSResult.Ok("{}", sync));
    }
}

public sealed class SelectDiffParam
{
    [JsonPropertyName("nDiffId")]
    public uint DiffId { get; set; }
}
