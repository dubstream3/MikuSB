using System.Text.Json.Serialization;
using MikuSB.GameServer.Game.Rogue3D;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;

[CallGSApi("Rogue3D_RestrictTech")]
public class Rogue3D_RestrictTech : CallGSHandler<RestrictTechParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, RestrictTechParam req)
    {
        var result = context.Player.Rogue3DManager.RestrictTech(req.TechId, req.Restrict);
        return Task.FromResult(ToCallGSResult(result));
    }

    private static CallGSResult ToCallGSResult(Rogue3DTechResult result)
    {
        return result.Error switch
        {
            Rogue3DTechError.None => CallGSResult.Ok("{}", result.Sync),
            Rogue3DTechError.BadParam => CallGSResult.Error("error.BadParam"),
            Rogue3DTechError.ConditionLimit => CallGSResult.Error("error.condition_limit"),
            _ => throw new ArgumentOutOfRangeException(nameof(result.Error), result.Error, null)
        };
    }
}

public sealed class RestrictTechParam
{
    [JsonPropertyName("nTechId")]
    public uint TechId { get; set; }

    [JsonPropertyName("nRestrict")]
    public uint Restrict { get; set; }
}
