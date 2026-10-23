using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.Proto;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Weapon;

[CallGSApi("Weapon_OneKeyToMax")]
public class Weapon_OneKeyToMax : CallGSHandler<OneKeyToMaxParam>
{
    private const uint MaxBreak = 6;

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, OneKeyToMaxParam req)
    {
        var player = context.Connection.Player!;

        if (req == null || req.Id <= 0 || req.TbBreakUpgradeMat == null || req.TbBreakUpgradeMat.Count == 0)
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var weapon = player.InventoryManager.GetWeaponItem((uint)req.Id);
        if (weapon == null)
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var config = WeaponUpgradeConfig.Load();
        if (!config.TryGetWeaponTemplate(weapon.TemplateId, out var targetTemplate))
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var inventory = player.InventoryManager.InventoryData;
        var equippedWeaponIds = player.CharacterManager.CharacterData.Characters
            .Select(x => x.WeaponUniqueId)
            .Where(x => x != 0)
            .ToHashSet();

        // Validate all materials upfront before making any changes
        foreach (var stage in req.TbBreakUpgradeMat)
        {
            if (stage == null || stage.Count < 3) continue;
            var matList = stage[1].Deserialize<List<List<int>>>();
            if (matList == null) continue;

            foreach (var entry in matList)
            {
                if (entry == null || entry.Count < 2) continue;
                var itemId = (uint)Math.Max(0, entry[0]);
                var count = (uint)Math.Max(0, entry[1]);
                if (itemId == 0 || count == 0) continue;

                if (itemId == weapon.UniqueId)
                {
                    return Task.FromResult(CallGSResult.Error("tip.material_not_enough"));
                }

                var material = FindInventoryItem(inventory, itemId);
                if (material == null || material.ItemCount < count)
                {
                    return Task.FromResult(CallGSResult.Error("tip.material_not_enough"));
                }

                if (material is GameWeaponInfo materialWeapon &&
                    (materialWeapon.EquipAvatarId != 0 || equippedWeaponIds.Contains(materialWeapon.UniqueId)))
                {
                    return Task.FromResult(CallGSResult.Error("tip.material_not_enough"));
                }
            }
        }

        var syncItems = new List<Item>();
        var weaponLevel = weapon.Level == 0 ? 1u : weapon.Level;
        weapon.Level = weaponLevel;

        // Process each break stage
        foreach (var stage in req.TbBreakUpgradeMat)
        {
            if (stage == null || stage.Count < 3) continue;

            var matList = stage[1].Deserialize<List<List<int>>>();
            var doBreak = stage[2].GetInt32() == 1;

            if (matList != null && matList.Count > 0)
            {
                // Aggregate materials, skip duplicates
                var materialsUsed = new Dictionary<uint, uint>();
                foreach (var entry in matList)
                {
                    if (entry == null || entry.Count < 2) continue;
                    var itemId = (uint)Math.Max(0, entry[0]);
                    var count = (uint)Math.Max(0, entry[1]);
                    if (itemId == 0 || count == 0) continue;
                    materialsUsed[itemId] = materialsUsed.GetValueOrDefault(itemId) + count;
                }

                ulong totalExp = 0;
                foreach (var (itemId, count) in materialsUsed)
                {
                    var material = FindInventoryItem(inventory, itemId)!;
                    if (config.TryGetMaterialGain(material, out var gainExp))
                        totalExp += gainExp * count;
                }

                // Consume materials
                foreach (var (itemId, count) in materialsUsed)
                {
                    var material = FindInventoryItem(inventory, itemId)!;
                    material.ItemCount -= count;
                    if (material.ItemCount == 0)
                    {
                        RemoveInventoryItem(inventory, itemId);
                        var proto = material.ToProto();
                        proto.Count = 0;
                        syncItems.Add(proto);
                    }
                    else
                    {
                        syncItems.Add(material.ToProto());
                    }
                }

                // Apply exp to weapon
                if (totalExp > 0)
                {
                    var maxLevel = config.GetWeaponMaxLevel(targetTemplate.BreakLimitId, weapon.Break);
                    var (newLevel, newExp) = config.ApplyWeaponExp(weapon.Level, weapon.Exp, totalExp, targetTemplate.Color, maxLevel);
                    weapon.Level = newLevel;
                    weapon.Exp = newExp;
                }
            }

            // Perform break
            if (doBreak && weapon.Break < MaxBreak)
                weapon.Break++;
        }

        syncItems.Add(weapon.ToProto());
        DatabaseHelper.SaveDatabaseType(inventory);

        var finalMaxLevel = config.GetWeaponMaxLevel(targetTemplate.BreakLimitId, weapon.Break);
        var bMaxUnlock = finalMaxLevel > 0 && weapon.Level >= finalMaxLevel;

        var sync = new NtfSyncPlayer();
        sync.Items.AddRange(syncItems);

        // Same part award hook as Weapon_Upgrade: the one key button can be the call that passes
        // the award unlock level, and the client only offers the claim once attribute 112 says so.
        var awardUnlockLevel = WeaponPartAward.GetAwardUnlockLevel(config, targetTemplate.BreakLimitId);
        if (awardUnlockLevel > 0 && weapon.Level >= awardUnlockLevel &&
            WeaponPartAward.MarkUnlocked(player, weapon.TemplateId, sync))
        {
            DatabaseHelper.SaveDatabaseType(player.Data);
        }

        return Task.FromResult(CallGSResult.Ok($"{{\"bMaxUnLock\":{(bMaxUnlock ? "true" : "false")}}}", sync));
    }

    private static BaseGameItemInfo? FindInventoryItem(InventoryData inventory, uint itemId)
    {
        if (inventory.Weapons.TryGetValue(itemId, out var weapon)) return weapon;
        if (inventory.Skins.TryGetValue(itemId, out var skin)) return skin;
        if (inventory.Items.TryGetValue(itemId, out var item)) return item;
        return null;
    }

    private static void RemoveInventoryItem(InventoryData inventory, uint itemId)
    {
        inventory.Weapons.Remove(itemId);
        inventory.Skins.Remove(itemId);
        inventory.Items.Remove(itemId);
    }
}

public sealed class OneKeyToMaxParam
{
    [JsonPropertyName("Id")]
    public int Id { get; set; }

    [JsonPropertyName("tbBreakUpgradeMat")]
    public List<List<System.Text.Json.JsonElement>> TbBreakUpgradeMat { get; set; } = [];
}
