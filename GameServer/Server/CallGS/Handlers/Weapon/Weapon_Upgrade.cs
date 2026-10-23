using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.Proto;
using Newtonsoft.Json.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Weapon;

[CallGSApi("Weapon_Upgrade")]
public class Weapon_Upgrade : CallGSHandler<WeaponUpgradeParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, WeaponUpgradeParam req)
    {
        var player = context.Connection.Player!;

        if (req == null || req.Id <= 0 || req.TbMat == null || req.TbMat.Count == 0)
        {
            return Task.FromResult(CallGSResult.Error("tip.material_not_enough"));
        }

        var targetWeapon = player.InventoryManager.GetWeaponItem((uint)req.Id);
        if (targetWeapon == null)
        {
            return Task.FromResult(CallGSResult.Error("tip.material_not_enough"));
        }

        var config = WeaponUpgradeConfig.Load();
        if (!config.TryGetWeaponTemplate(targetWeapon.TemplateId, out var targetTemplate))
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var requestedMaterials = new Dictionary<uint, uint>();
        foreach (var row in req.TbMat)
        {
            if (row == null || row.Count < 2) continue;
            var itemId = (uint)Math.Max(0, row[0]);
            var count = (uint)Math.Max(0, row[1]);
            if (itemId == 0 || count == 0) continue;
            requestedMaterials[itemId] = requestedMaterials.GetValueOrDefault(itemId) + count;
        }

        if (requestedMaterials.Count == 0)
        {
            return Task.FromResult(CallGSResult.Error("tip.material_not_enough"));
        }

        ulong totalExp = 0;
        var syncItems = new List<Item>();
        var equippedWeaponIds = player.CharacterManager.CharacterData.Characters
            .Select(x => x.WeaponUniqueId)
            .Where(x => x != 0)
            .ToHashSet();

        foreach (var (itemId, count) in requestedMaterials)
        {
            if (itemId == targetWeapon.UniqueId)
            {
                return Task.FromResult(CallGSResult.Error("tip.material_not_enough"));
            }

            var material = FindInventoryItem(player.InventoryManager.InventoryData, itemId);
            if (material == null || material.ItemCount < count)
            {
                return Task.FromResult(CallGSResult.Error("tip.material_not_enough"));
            }

            if (material is GameWeaponInfo materialWeapon &&
                (materialWeapon.EquipAvatarId != 0 || equippedWeaponIds.Contains(materialWeapon.UniqueId)))
            {
                return Task.FromResult(CallGSResult.Error("tip.material_not_enough"));
            }

            if (!TryGetMaterialGain(config, material, out var gainExp))
            {
                return Task.FromResult(CallGSResult.Error("error.BadParam"));
            }

            totalExp += gainExp * count;
        }

        foreach (var (itemId, count) in requestedMaterials)
        {
            var material = FindInventoryItem(player.InventoryManager.InventoryData, itemId)!;
            material.ItemCount -= count;

            if (material.ItemCount == 0)
            {
                RemoveInventoryItem(player.InventoryManager.InventoryData, itemId);
                syncItems.Add(BuildRemovedProto(material));
            }
            else
            {
                syncItems.Add(material.ToProto());
            }
        }

        var maxLevel = config.GetWeaponMaxLevel(targetTemplate.BreakLimitId, targetWeapon.Break);
        var oldLevel = targetWeapon.Level == 0 ? 1u : targetWeapon.Level;
        targetWeapon.Level = oldLevel;
        var (newLevel, newExp) = config.ApplyWeaponExp(targetWeapon.Level, targetWeapon.Exp, totalExp, targetTemplate.Color, maxLevel);
        targetWeapon.Level = newLevel;
        targetWeapon.Exp = newExp;

        syncItems.Add(targetWeapon.ToProto());

        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);

        var sync = new NtfSyncPlayer();
        sync.Items.AddRange(syncItems);

        var bMaxUnlock = maxLevel > 0 && targetWeapon.Level >= maxLevel;

        // Passing the award unlock level unlocks the weapon's part award. The client keys both the
        // red dot on the arms tab and the claim button off attribute 112 (Item/Weapon.lua:1503).
        // bMaxUnLock above asks a different question (did the weapon reach its *current* cap) and
        // stays as it was.
        var awardUnlockLevel = WeaponPartAward.GetAwardUnlockLevel(config, targetTemplate.BreakLimitId);
        if (awardUnlockLevel > 0 && targetWeapon.Level >= awardUnlockLevel &&
            WeaponPartAward.MarkUnlocked(player, targetWeapon.TemplateId, sync))
        {
            DatabaseHelper.SaveDatabaseType(player.Data);
        }

        var arg = $"{{\"bMaxUnLock\":{(bMaxUnlock ? "true" : "false")}}}";
        return Task.FromResult(CallGSResult.Ok(arg, sync));
    }

    private static BaseGameItemInfo? FindInventoryItem(InventoryData inventory, uint itemId)
    {
        if (inventory.Weapons.TryGetValue(itemId, out var weapon))
        {
            return weapon;
        }

        if (inventory.Skins.TryGetValue(itemId, out var skin))
        {
            return skin;
        }

        if (inventory.Items.TryGetValue(itemId, out var item))
        {
            return item;
        }

        return null;
    }

    private static void RemoveInventoryItem(InventoryData inventory, uint itemId)
    {
        inventory.Weapons.Remove(itemId);
        inventory.Skins.Remove(itemId);
        inventory.Items.Remove(itemId);
    }

    private static Item BuildRemovedProto(BaseGameItemInfo item)
    {
        var proto = item.ToProto();
        proto.Count = 0;
        return proto;
    }

    private static bool TryGetMaterialGain(WeaponUpgradeConfig config, BaseGameItemInfo item, out ulong exp)
    {
        exp = 0;
        if (config.TryGetWeaponTemplate(item.TemplateId, out var weaponTemplate))
        {
            exp = weaponTemplate.ProvideExp;
            if (item is GameWeaponInfo weapon && weapon.Level > 1)
            {
                exp += config.GetWeaponRecycleExp(weaponTemplate, weapon.Level);
            }

            return true;
        }

        if (config.TryGetSuppliesTemplate(item.TemplateId, out var suppliesTemplate))
        {
            exp = suppliesTemplate.ProvideExp;
            return true;
        }

        return false;
    }
}

public sealed class WeaponUpgradeParam
{
    [JsonPropertyName("Id")]
    public int Id { get; set; }

    [JsonPropertyName("tbMat")]
    public List<List<int>> TbMat { get; set; } = [];
}

internal sealed class WeaponUpgradeConfig
{
    private readonly Dictionary<int, uint> _weaponNeedExpNormal;
    private readonly Dictionary<int, uint> _weaponNeedExpSsr;
    private readonly Dictionary<int, uint[]> _breakLimits;
    private readonly Dictionary<int, RecycleEntry> _recycleById;
    private readonly Dictionary<ulong, MaterialTemplate> _weaponTemplates;
    private readonly Dictionary<ulong, MaterialTemplate> _suppliesTemplates;
    private readonly Dictionary<int, ulong> _weaponRecycleExpNormal = [];
    private readonly Dictionary<int, ulong> _weaponRecycleExpSsr = [];

    public WeaponUpgradeConfig(
        Dictionary<int, uint> weaponNeedExpNormal,
        Dictionary<int, uint> weaponNeedExpSsr,
        Dictionary<int, uint[]> breakLimits,
        Dictionary<int, RecycleEntry> recycleById,
        Dictionary<ulong, MaterialTemplate> weaponTemplates,
        Dictionary<ulong, MaterialTemplate> suppliesTemplates)
    {
        _weaponNeedExpNormal = weaponNeedExpNormal;
        _weaponNeedExpSsr = weaponNeedExpSsr;
        _breakLimits = breakLimits;
        _recycleById = recycleById;
        _weaponTemplates = weaponTemplates;
        _suppliesTemplates = suppliesTemplates;

        BuildRecycleExpTable(_weaponNeedExpNormal, _weaponRecycleExpNormal);
        BuildRecycleExpTable(_weaponNeedExpSsr, _weaponRecycleExpSsr);
    }

    public static WeaponUpgradeConfig Load()
    {
        var normalExp = new Dictionary<int, uint>();
        var ssrExp = new Dictionary<int, uint>();
        foreach (var row in GameData.UpgradeExpData.Values)
        {
            normalExp[row.Lv] = row.WeaponNeedExp;
            ssrExp[row.Lv] = row.SSRWeaponNeedExp;
        }

        var breakLimits = new Dictionary<int, uint[]>();
        foreach (var row in GameData.BreakLevelLimitData.Values)
        {
            breakLimits[row.ID] =
            [
                row.Break0,
                row.Break1,
                row.Break2,
                row.Break3,
                row.Break4,
                row.Break5,
                row.Break6
            ];
        }

        var recycleById = new Dictionary<int, RecycleEntry>();
        foreach (var row in GameData.RecycleData.Values)
        {
            recycleById[row.ID] = new RecycleEntry(
                GetUIntFlexible(row.RecycleBase),
                GetDecimalFlexible(row.RecycleRatio));
        }

        var weaponTemplates = GameData.WeaponData.Values.ToDictionary(
            x => GameResourceTemplateId.FromGdpl(x.Genre, x.Detail, x.Particular, x.Level),
            x => new MaterialTemplate(x.Color, x.ProvideExp, x.ConsumeGold, x.RecycleID, x.BreakLimitID));
        var suppliesTemplates = GameData.AllSuppliesData
            .GroupBy(x => GameResourceTemplateId.FromGdpl(x.Genre, x.Detail, x.Particular, x.Level))
            .ToDictionary(g => g.Key, g => new MaterialTemplate(g.First().Color, g.First().ProvideExp, g.First().ConsumeGold, 0, 0));

        return new WeaponUpgradeConfig(normalExp, ssrExp, breakLimits, recycleById, weaponTemplates, suppliesTemplates);
    }

    public bool TryGetWeaponTemplate(ulong templateId, out MaterialTemplate template) =>
        _weaponTemplates.TryGetValue(templateId, out template!);

    public bool TryGetSuppliesTemplate(ulong templateId, out MaterialTemplate template) =>
        _suppliesTemplates.TryGetValue(templateId, out template!);

    public bool TryGetMaterialGain(BaseGameItemInfo item, out ulong exp)
    {
        exp = 0;
        if (TryGetWeaponTemplate(item.TemplateId, out var weaponTemplate))
        {
            exp = weaponTemplate.ProvideExp;
            if (item is GameWeaponInfo weapon && weapon.Level > 1)
                exp += GetWeaponRecycleExp(weaponTemplate, weapon.Level);
            return true;
        }
        if (TryGetSuppliesTemplate(item.TemplateId, out var suppliesTemplate))
        {
            exp = suppliesTemplate.ProvideExp;
            return true;
        }
        return false;
    }

    public ulong GetWeaponRecycleExp(MaterialTemplate template, uint level)
    {
        if (template.RecycleId <= 0 || !_recycleById.TryGetValue(template.RecycleId, out var recycle))
        {
            return 0;
        }

        var levelExp = template.Color == 5 ? _weaponRecycleExpSsr : _weaponRecycleExpNormal;
        var baseExp = levelExp.GetValueOrDefault((int)level);
        return (ulong)Math.Floor((recycle.RecycleBase + baseExp) * recycle.RecycleRatio);
    }

    public uint GetWeaponMaxLevel(int breakLimitId, uint currentBreak)
    {
        if (!_breakLimits.TryGetValue(breakLimitId, out var limits) || limits.Length == 0)
        {
            return 0;
        }

        var index = (int)Math.Min(currentBreak, (uint)(limits.Length - 1));
        return limits[index];
    }

    public (uint Level, uint Exp) ApplyWeaponExp(uint level, uint exp, ulong addExp, int color, uint maxLevel)
    {
        if (addExp == 0)
        {
            return (level, exp);
        }

        if (maxLevel > 0 && level >= maxLevel)
        {
            return (maxLevel, checked((uint)(exp + addExp)));
        }

        var destLevel = level;
        var destExp = exp + addExp;
        var needExp = GetWeaponNeedExp(color, destLevel);

        while (needExp > 0 && destExp >= needExp)
        {
            destExp -= needExp;
            destLevel++;

            if (maxLevel > 0 && destLevel >= maxLevel)
            {
                return (maxLevel, checked((uint)destExp));
            }

            needExp = GetWeaponNeedExp(color, destLevel);
            if (needExp == 0)
            {
                return (destLevel, checked((uint)destExp));
            }
        }

        return (destLevel, checked((uint)destExp));
    }

    private uint GetWeaponNeedExp(int color, uint level)
    {
        return color == 5
            ? _weaponNeedExpSsr.GetValueOrDefault((int)level)
            : _weaponNeedExpNormal.GetValueOrDefault((int)level);
    }

    private static void BuildRecycleExpTable(Dictionary<int, uint> needExp, Dictionary<int, ulong> recycleExp)
    {
        ulong current = 0;
        foreach (var level in needExp.Keys.OrderBy(x => x))
        {
            recycleExp[level] = current;
            current += needExp[level];
        }
    }

    private static uint GetUIntFlexible(JToken? token)
    {
        if (token == null)
        {
            return 0;
        }

        return token.Type switch
        {
            JTokenType.Integer => token.Value<uint>(),
            JTokenType.Float => (uint)Math.Max(0, token.Value<decimal>()),
            JTokenType.String when uint.TryParse(token.Value<string>(), out var result) => result,
            _ => 0
        };
    }

    private static decimal GetDecimalFlexible(JToken? token)
    {
        if (token == null)
        {
            return 0m;
        }

        return token.Type switch
        {
            JTokenType.Integer => token.Value<decimal>(),
            JTokenType.Float => token.Value<decimal>(),
            JTokenType.String when decimal.TryParse(token.Value<string>(), out var result) => result,
            _ => 0m
        };
    }
}

internal readonly record struct MaterialTemplate(int Color, uint ProvideExp, uint ConsumeGold, int RecycleId, int BreakLimitId);
internal readonly record struct RecycleEntry(uint RecycleBase, decimal RecycleRatio);
