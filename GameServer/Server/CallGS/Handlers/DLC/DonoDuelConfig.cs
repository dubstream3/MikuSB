using MikuSB.Data;
using MikuSB.Data.Excel;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

/// <summary>
/// Typed access to dlc/DonoDuel/donoduel_config.json (see DonoDuelConfigExcel for the row shapes).
/// The ConfigID meanings come from the table's own Note1 column:
///   1  attack upgrades   [[quality, cost, gainMin, gainMax], ...]
///   2  quality cost      {"2": 700, "3": 1500, ...}
///   3  quality unlock    {"2": [[36, 103]], ...}
///   4  skill learning cost (unused by the client - see DonoDuel_UpgradeSkill)
///   5  reshaping cost    [20, 50, 160] indexed by the number of locked skill slots
///   13 recycle by star   {"1": 40, ...}
///   14 recycle by quality{"1": 400, ...}
///   15 recycle by stats  [1000, 3] = every 1000 attribute points pays 3
///   16 maximum quality   [6]
///   19 initial deck size [150]
///   23 defence upgrades  (same shape as 1)
///   24 health upgrades   (same shape as 1)
/// </summary>
internal static class DonoDuelConfig
{
    public const uint AttrAttackConfigId = 1;
    public const uint QualityCostConfigId = 2;
    public const uint QualityUnlockConfigId = 3;
    public const uint SkillLearningCostConfigId = 4;
    public const uint SkillReshapingCostConfigId = 5;
    public const uint RecycleStarConfigId = 13;
    public const uint RecycleQualityConfigId = 14;
    public const uint RecycleAttrConfigId = 15;
    public const uint QualityMaxConfigId = 16;
    public const uint InitBagConfigId = 19;
    public const uint AttrDefenceConfigId = 23;
    public const uint AttrHealthConfigId = 24;

    private static DonoDuelConfigExcel? Get(uint configId)
        => GameData.DonoDuelConfigData.GetValueOrDefault(configId);

    public static uint QualityMax()
    {
        var value = Get(QualityMaxConfigId)?.Scalar() ?? 0;
        return value == 0 ? 6 : value;
    }

    /// <summary>Starting deck size the client compares (185, 3) against (umg_donoduel_bag.lua:229).</summary>
    public static uint InitBag() => Get(InitBagConfigId)?.Scalar() ?? 150;

    /// <summary>Cost of taking a card from (quality - 1) to <paramref name="targetQuality"/>.</summary>
    public static uint QualityCost(uint targetQuality) => Get(QualityCostConfigId)?.ValueAt(targetQuality) ?? 0;

    public static IReadOnlyList<IReadOnlyList<uint>> QualityUnlockConditions(uint quality)
        => Get(QualityUnlockConfigId)?.RowsAt(quality) ?? [];

    /// <summary>
    /// [session-currency cost of a plain single-slot skill learn]. The shipped client never charges
    /// for it (ConfigID 4 has no call site), so this is only used by the optional charge in
    /// DonoDuel_UpgradeSkill.
    /// </summary>
    public static uint SkillLearningCost() => Get(SkillLearningCostConfigId)?.Flat().FirstOrDefault() ?? 0;

    /// <summary>
    /// Reshaping ("一键重塑") cost indexed by the number of locked skill slots: [20, 50, 160]
    /// (uw_donoduel_culture_skill.lua:372-386 reads it as GetSkillLockedNum() + 1).
    /// </summary>
    public static uint SkillReshapingCost(uint lockedSlots)
    {
        var costs = Get(SkillReshapingCostConfigId)?.Flat() ?? [];
        if (costs.Count == 0)
            return 0;

        return lockedSlots < costs.Count ? costs[(int)lockedSlots] : costs[^1];
    }

    /// <summary>Attribute upgrade cost and gain range for one position at one quality.</summary>
    public static (uint Cost, uint GainMin, uint GainMax) AttrGain(uint position, uint quality)
    {
        var configId = position switch
        {
            DonoDuelGrowth.AttrAttack => AttrAttackConfigId,
            DonoDuelGrowth.AttrDefence => AttrDefenceConfigId,
            DonoDuelGrowth.AttrHealth => AttrHealthConfigId,
            _ => 0u
        };

        var row = configId == 0 ? [] : Get(configId)?.QualityRow(quality) ?? [];
        return row.Count >= 4 ? (row[1], row[2], row[3]) : (0u, 0u, 0u);
    }

    public static uint RecycleStar(uint star) => Get(RecycleStarConfigId)?.ValueAt(star) ?? 0;

    public static uint RecycleQuality(uint quality) => Get(RecycleQualityConfigId)?.ValueAt(quality) ?? 0;

    /// <summary>[every N attribute points, paid M] - donoduel_config ConfigID 15 = [1000, 3].</summary>
    public static (uint Per, uint Reward) RecycleAttr()
    {
        var flat = Get(RecycleAttrConfigId)?.Flat() ?? [];
        return flat.Count >= 2 ? (flat[0], flat[1]) : (1000u, 3u);
    }
}
