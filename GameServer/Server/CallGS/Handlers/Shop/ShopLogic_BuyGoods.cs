using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.Packet.Send.Misc;
using MikuSB.Proto;
using MikuSB.Util;
using Newtonsoft.Json.Linq;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ShopPrice = global::MikuSB.GameServer.Game.Shop.ShopPrice;
using ShopRefreshService = global::MikuSB.GameServer.Game.Shop.ShopRefreshService;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Shop;

// Purchase request from the DLC shop (Shop.lua:690 BuyGoods -> ShopLogic_BuyGoods). The client
// validates time/stock/affordability locally and then expects the server to charge the price and
// hand back the granted item row; Shop.lua:931 plays the gain UI from tbParam.tbGoods, which
// ShopLogic.GetActualItemsUseMode reads as a single [Genre, Detail, Particular, Level, Count] row.
// Request: { nGoodsId, nBuyMode, nCount }
// Response: { nGoodsId, tbGoods: [G, D, P, L, Count] }
[CallGSApi("ShopLogic_BuyGoods")]
public class ShopLogic_BuyGoods : CallGSHandler<ShopLogicBuyGoodsParam>
{
    // A static logger keeps the module name readable: GetByClassName() inside an async
    // method resolves to the compiler-generated state machine (<HandleAsync>d__4).
    private static readonly Logger Log = new(nameof(ShopLogic_BuyGoods));

    private const uint BuyGroupId = AttrIds.Shop.PurchaseGid;
    private const uint ShopStrGid = AttrIds.Shop.Gid;
    private const uint GoodsStartSid = AttrIds.Shop.GoodsStartSid;
    private const uint MaxBuyCount = 99;

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, ShopLogicBuyGoodsParam request)
    {
        var player = context.Connection.Player!;

        if (request.GoodsId == 0 || request.Count == 0 || request.Count > MaxBuyCount)
            return CallGSResult.Error("error.BadParam");

        // The main shop (shop_tab.json, the 16 tabs of the current live client) comes first; the DLC
        // shops keep their own table and are only reached when the id is not a main-shop goods.
        if (GameData.ShopGoodsData.TryGetValue(request.GoodsId, out var shopGoods))
            return await HandleMainShopAsync(player, shopGoods, request);

        if (!GameData.DlcGoodsData.TryGetValue(request.GoodsId, out var goods))
            return CallGSResult.Error("error.BadParam");

        if (!TryReadRow(goods.ExtraData, "GDPLN", out var giveRow))
        {
            Log.Warn($"Goods {request.GoodsId} has no usable GDPLN row.");
            return CallGSResult.Error("error.BadParam");
        }

        var limitType = ReadLong(goods.ExtraData, "LimitType");
        var limitNum = ReadLong(goods.ExtraData, "LimitNum");

        // The DLC tables carry the same LimitType/LimitNum columns as the main shop, so an expired
        // period has to release its counter here too. DlcShopExcel is only registered for the DLC
        // shops, so a lookup miss simply means "no period configured" and changes nothing.
        if (GameData.ShopTabData.TryGetValue(goods.ShopId, out var dlcTab))
        {
            ShopRefreshService.RollWindows(player, dlcTab, out _, out var dlcLimitsRolled, out _);
            if (dlcLimitsRolled)
                ShopRefreshService.ResetPurchaseCounters(player, dlcTab);
        }

        var buyAttr = player.Attributes.GetOrCreate(BuyGroupId, request.GoodsId);

        if (limitType > 0 && limitNum > 0 && buyAttr.Val + request.Count > limitNum)
            return CallGSResult.Error("ui.TxtLimitBuy");

        // Price1 is the primary currency, Price2 the alternate one; nBuyMode picks between them
        // for goods whose nCalculation == 1 (Shop.lua:719-725).
        var priceName = request.BuyMode == 2 ? "Price2" : "Price1";
        if (!TryReadRow(goods.ExtraData, priceName, out var priceRow) &&
            !TryReadRow(goods.ExtraData, "Price1", out priceRow))
        {
            Log.Warn($"Goods {request.GoodsId} has no usable price row.");
            return CallGSResult.Error("error.BadParam");
        }

        var cost = priceRow[4] * request.Count;
        var priceTemplate = GameResourceTemplateId.FromGdpl(priceRow[0], priceRow[1], priceRow[2], priceRow[3]);
        var wallet = player.InventoryManager.InventoryData.Items.Values
            .FirstOrDefault(x => x.TemplateId == priceTemplate);

        if (wallet == null || wallet.ItemCount < cost)
            return CallGSResult.Error("tip.NotEnoughItem");

        wallet.ItemCount -= cost;
        await player.SendPacket(new PacketNtfCallScript([wallet]));

        var sync = new NtfSyncPlayer();

        var grantedRow = new uint[]
        {
            giveRow[0], giveRow[1], giveRow[2], giveRow[3],
            giveRow[4] * request.Count
        };

        await player.RewardManager.GrantConfiguredRewardsAsync([grantedRow], sync);

        buyAttr.Val += request.Count;
        player.Attributes.SyncTo(sync, buyAttr);

        SyncGoodsData(player, request.GoodsId, buyAttr.Val, sync);

        DatabaseHelper.SaveDatabaseType(player.Data);
        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
        DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);

        var responseRow = new JsonArray();
        foreach (var value in grantedRow)
            responseRow.Add((int)value);

        var payload = new JsonObject
        {
            ["nGoodsId"] = (int)request.GoodsId,
            ["tbGoods"] = responseRow
        };

        Log.Info(
            $"Shop {goods.ShopId} goods {request.GoodsId} x{request.Count} bought by uid={player.Uid} " +
            $"(paid {cost}x {priceRow[0]}-{priceRow[1]}-{priceRow[2]}-{priceRow[3]}, " +
            $"granted {grantedRow[0]}-{grantedRow[1]}-{grantedRow[2]}-{grantedRow[3]} x{grantedRow[4]}).");

        return CallGSResult.Ok(payload.ToJsonString(), sync);
    }

    /// <summary>
    /// Purchase path for the main shop tabs. Unlike the DLC tables - which key their prices by a
    /// JSON column name - the converted shop_tab.json keeps Price1/Price2 raw so that
    /// <c>ShopPrice</c> can read both config shapes: {moneyType, cost} for a currency and
    /// {Genre, Detail, Particular, Level, cost} for an inventory item, and can combine the two
    /// columns for the goods whose nCalculation is 1 (alternate) or 2 (pay both).
    /// </summary>
    private async Task<CallGSResult> HandleMainShopAsync(
        PlayerInstance player,
        ShopGoodsItem goods,
        ShopLogicBuyGoodsParam request)
    {
        if (goods.Gdpln is not { Length: >= 5 })
        {
            Log.Warn($"Goods {request.GoodsId} has no usable GDPLN row.");
            return CallGSResult.Error("error.BadParam");
        }

        // The purchase counters are period-scoped, so a limit that expired since the last purchase
        // has to be released before the check - the client cannot be trusted to have opened the shop
        // (and therefore to have rolled the window) first.
        if (GameData.ShopTabData.TryGetValue(goods.ShopId, out var tab))
        {
            ShopRefreshService.RollWindows(player, tab, out _, out var limitsRolled, out _);
            if (limitsRolled)
                ShopRefreshService.ResetPurchaseCounters(player, tab);
        }

        var buyAttr = player.Attributes.GetOrCreate(BuyGroupId, request.GoodsId);

        if (goods.LimitType > 0 && goods.LimitNum > 0 && buyAttr.Val + request.Count > goods.LimitNum)
            return CallGSResult.Error("ui.TxtLimitBuy");

        var prices = ShopPrice.ResolveAll(goods, request.BuyMode, request.Count);

        // Check the whole basket first: with nCalculation == 2 there are two currencies to pay, and
        // taking the first one before discovering the second is short would silently eat it.
        if (ShopPrice.CheckAffordable(player, prices) is { } affordError)
            return CallGSResult.Error(affordError);

        var sync = new NtfSyncPlayer();

        foreach (var price in prices)
        {
            if (price.Charge(player, sync) is { } priceError)
                return CallGSResult.Error(priceError);
        }

        var grantedRow = new uint[]
        {
            goods.Gdpln[0], goods.Gdpln[1], goods.Gdpln[2], goods.Gdpln[3],
            goods.Gdpln[4] * request.Count
        };

        await player.RewardManager.GrantConfiguredRewardsAsync([grantedRow], sync);

        buyAttr.Val += request.Count;
        player.Attributes.SyncTo(sync, buyAttr);

        SyncGoodsData(player, request.GoodsId, buyAttr.Val, sync);

        // The currencies live on Player.Data, the bought item on InventoryData and anything the
        // reward row unpacked (characters, weapons, skins) on CharacterData.
        DatabaseHelper.SaveDatabaseType(player.Data);
        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
        DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);

        var responseRow = new JsonArray();
        foreach (var value in grantedRow)
            responseRow.Add((int)value);

        var payload = new JsonObject
        {
            ["nGoodsId"] = (int)request.GoodsId,
            ["tbGoods"] = responseRow
        };

        Log.Info(
            $"Shop {goods.ShopId} goods {request.GoodsId} x{request.Count} bought by uid={player.Uid} " +
            $"(paid {ShopPrice.DescribeAll(prices)}, " +
            $"granted {grantedRow[0]}-{grantedRow[1]}-{grantedRow[2]}-{grantedRow[3]} x{grantedRow[4]}).");

        return CallGSResult.Ok(payload.ToJsonString(), sync);
    }

    // Shop.lua:472 GetBuyNum reads (1, GoodsStart + goodsid).buynum for the "bought x / limit y"
    // label in the shop tile.
    private static void SyncGoodsData(PlayerInstance player, uint goodsId, uint bought, NtfSyncPlayer sync)
    {
        var goodsData = new JsonObject
        {
            ["buynum"] = (int)bought,
            ["invalidtime"] = 0
        };

        var attr = player.Attributes.SetString(ShopStrGid, GoodsStartSid + goodsId, goodsData.ToJsonString());
        player.Attributes.SyncTo(sync, attr);
    }

    private static bool TryReadRow(IDictionary<string, JToken> data, string key, out uint[] row)
    {
        row = [];

        if (!data.TryGetValue(key, out var token) || token is not JArray array || array.Count < 5)
            return false;

        row = new uint[5];
        for (var i = 0; i < 5; i++)
            row[i] = DlcRuntimeOverrides.ReadUint(array[i]);

        return row[0] != 0;
    }

    private static long ReadLong(IDictionary<string, JToken> data, string key)
    {
        if (!data.TryGetValue(key, out var token) || token == null)
            return 0;

        return token.Type switch
        {
            JTokenType.Integer => token.Value<long>(),
            JTokenType.Float => (long)token.Value<double>(),
            JTokenType.String => long.TryParse(token.Value<string>()?.Trim('[', ']', '"'), out var parsed)
                ? parsed
                : 0,
            _ => 0
        };
    }
}

public sealed class ShopLogicBuyGoodsParam
{
    [JsonPropertyName("nGoodsId")] public uint GoodsId { get; set; }
    [JsonPropertyName("nBuyMode")] public uint BuyMode { get; set; }
    [JsonPropertyName("nCount")] public uint Count { get; set; } = 1;
}
