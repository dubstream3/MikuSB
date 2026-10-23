using MikuSB.Database.Inventory;

namespace MikuSB.GameServer.Game.Support;

public static class SupportAffixStateService
{
    public const int PairSize = 2;
    public const int MaxLogicalSlots = 5;
    public const int ActiveThirdAffixSlot = 3;
    public const int PendingMaxAffixSlot = 4;
    public const int PendingInitialAffixSlot = 5;

    public static void EnsureCapacity(GameSupportCardInfo card, int logicalSlot = MaxLogicalSlots)
    {
        var minCount = Math.Clamp(logicalSlot, 1, MaxLogicalSlots) * PairSize;
        while (card.Affixs.Count < minCount)
            card.Affixs.Add(0);
    }

    public static (uint AffixId, uint Tier) GetAffix(GameSupportCardInfo card, int logicalSlot)
    {
        if (logicalSlot < 1 || logicalSlot > MaxLogicalSlots)
            return (0, 0);

        var index = (logicalSlot - 1) * PairSize;
        if (card.Affixs.Count <= index + 1)
            return (0, 0);

        return (card.Affixs[index], card.Affixs[index + 1]);
    }

    public static bool HasAffix(GameSupportCardInfo card, int logicalSlot)
    {
        var (affixId, tier) = GetAffix(card, logicalSlot);
        return affixId > 0 && tier > 0;
    }

    public static void SetAffix(GameSupportCardInfo card, int logicalSlot, uint affixId, uint tier)
    {
        if (logicalSlot < 1 || logicalSlot > MaxLogicalSlots)
            return;

        EnsureCapacity(card, logicalSlot);
        var index = (logicalSlot - 1) * PairSize;
        card.Affixs[index] = affixId;
        card.Affixs[index + 1] = tier;
    }

    public static void ClearAffix(GameSupportCardInfo card, int logicalSlot)
    {
        SetAffix(card, logicalSlot, 0, 0);
    }

    public static void CopyAffix(GameSupportCardInfo card, int fromSlot, int toSlot)
    {
        var (affixId, tier) = GetAffix(card, fromSlot);
        SetAffix(card, toSlot, affixId, tier);
    }

    public static uint GetVisibleInitialAffixIndex(GameSupportCardInfo card)
    {
        return HasAffix(card, PendingInitialAffixSlot) ? card.AffixId : 0;
    }

    public static void NormalizePendingState(GameSupportCardInfo card)
    {
        if (!HasAffix(card, PendingInitialAffixSlot))
            card.AffixId = 0;
    }
}
