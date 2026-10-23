using System.Text.Json;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Daily;

[CallGSApi("Daily_SetSelectSuit")]
public class Daily_SetSelectSuit : CallGSHandler<GirlWeaponSkinParam>
{

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, GirlWeaponSkinParam req)
    {

        if (req == null)
        {
            return Task.FromResult(CallGSResult.Ok("{}"));
        }
        var rsp = $"{{\"SuitId\":{req.Suit}}}";
        return Task.FromResult(CallGSResult.Ok(rsp));
    }
}

public sealed class GirlWeaponSkinParam
{
    public uint Type { get; set; }
    public uint Suit { get; set; }
}
