using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database.Inventory;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Game.Gacha;

/// <summary>
/// Ticket price of one gacha pull, read from the CastOne/CastTen/CastSpecial columns of gacha.json
/// exactly the way the client reads them (Gacha.lua:813-874 GachaTemplate:CheckSpecialCost and
/// GachaTemplate:CheckCost). A pull is paid with event tickets, banner tickets or a mix of both:
///   CastOne / CastTen  [[Genre, Detail, Particular, Level, Count], ...] - the amount the pull costs
///                      is the Count of the first row (a ten pull may be discounted, gacha.json
///                      charges 8 for 10 on some banners), the other rows are tickets it accepts;
///   CastSpecial        [Genre, Detail, Particular, Level, Count] of the event ticket, or nothing.
///                      Gacha.lua:817 addresses it with table.unpack, so its Count field is unused
///                      and the ticket is spent up to the amount the pull needs.
/// Gacha.lua:851-858 walks the rows in order and keeps the LAST one that alone covers what the event
/// tickets left over, so that is the row charged here too. When neither the event tickets nor any
/// single row covers the pull, the client refuses the request and the server answers
/// tip.NotEnoughItem, which Gacha.lua:469 shows as a tip.
/// </summary>
public sealed class GachaPrice
{
    private readonly List<uint[]> _rows;
    private readonly uint[]? _special;
    private readonly uint _need;
    private readonly List<(uint[] Gdpl, uint Count)> _charged = [];

    private GachaPrice(List<uint[]> rows, uint[]? special, uint need)
    {
        _rows = rows;
        _special = special;
        _need = need;
    }

    /// <summary>
    /// Reads the cost column of the requested pull size. Returns null when the banner has no usable
    /// row for it - a malformed row or a pull size the banner does not sell is a data error, not
    /// something the player can fix by holding more tickets.
    /// </summary>
    public static GachaPrice? Resolve(GachaExcel cfg, uint nTime)
    {
        // Gacha.lua:916-917 loads CastOne for a single pull and CastTen for a ten pull.
        var rows = nTime == 1 ? cfg.CastOne : cfg.CastTen;
        if (rows.Count == 0)
            return null;

        // Gacha.lua:837 - "tbCost[1][5] or nTime": the count column of the first row is what the
        // pull costs, and a row without a count costs as many tickets as the pull draws cards.
        var need = rows[0][4] != 0 ? rows[0][4] : nTime;

        return new GachaPrice(rows, cfg.CastSpecial, need);
    }

    /// <summary>
    /// Charges what the player can pay with, mirroring GachaTemplate:CheckCost (Gacha.lua:830-874):
    /// event tickets first - never more than the pull costs - then the last row covering the
    /// remainder. Returns null on success, otherwise the client error key to send back.
    /// </summary>
    public string? Charge(PlayerInstance player, NtfSyncPlayer sync)
    {
        var specialCount = _special == null ? 0 : Math.Min(CountItem(player, _special), _need);

        if (_special != null && specialCount >= _need)
        {
            // Gacha.lua:838-841 - the event tickets alone cover the whole pull.
            return Spend(player, sync, _special, _need);
        }

        // Gacha.lua:851-858 - the loop does not break, so the last row that is affordable wins.
        uint[]? regular = null;
        foreach (var row in _rows)
        {
            if (CountItem(player, row) >= _need - specialCount)
                regular = row;
        }

        // regular[4] < specialCount would underflow the remainder; gacha.json never mixes a row
        // that short with a special ticket, but a hand-edited banner must not turn into a free pull.
        if (regular == null || regular[4] < specialCount)
            return "tip.NotEnoughItem";

        // A banner whose event ticket is also one of its regular tickets would spend from that stack
        // twice, and the second spend could fail after the first one already charged. gacha.json
        // never does this (its event ticket is 5-21-4-5), so pay the pull with a single spend from
        // the shared stack and refuse it when that stack cannot cover the whole price.
        if (specialCount > 0 && SharesTemplate(_special!, regular))
        {
            return CountItem(player, regular) >= regular[4]
                ? Spend(player, sync, regular, regular[4])
                : "tip.NotEnoughItem";
        }

        if (specialCount > 0 && Spend(player, sync, _special!, specialCount) is { } specialError)
            return specialError;

        return Spend(player, sync, regular, regular[4] - specialCount);
    }

    /// <summary>
    /// Describes the tickets <see cref="Charge"/> actually spent, e.g. "5-21-4-5 x3 + 5-21-1-5 x7".
    /// Empty before the charge has run.
    /// </summary>
    public string Describe() => string.Join(" + ",
        _charged.Select(part => $"{string.Join('-', part.Gdpl[..4])} x{part.Count}"));

    private string? Spend(PlayerInstance player, NtfSyncPlayer sync, uint[] gdpl, uint count)
    {
        if (count == 0)
            return null;

        if (!CurrencyService.TrySpendItem(player, gdpl, count, out var error))
            return error;

        _charged.Add((gdpl, count));
        SyncItem(player, gdpl, sync);
        return null;
    }

    /// <summary>
    /// Mirrors a spent ticket stack back to the client, the way ShopPrice.SyncItem does for the
    /// shop: TrySpendItem only moves the stored count, so without this the bag keeps showing the old
    /// one. Gacha_Launch already answers with a sync, so the stack rides along with the response
    /// instead of a packet of its own.
    /// </summary>
    private static void SyncItem(PlayerInstance player, uint[] gdpl, NtfSyncPlayer sync)
    {
        if (FindStack(player, gdpl) is { } stack)
            sync.Items.Add(stack.ToProto());
    }

    /// <summary>
    /// Counts a ticket stack exactly the way CurrencyService.TrySpendItem spends from it (the first
    /// stack of the template). me:GetItemCount (Gacha.lua:814) sums every stack instead, but tickets
    /// are merged into one stack per template by InventoryManager.AddSuppliesItem and AddOtherItem,
    /// so checking the price and spending it cannot disagree.
    /// </summary>
    private static uint CountItem(PlayerInstance player, uint[] gdpl) => FindStack(player, gdpl)?.ItemCount ?? 0;

    /// <summary>True when two cost rows name the same item, so both would spend from one stack.</summary>
    private static bool SharesTemplate(uint[] left, uint[] right)
    {
        for (var i = 0; i < 4; i++)
        {
            if (left[i] != right[i])
                return false;
        }

        return true;
    }

    private static BaseGameItemInfo? FindStack(PlayerInstance player, uint[] gdpl)
    {
        var templateId = GameResourceTemplateId.FromGdpl(gdpl[0], gdpl[1], gdpl[2], gdpl[3]);
        return player.InventoryManager.InventoryData.Items.Values
            .FirstOrDefault(x => x.TemplateId == templateId);
    }
}
