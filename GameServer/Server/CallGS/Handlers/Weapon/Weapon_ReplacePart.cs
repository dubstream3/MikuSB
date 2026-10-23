using MikuSB.Proto;
using System.Text.Json;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

[CallGSApi("Weapon_ReplacePart")]
public class Weapon_ReplacePart : CallGSHandler<WeaponPartReplaceParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, WeaponPartReplaceParam req)
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

        uint partId = 0;
        if (req.PartId != -1)
        {
            var partData = player.InventoryManager.GetNormalItem((uint)req.PartId);
            if (partData != null) partId = partData.UniqueId;
        }

        weaponData.PartSlots[req.Type] = partId;
        var sync = new NtfSyncPlayer
        {
            Items = { weaponData.ToProto() }
        };
        return Task.FromResult(CallGSResult.Ok("null", sync));
    }
}

public sealed class WeaponPartReplaceParam
{
    public int PartId { get; set; }
    public uint Type { get; set; }
    public uint Id { get; set; }
}
