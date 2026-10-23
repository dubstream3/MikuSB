using System.Text.Json.Serialization;
using MikuSB.GameServer.Game.Rogue3D;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;

[CallGSApi("Rogue3D_UpgradeTech")]
public class Rogue3D_UpgradeTech : CallGSHandler<UpgradeTechParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, UpgradeTechParam req)
    {
        var result = context.Player.Rogue3DManager.UpgradeTech(req.TechId);
        return Task.FromResult(ToCallGSResult(result));
    }

    private static CallGSResult ToCallGSResult(Rogue3DTechResult result)
    {
        return result.Error switch
        {
            Rogue3DTechError.None => CallGSResult.Ok("{}", result.Sync),
            Rogue3DTechError.BadParam => CallGSResult.Error("error.BadParam"),
            Rogue3DTechError.MaxLevel => CallGSResult.Error("tip.girlcard_break_max"),
            Rogue3DTechError.ConditionLimit => CallGSResult.Error("error.condition_limit"),
            Rogue3DTechError.GoldNotEnough => CallGSResult.Error("error.gold_not_enough"),
            _ => throw new ArgumentOutOfRangeException(nameof(result.Error), result.Error, null)
        };
    }
}

public sealed class UpgradeTechParam
{
    [JsonPropertyName("nTechId")]
    public uint TechId { get; set; }
}
