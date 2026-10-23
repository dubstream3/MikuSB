using MikuSB.Enums.Item;
using MikuSB.Proto;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

[CallGSApi("Weapon_ShowDefaultPart")]
public class Weapon_ShowDefaultPart : CallGSHandler<WeaponShowDefaultPartParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, WeaponShowDefaultPartParam req)
    {

        if (req == null)
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var player = context.Connection.Player!;
        var weaponData = player.InventoryManager.GetWeaponItem(req.Id);
        if (weaponData == null)
        {
            return Task.FromResult(CallGSResult.Ok("{}"));
        }

        if (req.Flag == 1) weaponData.Flag = ItemFlagEnum.FLAG_WEAPON_DEFAULT;
        else weaponData.Flag = ItemFlagEnum.FLAG_READED;

        var sync = new NtfSyncPlayer
        {
            Items = { weaponData.ToProto() }
        };
        return Task.FromResult(CallGSResult.Ok("null", sync));
    }
}

public sealed class WeaponShowDefaultPartParam
{
    [JsonPropertyName("nFlag")] public int Flag { get; set; }
    public uint Id { get; set; }
}
