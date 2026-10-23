using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Proto;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Weapon;

// s2c: function(sErr) — send "null" on success (json.decode("null") = nil = falsy in Lua)
[CallGSApi("Weapon_Break")]
public class Weapon_Break : CallGSHandler<WeaponBreakParam>
{
    private const uint MaxBreak = 6;

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, WeaponBreakParam req)
    {
        var player = context.Connection.Player!;

        if (req == null || req.WeaponId == 0)
        {
            return Task.FromResult(CallGSResult.Ok("\"error.BadParam\""));
        }

        var weapon = player.InventoryManager.InventoryData.Weapons.GetValueOrDefault((uint)req.WeaponId);
        if (weapon == null)
        {
            return Task.FromResult(CallGSResult.Ok("\"error.BadParam\""));
        }

        if (weapon.Break >= MaxBreak)
        {
            return Task.FromResult(CallGSResult.Ok("\"tip.already_max_break\""));
        }

        var nextBreak = weapon.Break + 1;

        // Look up break cost from WeaponExcel → BreakExcel
        var weaponExcel = GameData.WeaponData.Values.FirstOrDefault(x =>
            GameResourceTemplateId.FromGdpl(x.Genre, x.Detail, x.Particular, x.Level) == weapon.TemplateId);

        var requestedMaterials = new Dictionary<ulong, uint>();
        if (weaponExcel != null && GameData.BreakData.TryGetValue(weaponExcel.BreakMatID, out var breakExcel))
        {
            foreach (var row in breakExcel.GetItems(nextBreak))
            {
                if (row.Count < 5) continue;
                var tid = GameResourceTemplateId.FromGdpl(
                    (uint)row[0], (uint)row[1], (uint)row[2], (uint)row[3]);
                requestedMaterials[tid] = requestedMaterials.GetValueOrDefault(tid) + (uint)row[4];
            }
        }

        // Validate materials
        foreach (var (tid, count) in requestedMaterials)
        {
            var item = player.InventoryManager.InventoryData.Items.Values.FirstOrDefault(x => x.TemplateId == tid);
            if (item == null || item.ItemCount < count)
            {
                return Task.FromResult(CallGSResult.Ok("\"tip.not_material_for_break\""));
            }
        }

        // Consume materials
        var syncItems = new List<Item>();
        foreach (var (tid, count) in requestedMaterials)
        {
            var item = player.InventoryManager.InventoryData.Items.Values.First(x => x.TemplateId == tid);
            item.ItemCount -= count;
            var proto = item.ToProto();
            if (item.ItemCount == 0)
            {
                player.InventoryManager.InventoryData.Items.Remove(item.UniqueId);
                proto.Count = 0;
            }
            syncItems.Add(proto);
        }

        weapon.Break = nextBreak;
        syncItems.Add(weapon.ToProto());

        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);

        var sync = new NtfSyncPlayer();
        sync.Items.AddRange(syncItems);

        return Task.FromResult(CallGSResult.Ok("null", sync));
    }
}

public sealed class WeaponBreakParam
{
    [JsonPropertyName("Id")]
    public int WeaponId { get; set; }
}
