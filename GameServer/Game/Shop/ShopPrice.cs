using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.Packet.Send.Misc;
using MikuSB.Proto;
using System.Text.Json.Nodes;

namespace MikuSB.GameServer.Game.Shop;

/// <summary>
/// One price line of a shop goods row. The client keeps prices positional and decides the shape by
/// length (Shop.lua:608 GetBuyPrice + :726 BuyGoods):
///   two elements             -> {moneyType, cost}, charged through Cash.GetMoneyCount
///   five or more elements    -> GDPL + count, charged from the item bag
/// The cost is always the LAST element, and the server multiplies it by the purchase count itself, so
/// a tampered nCount or a stale local cache cannot get goods for free.
/// </summary>
public sealed class ShopPrice
{
    private ShopPrice(bool isCurrency, uint moneyType, uint cost, uint[]? itemGdpl)
    {
        IsCurrency = isCurrency;
        MoneyType = moneyType;
        Cost = cost;
        ItemGdpl = itemGdpl;
    }

    /// <summary>True when the price is a currency id (group 1 / Vigor), false for an inventory item.</summary>
    public bool IsCurrency { get; }

    /// <summary>Currency id when <see cref="IsCurrency"/>; the number of units when it is an item.</summary>
    public uint MoneyType { get; }

    /// <summary>Total cost for the requested count.</summary>
    public uint Cost { get; }

    /// <summary>Genre/Detail/Particular/Level of the item price, when <see cref="IsCurrency"/> is false.</summary>
    public uint[]? ItemGdpl { get; }

    /// <summary>
    /// Parses one raw price column into a single line, or null when the column is absent, zero or an
    /// unrecognised length (a free good is expressed by having no usable price at all).
    /// </summary>
    private static ShopPrice? Parse(uint[]? numbers, uint count)
    {
        if (numbers == null || numbers.Length == 0 || numbers[0] == 0)
            return null;

        var cost = numbers[^1] * count;

        if (numbers.Length >= 5)
            return new ShopPrice(false, 0, cost, [numbers[0], numbers[1], numbers[2], numbers[3]]);

        // The short form is {moneyType, cost}; any other length is a price we should not guess at.
        if (numbers.Length != 2)
            return null;

        return new ShopPrice(true, numbers[0], cost, null);
    }

    /// <summary>
    /// Every price the buyer has to pay, mirroring Shop.lua:717-743. <c>nCalculation</c> picks how the
    /// two columns combine:
    ///   1 -> exactly one of Price1/Price2, chosen by <paramref name="buyMode"/> (2 = Price2)
    ///   2 -> BOTH columns are charged
    ///   0 -> Price1 only (Price2 is ignored)
    /// </summary>
    public static List<ShopPrice> ResolveAll(ShopGoodsItem goods, uint buyMode, uint count)
    {
        var prices = new List<ShopPrice>();

        switch (goods.Calculation)
        {
            case 1:
                var chosen = buyMode == 2 ? goods.Price2 : goods.Price1;
                Add(prices, chosen, count);
                break;

            case 2:
                Add(prices, goods.Price1, count);
                Add(prices, goods.Price2, count);
                break;

            default:
                Add(prices, goods.Price1, count);
                break;
        }

        return prices;
    }

    private static void Add(List<ShopPrice> prices, uint[]? numbers, uint count)
    {
        var price = Parse(numbers, count);
        if (price != null)
            prices.Add(price);
    }

    /// <summary>
    /// Checks the whole basket before anything is deducted, so a two-currency purchase can never take
    /// the first currency and then fail on the second. Returns null when the player can afford
    /// everything, otherwise the client error key (Shop.lua:731-740 raises tip.NotEnoughItem for an
    /// item price and tip.gold_not_enough for a currency price).
    /// </summary>
    public static string? CheckAffordable(PlayerInstance player, IReadOnlyList<ShopPrice> prices)
    {
        foreach (var price in prices)
        {
            var error = price.CheckAffordable(player);
            if (error != null)
                return error;
        }

        return null;
    }

    private string? CheckAffordable(PlayerInstance player)
    {
        if (Cost == 0)
            return null;

        if (IsCurrency)
        {
            if (CurrencyService.GetBalance(player, MoneyType) >= Cost)
                return null;

            return MoneyType == AttrIds.Currency.Vigor ? "tip.vigor_not_enough" : "tip.gold_not_enough";
        }

        if (ItemGdpl == null)
            return "tip.NotEnoughItem";

        return CurrencyService.GetItemCount(player, ItemGdpl) >= Cost ? null : "tip.NotEnoughItem";
    }

    /// <summary>
    /// Charges one line. Only call after <see cref="CheckAffordable(PlayerInstance, IReadOnlyList{ShopPrice})"/>
    /// has passed for the whole basket. Returns null on success, otherwise the client error key.
    /// </summary>
    public string? Charge(PlayerInstance player, NtfSyncPlayer sync)
    {
        if (Cost == 0)
            return null;

        if (IsCurrency)
        {
            return CurrencyService.TrySpend(player, MoneyType, Cost, sync, out var error)
                ? null
                : error ?? "tip.gold_not_enough";
        }

        if (ItemGdpl == null)
            return "tip.NotEnoughItem";

        if (!CurrencyService.TrySpendItem(player, ItemGdpl, Cost, out var itemError))
            return itemError;

        SyncItem(player, ItemGdpl);
        return null;
    }

    /// <summary>
    /// Mirrors the spent item back to the client. ShopLogic_BuyGoods does the same for its own item
    /// price (PacketNtfCallScript with the wallet entry); without it the bag keeps showing the old
    /// count until the next full snapshot.
    /// </summary>
    private static void SyncItem(PlayerInstance player, uint[] gdpl)
    {
        var templateId = GameResourceTemplateId.FromGdpl(gdpl[0], gdpl[1], gdpl[2], gdpl[3]);
        var item = player.InventoryManager.InventoryData.Items.Values
            .FirstOrDefault(x => x.TemplateId == templateId);

        if (item != null)
            _ = player.SendPacket(new PacketNtfCallScript([item]));
    }

    public string Describe() => IsCurrency
        ? $"{Cost}x {CurrencyService.Describe(MoneyType)}"
        : $"item {string.Join('-', ItemGdpl ?? [])} x{Cost}";

    public static string DescribeAll(IReadOnlyList<ShopPrice> prices) =>
        prices.Count == 0 ? "free" : string.Join(" + ", prices.Select(x => x.Describe()));

    public JsonObject ToJson()
    {
        var json = new JsonObject
        {
            ["isCurrency"] = IsCurrency,
            ["cost"] = (long)Cost
        };

        if (IsCurrency)
        {
            json["moneyType"] = (int)MoneyType;
        }
        else if (ItemGdpl != null)
        {
            var gdpl = new JsonArray();
            foreach (var value in ItemGdpl)
                gdpl.Add((long)value);
            json["gdpl"] = gdpl;
        }

        return json;
    }
}
