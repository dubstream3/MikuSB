using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.Data;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Game.Player;

/// <summary>
/// The single place that spends currency. Until now the mock server only ever produced money:
/// <see cref="PlayerInstance.AddCurrency"/> is the sole writer and the login bootstrap pinned every
/// currency id to 999_999_999 with a "raise only" ratchet, so a deduction could never survive the
/// next NtfSyncPlayer snapshot. Every spending entry point (shop purchase, gacha exchange, item
/// boxes, gacha pull) goes through here so the charge, the sync and the persistence stay together.
///
/// Client contract: Cash.GetMoneyCount(moneyType) reads (AttrIds.Currency.GroupId, moneyType * 2 + 1)
/// except for Vigor, which reads the player proto field (Script/Cash/Cash.lua:55-63).
/// </summary>
public static class CurrencyService
{
    private static readonly Logger Log = new(nameof(CurrencyService));

    /// <summary>Chinese names for the currency ids the shop tabs, prices and GM output surface.</summary>
    private static readonly Dictionary<uint, string> ChineseNames = new()
    {
        [1] = "比特金",
        [2] = "数据金",
        [3] = "通用银",
        [4] = "体力",
        [5] = "诺恩人token",
        [6] = "金币",
        [7] = "记忆碎片",
        [8] = "数据金",
        [9] = "迷域代币",
        [12] = "压抑区代币",
        [13] = "作战凭证",
        [14] = "红宝石票",
        [15] = "联机票",
        [23] = "元素塔票",
        [24] = "元素塔抽奖券",
        [27] = "虚拟捕获代币",
        [30] = "大富翁骰子",
        [41] = "背包迷域币1",
        [42] = "背包迷域币2",
        [51] = "背包迷域币3",
        [52] = "梦境卡代币",
        [53] = "异星守护代币",
        [54] = "农场战争技能点",
        [56] = "大富翁代币",
        [57] = "联机海战代币",
        [58] = "拼图代币",
        [61] = "星愿币",
        [62] = "进击行动代币",
        [63] = "多诺对决代币",
    };

    private static readonly Dictionary<string, uint> AliasMap = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves a GM/console token to a moneyType. Accepts the numeric id, the cash.json Type column
    /// (MONEY, GOLD, SILVER, VIGOR, ...), the Type column with underscores removed, the fallback
    /// Chinese name and the raw localization key.
    /// </summary>
    public static bool TryResolve(string? token, out uint moneyType)
    {
        moneyType = 0;
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var key = token.Trim();
        if (uint.TryParse(key, out var numeric) && numeric is > 0 and <= 1000)
        {
            moneyType = numeric;
            return true;
        }

        EnsureAliases();
        if (AliasMap.TryGetValue(key, out moneyType))
            return true;

        // Accept "Money_Type_Gold" / "money-type-gold" style spellings of the same Type column.
        var normalized = key.Replace("_", "").Replace("-", "").Replace(" ", "");
        return AliasMap.TryGetValue(normalized, out moneyType);
    }

    private static void EnsureAliases()
    {
        if (AliasMap.Count > 0)
            return;

        foreach (var (id, name) in ChineseNames)
            AliasMap[name] = id;

        foreach (var entry in GameData.CashData.Values)
        {
            if (!string.IsNullOrWhiteSpace(entry.Type))
            {
                AliasMap[entry.Type!] = entry.Id;
                AliasMap[entry.Type!.Replace("_", "").Replace("-", "")] = entry.Id;
            }

            if (!string.IsNullOrWhiteSpace(entry.Name))
                AliasMap[entry.Name!] = entry.Id;

            if (ChineseNames.TryGetValue(entry.Id, out var chinese))
                AliasMap[chinese] = entry.Id;
        }

        Log.Info($"Currency alias table ready: {AliasMap.Count} token(s) over {GameData.CashData.Count} cash row(s).");
    }

    public static string Describe(uint moneyType)
    {
        var type = GameData.CashData.TryGetValue(moneyType, out var cash) ? cash.Type : null;
        var chinese = ChineseNames.TryGetValue(moneyType, out var name) ? name : null;
        var localization = cash?.Name;

        var label = chinese ?? (string.IsNullOrWhiteSpace(localization) ? type : localization);
        return $"{moneyType} ({type ?? "?"}{(label == null ? "" : ", " + label)})";
    }

    public static string NameOrType(uint moneyType)
    {
        if (ChineseNames.TryGetValue(moneyType, out var chinese))
            return chinese;

        if (GameData.CashData.TryGetValue(moneyType, out var cash))
            return cash.Type ?? cash.Name ?? moneyType.ToString();

        return moneyType.ToString();
    }

    /// <summary>Current balance of a moneyType, routed through Vigor when that is the id.</summary>
    public static uint GetBalance(PlayerInstance player, uint moneyType) =>
        moneyType == AttrIds.Currency.Vigor
            ? player.Data.Vigor
            : player.Attributes.GetValue(AttrIds.Currency.GroupId, AttrIds.Currency.GetSid(moneyType));

    /// <summary>
    /// Charges <paramref name="amount"/> of <paramref name="moneyType"/>. Returns false with a client
    /// error key when the balance is short; on success the new value is synced into
    /// <paramref name="sync"/> so the client UI updates immediately.
    /// </summary>
    public static bool TrySpend(PlayerInstance player, uint moneyType, uint amount, NtfSyncPlayer sync,
        out string error)
    {
        error = "tip.gold_not_enough";
        if (amount == 0)
            return true;

        if (moneyType == AttrIds.Currency.Vigor)
        {
            // VigorService also starts the recovery clock, which is what the client's countdown reads.
            if (VigorService.TrySpend(player, amount, sync))
                return true;

            error = "tip.vigor_not_enough";
            return false;
        }

        var sid = AttrIds.Currency.GetSid(moneyType);
        var attr = player.Attributes.GetOrCreate(AttrIds.Currency.GroupId, sid);
        if (attr.Val < amount)
            return false;

        attr.Val -= amount;
        player.Attributes.SyncTo(sync, attr);
        player.SyncMoneyChannels(sync);

        Log.Info($"uid={player.Uid} spent {amount}x moneyType={Describe(moneyType)}, left {attr.Val}.");
        return true;
    }

    /// <summary>Sets an absolute balance (GM path) and syncs it to the client.</summary>
    public static void SetBalance(PlayerInstance player, uint moneyType, uint value, NtfSyncPlayer sync)
    {
        if (moneyType == AttrIds.Currency.Vigor)
        {
            VigorService.Set(player, value, sync);
            return;
        }

        var attr = player.Attributes.Set(AttrIds.Currency.GroupId, AttrIds.Currency.GetSid(moneyType), value);
        player.Attributes.SyncTo(sync, attr);
        player.SyncMoneyChannels(sync);
    }

    /// <summary>
    /// How many units of a GDPL row the player owns. Uses the same first-stack-by-template lookup as
    /// <see cref="TrySpendItem"/>, so an affordability check can never disagree with the charge.
    /// </summary>
    public static uint GetItemCount(PlayerInstance player, IReadOnlyList<uint> gdpl)
    {
        if (gdpl.Count < 4)
            return 0;

        var templateId = GameResourceTemplateId.FromGdpl(gdpl[0], gdpl[1], gdpl[2], gdpl[3]);
        var wallet = player.InventoryManager.InventoryData.Items.Values
            .FirstOrDefault(x => x.TemplateId == templateId);

        return wallet?.ItemCount ?? 0;
    }

    /// <summary>
    /// Spends an inventory item identified by its GDPL row, mirroring how
    /// ShopLogic_BuyGoods already charges item-shaped prices.
    /// </summary>
    public static bool TrySpendItem(PlayerInstance player, IReadOnlyList<uint> gdpl, uint count,
        out string error)
    {
        error = "tip.NotEnoughItem";
        if (count == 0)
            return true;

        if (gdpl.Count < 4)
            return false;

        var templateId = GameResourceTemplateId.FromGdpl(gdpl[0], gdpl[1], gdpl[2], gdpl[3]);
        var wallet = player.InventoryManager.InventoryData.Items.Values
            .FirstOrDefault(x => x.TemplateId == templateId);

        if (wallet == null || wallet.ItemCount < count)
            return false;

        wallet.ItemCount -= count;
        return true;
    }

    /// <summary>Flushes the tables a currency change can touch.</summary>
    public static void Persist(PlayerInstance player)
    {
        DatabaseHelper.SaveDatabaseType(player.Data);
        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
    }
}
