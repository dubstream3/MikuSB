using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// Rolls the affix set of a monster card from the pools the client never reads (attributepool /
/// skillpool / chippool), producing the 9 (key, level) pairs the engine reads back through
/// pItem:AffixAttrLevel / GetAffixKeyAndValue (VirCapture.lua:112-122, 2544-2580).
///
/// Slot layout used by the client:
///   1..3 attributes (tier 1..4 shown as A/B/C/S, VirCapture.lua:109)
///   4..6 skills     (key = skill id from skilllist, level = skillcost row - 1)
///   7..9 chips      (key = chip id from chiplist, level fixed at 1; the chip's visible level comes
///                    from how many copies the formation owns, VirCaptureConfig:GetChipLevel)
///
/// Attribute tier: AttributeList pairs a multiplier with its weight and LevelList holds the tiers in
/// the same order, ascending - pool 1 ({"1.1":20,"1":60,"0.9":20}, levels [1,2,3]) therefore rolls
/// C 20% / B 60% / A 20%, and pool 2 ({"1":100}, levels [4]) always rolls S.
/// </summary>
internal static class VirCaptureMonsterAffixService
{
    private static readonly Logger Log = new("VirCapture.Affix");

    public const int SlotCount = 9;
    public const int AttributeSlots = 3;
    public const int SkillStartSlot = 4;
    public const int ChipStartSlot = 7;

    /// <summary>Builds the affix list of one card of <paramref name="template"/>.</summary>
    public static List<uint> Generate(MonsterCardExcel template)
    {
        var affixes = new List<uint>(SlotCount * 2);
        for (var slot = 1; slot <= SlotCount; slot++)
        {
            var (key, level) = RollSlot(template, slot);
            affixes.Add(key);
            affixes.Add(level);
        }

        return affixes;
    }

    private static (uint Key, uint Level) RollSlot(MonsterCardExcel template, int slot)
    {
        if (slot <= AttributeSlots)
            return RollAttribute(template.AttackRandomId, template.DefenseRandomId, template.HealthRandomId, slot);

        if (slot < ChipStartSlot)
            return RollSkill(template.SkillRandomId, slot);

        return RollChip(template.ChipRandomId, slot);
    }

    private static (uint Key, uint Level) RollAttribute(List<uint> attack, List<uint> defense, List<uint> health, int slot)
    {
        var pools = slot switch
        {
            1 => attack,
            2 => defense,
            _ => health
        };

        foreach (var poolId in pools)
        {
            if (!GameData.VirCaptureAttributePoolData.TryGetValue(poolId, out var pool) || pool.LevelList.Count == 0)
                continue;

            var rows = pool.Attributes
                .OrderBy(row => ParseMultiplier(row.Multiplier))
                .ToArray();
            var picked = VirCapturePoolParse.Roll(rows);
            if (picked == null)
                return ((uint)slot, pool.LevelList[0]);

            var index = Array.FindIndex(rows, row => row.Multiplier == picked);
            var level = index >= 0 && index < pool.LevelList.Count ? pool.LevelList[index] : pool.LevelList[0];
            return ((uint)slot, level);
        }

        return ((uint)slot, 1);
    }

    private static (uint Key, uint Level) RollSkill(List<uint> pools, int slot)
    {
        var slotIndex = slot - SkillStartSlot + 1;
        foreach (var poolId in pools)
        {
            if (!GameData.VirCaptureSkillPoolData.TryGetValue(poolId, out var pool))
                continue;

            var picked = VirCapturePoolParse.Roll(pool.Slot(slotIndex));
            if (picked == null || !uint.TryParse(picked, out var level) || level == 0)
                return (0, 0);

            var candidates = GameData.VirCaptureSkillListData.Values
                .Where(skill => skill.FitsSlot(slotIndex))
                .ToList();
            if (candidates.Count == 0)
                return (0, 0);

            var skill0 = candidates[Random.Shared.Next(candidates.Count)];
            return (skill0.Id, Math.Min(level, Math.Max(1u, skill0.MaxLevel)));
        }

        return (0, 0);
    }

    private static (uint Key, uint Level) RollChip(List<uint> pools, int slot)
    {
        var slotIndex = slot - ChipStartSlot + 1;
        foreach (var poolId in pools)
        {
            if (!GameData.VirCaptureChipPoolData.TryGetValue(poolId, out var pool))
                continue;

            var picked = VirCapturePoolParse.Roll(pool.Slot(slotIndex));
            if (picked == null || !uint.TryParse(picked, out var chipId) || chipId == 0)
                return (0, 0);
            if (!GameData.VirCaptureChipListData.ContainsKey(chipId))
            {
                Log.Warn($"chip {chipId} from pool {poolId} slot {slotIndex} is missing from chiplist");
                return (0, 0);
            }

            return (chipId, 1);
        }

        return (0, 0);
    }

    private static double ParseMultiplier(string text) =>
        double.TryParse(text, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : 1d;
}
