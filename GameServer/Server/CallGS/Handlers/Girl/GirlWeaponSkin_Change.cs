using MikuSB.Proto;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

[CallGSApi("GirlWeaponSkin_Change")]
public class GirlWeaponSkin_Change : CallGSHandler<GirlWeaponSkinParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, GirlWeaponSkinParam req)
    {

        if (req == null)
        {
            return Task.FromResult(CallGSResult.Ok("{\"err\":\"error.BadParam\"}"));
        }

        var player = context.Connection.Player!;
        var cardData = player.CharacterManager.GetCharacterByGUID(req.CardId);
        if (cardData == null) return Task.FromResult(CallGSResult.NoResponse());
        var skinData = player.InventoryManager.GetNormalItem(req.SkinId);
        if (skinData == null)
        {
            return Task.FromResult(CallGSResult.Ok("{\"err\":\"error.BadParam\"}"));
        }

        cardData.WeaponSkinId = req.SkinId;
        var sync = new NtfSyncPlayer
        {
            Items = { cardData.ToProto() }
        };

        return Task.FromResult(CallGSResult.Ok("null", sync));
    }
}

public sealed class GirlWeaponSkinParam
{
    [JsonPropertyName("nCardId")]
    public uint CardId { get; set; }

    [JsonPropertyName("nSkinId")]
    public uint SkinId { get; set; }
}
