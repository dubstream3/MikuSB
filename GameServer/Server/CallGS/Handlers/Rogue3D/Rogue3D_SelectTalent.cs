using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;

// Selects the Rogue3D talent and persists it as player attribute (GroupId=124, TalentId=7).
// param: {"nTalentId": int}
// Response: {} on success, {"sErr": "key"} on failure
[CallGSApi("Rogue3D_SelectTalent")]
public class Rogue3D_SelectTalent : CallGSHandler<SelectTalentParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, SelectTalentParam req)
    {

        if (req == null)
        {
            return Task.FromResult(CallGSResult.Ok("{}"));
        }

        var sync = context.Player.Rogue3DManager.SelectTalent(req.TalentId);
        return Task.FromResult(CallGSResult.Ok("{}", sync));
    }
}

public sealed class SelectTalentParam
{
    [JsonPropertyName("nTalentId")]
    public uint TalentId { get; set; }
}
