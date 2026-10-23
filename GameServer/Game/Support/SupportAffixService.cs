using MikuSB.Data;

namespace MikuSB.GameServer.Game.Support;

public static class SupportAffixService
{
    public static (uint AffixId, uint Tier) GenerateRandomAffix(int poolId, IEnumerable<uint>? excludedAffixIds = null)
    {
        if (!GameData.SupportAffixPoolData.TryGetValue(poolId, out var pool))
            return (0, 0);

        var groups = pool.Groups.ToList();
        if (groups.Count == 0)
            return (0, 0);

        var totalWeight = groups.Sum(x => x.Weight);
        var roll = Random.Shared.Next(totalWeight);
        var cumulative = 0;
        var selectedAffixs = groups[0].Affixs;

        foreach (var (affixIds, weight) in groups)
        {
            cumulative += weight;
            if (roll < cumulative)
            {
                selectedAffixs = affixIds;
                break;
            }
        }

        if (selectedAffixs.Count == 0)
            return (0, 0);

        var excluded = excludedAffixIds?.ToHashSet() ?? [];
        var candidates = selectedAffixs.Where(x => !excluded.Contains((uint)x)).ToList();
        if (candidates.Count == 0)
            candidates = selectedAffixs.ToList();

        var affixId = candidates[Random.Shared.Next(candidates.Count)];
        var tierCount = GameData.SupportAffixData.GetValueOrDefault(affixId)?.TierCount ?? 5;
        var tier = (uint)(Random.Shared.Next(tierCount) + 1);
        return ((uint)affixId, tier);
    }

    public static uint GenerateTier(uint affixId)
    {
        var tierCount = GameData.SupportAffixData.GetValueOrDefault((int)affixId)?.TierCount ?? 5;
        return (uint)(Random.Shared.Next(tierCount) + 1);
    }
}
