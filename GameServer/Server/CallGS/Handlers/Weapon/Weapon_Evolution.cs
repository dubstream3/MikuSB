using MikuSB.Database;
using MikuSB.Proto;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Weapon;

// s2c: function(sErr) — send "null" on success
// Id      = target weapon UniqueId
// nItemId = material item UniqueId (weapon or supply item to consume)
[CallGSApi("Weapon_Evolution")]
public class Weapon_Evolution : CallGSHandler<WeaponEvolutionParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, WeaponEvolutionParam req)
    {
        var player = context.Connection.Player!;

        if (req == null || req.WeaponId == 0 || req.MaterialId == 0)
        {
            return Task.FromResult(CallGSResult.Ok("\"error.BadParam\""));
        }

        var weapon = player.InventoryManager.InventoryData.Weapons.GetValueOrDefault((uint)req.WeaponId);
        if (weapon == null)
        {
            return Task.FromResult(CallGSResult.Ok("\"error.BadParam\""));
        }

        var syncItems = new List<Item>();

        // Material can be a weapon or a regular item
        if (player.InventoryManager.InventoryData.Weapons.TryGetValue((uint)req.MaterialId, out var matWeapon))
        {
            player.InventoryManager.InventoryData.Weapons.Remove((uint)req.MaterialId);
            var removed = matWeapon.ToProto();
            removed.Count = 0;
            syncItems.Add(removed);
        }
        else if (player.InventoryManager.InventoryData.Items.TryGetValue((uint)req.MaterialId, out var matItem))
        {
            matItem.ItemCount--;
            var proto = matItem.ToProto();
            if (matItem.ItemCount == 0)
            {
                player.InventoryManager.InventoryData.Items.Remove(matItem.UniqueId);
                proto.Count = 0;
            }
            syncItems.Add(proto);
        }
        else
        {
            return Task.FromResult(CallGSResult.Ok("\"tip.not_material\""));
        }

        weapon.Evolue++;
        syncItems.Add(weapon.ToProto());

        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);

        var sync = new NtfSyncPlayer();
        sync.Items.AddRange(syncItems);

        return Task.FromResult(CallGSResult.Ok("null", sync));
    }
}

public sealed class WeaponEvolutionParam
{
    [JsonPropertyName("Id")]
    public int WeaponId { get; set; }

    [JsonPropertyName("nItemId")]
    public int MaterialId { get; set; }
}
