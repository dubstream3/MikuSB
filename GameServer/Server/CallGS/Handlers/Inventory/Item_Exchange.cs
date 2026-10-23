using MikuSB.Data;
using MikuSB.Enums.Item;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Inventory;

// Gacha ticket top-up (抽奖兑换). The pulling screen sends this only when its own cost check failed
// because the player is short of the pulling ticket (Script/UMG/GachaExchange/umg_gacha_exchange.lua:27):
//   { "tbGDPLN": [g, d, p, l, nNeed] }
// nNeed is the number of *tickets* still missing (umg_gacha_exchange.lua:60,:85) -- the UI itself
// multiplies it by the per-ticket price at :64,:71. The server therefore prices the exchange from
// item/exchange.json alone and charges count * ratio, so a tampered payload can never buy a cheaper
// ticket; asking for more just costs proportionally more.
//
// The client s2c handler for this API reads no field at all and only fires Event.ExchangeSuc
// (Script/Item/Item.lua:1124-1129), so an empty object is a valid answer and the exchanged state
// reaches the player through the NtfSyncPlayer payload.
[CallGSApi("Item_Exchange")]
public class Item_Exchange : CallGSHandler<ItemExchangeParam>
{
    private static readonly Logger Log = new(nameof(Item_Exchange));

    // The window only ever tops a single pull or a ten-pull up (gacha.json CastOne/CastTen), so a
    // sane request never exceeds 10 tickets; the cap merely keeps a tampered count from turning the
    // cost multiplication into an overflow.
    private const uint MaxTicketCount = 100;

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, ItemExchangeParam req)
    {
        var player = context.Connection.Player!;

        if (req?.TbGDPLN == null || req.TbGDPLN.Count < 5)
        {
            return CallGSResult.Error("error.BadParam");
        }

        var g = req.TbGDPLN[0];
        var d = req.TbGDPLN[1];
        var p = req.TbGDPLN[2];
        var l = req.TbGDPLN[3];
        var count = req.TbGDPLN[4];

        if (count == 0 || count > MaxTicketCount)
        {
            return CallGSResult.Error("error.BadParam");
        }

        var templateId = GameResourceTemplateId.FromGdpl(g, d, p, l);

        if (!GameData.ItemExchangeData.TryGetValue(templateId, out var exchange))
        {
            Log.Warn($"No exchange price for {g}-{d}-{p}-{l}, uid={player.Uid}.");
            return CallGSResult.Error("error.BadParam");
        }

        var moneyType = exchange.CashId;
        var ratio = exchange.CashRatio;

        if (moneyType == 0 || ratio == 0)
        {
            Log.Warn($"Exchange row {g}-{d}-{p}-{l} has no usable Cash price, uid={player.Uid}.");
            return CallGSResult.Error("error.BadParam");
        }

        // Refuse before charging when the item tables have no template to build the ticket from:
        // RewardManager.GrantRewardAsync returns false for an unknown template, which would otherwise
        // take the money and grant nothing.
        if (!HasTicketTemplate(templateId, g))
        {
            Log.Warn($"Exchange ticket {g}-{d}-{p}-{l} has no item template, uid={player.Uid}.");
            return CallGSResult.Error("error.BadParam");
        }

        var cost = count * ratio;
        var sync = new NtfSyncPlayer();

        if (!CurrencyService.TrySpend(player, moneyType, cost, sync, out var spendError))
        {
            Log.Info($"Exchange refused, uid={player.Uid} {count}x {g}-{d}-{p}-{l} " +
                     $"costs {cost}x {CurrencyService.Describe(moneyType)}: {spendError}");
            return CallGSResult.Error(spendError);
        }

        var ticketRow = new uint[] { g, d, p, l, count };
        await player.RewardManager.GrantConfiguredRewardsAsync([ticketRow], sync);
        CurrencyService.Persist(player);

        Log.Info($"Exchange, uid={player.Uid} {count}x {g}-{d}-{p}-{l} " +
                 $"for {cost}x {CurrencyService.Describe(moneyType)} (ratio={ratio})");

        return CallGSResult.Ok("{}", sync);
    }

    // Tickets are supplies in the shipped table (Genre 5, the supplies.gacha1/gacha2 rows of
    // item/templates/suplies.json); RewardManager keys supplies and useable items through two
    // different dictionaries, so mirror that split instead of guessing one of them.
    private static bool HasTicketTemplate(ulong templateId, uint genre) => genre switch
    {
        (uint)ItemTypeEnum.TYPE_SUPPLIES => GameData.SuppliesData.ContainsKey(templateId),
        (uint)ItemTypeEnum.TYPE_USEABLE => GameData.OtherItemData.ContainsKey(templateId),
        _ => false
    };
}

public sealed class ItemExchangeParam
{
    // umg_gacha_exchange.lua:85 sends {tbGDPLN = {g, d, p, l, nNeed}} and nothing else.
    [JsonPropertyName("tbGDPLN")] public List<uint>? TbGDPLN { get; set; }
}
