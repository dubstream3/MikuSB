using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;

// Selects the Rogue3D season talent and persists it as player attribute (GroupId=124, TalentId=1007).
// param: {"nTalentId": int}
// Response: {} on success, {"sErr": "key"} on failure
[CallGSApi("Rogue3D_SelectSeasonTalent")]
public class Rogue3D_SelectSeasonTalent : CallGSHandler<SelectSeasonTalentParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, SelectSeasonTalentParam req)
    {

        if (req == null)
        {
            return Task.FromResult(CallGSResult.Ok("{}"));
        }

        var sync = context.Player.Rogue3DManager.SelectSeasonTalent(req.TalentId);
        return Task.FromResult(CallGSResult.Ok("{}", sync));
    }
}

public sealed class SelectSeasonTalentParam
{
    [JsonPropertyName("nTalentId")]
    public uint TalentId { get; set; }
}
