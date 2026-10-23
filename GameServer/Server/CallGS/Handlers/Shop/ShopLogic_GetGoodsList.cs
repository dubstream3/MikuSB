using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.Shop;
using MikuSB.Proto;
using MikuSB.Util;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Shop;

// Answers the goods list request a DLC shop UI sends on open (Shop.lua:750 GetGoodsList ->
// ShopLogic_GetGoodsList). The client resolves prices, stock and icons through its own
// ShopLogic.GetGoodsInfo(goodsid) (local dlc_goods.txt), but ShopLogic.BuyGoods (Shop.lua:690)
// additionally needs the server-authored on-line snapshot that lives in the string attribute
// (AttrIds.Shop.Gid, shopId): Shop.lua:277 GetShopData / :582 GetOnLineGoodsInfo refuse the
// purchase with ui.TxtNotOpen when the cache is missing or does not list the goods.
// Request: { nShopId }
// Response: { shopId, goodsList: [ { goodsid }, ... ] } + string-attribute sync
[CallGSApi("ShopLogic_GetGoodsList")]
public class ShopLogic_GetGoodsList : CallGSHandler<ShopLogicGetGoodsListParam>
{
    private const uint ShopStrGid = AttrIds.Shop.Gid;
    private const uint GoodsStartSid = AttrIds.Shop.GoodsStartSid;
    private const uint BuyGroupId = AttrIds.Shop.PurchaseGid;

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, ShopLogicGetGoodsListParam request)
    {
        if (request.ShopId == 0)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        return Task.FromResult(BuildGoodsList(context.Connection.Player!, request.ShopId, null));
    }

    /// <summary>
    /// Builds the goods-list answer. <paramref name="api"/> overrides the s2c callback name: the
    /// manual refresh (Shop.lua:845 ShopLogic.UpdateShop -> :881 me:CallGS("ShopLogic_UpdateShop"))
    /// registers no callback of its own - it only shows the connection spinner and waits for the
    /// goods list - so echoing the request name back makes Misc/S2C.lua:32 raise
    /// "No command named: ShopLogic_UpdateShop" (observed 2026-10-05 23:32:50).
    /// <paramref name="sync"/> lets the manual refresh hand over the snapshot that already carries
    /// the fee deduction instead of dropping it on the floor.
    /// </summary>
    internal static CallGSResult BuildGoodsList(PlayerInstance player, uint shopId, string? api,
        NtfSyncPlayer? existingSync = null)
    {
        var goodsList = new JsonArray();
        var onlineGoods = new JsonArray();
        var sync = existingSync ?? new NtfSyncPlayer();

        // version / listversion come from the tab row. Shop.lua:254 IsShowRedDot compares them with
        // the client's own copy of shop_tab and keeps the tab marked as "new" until they match, so a
        // hardcoded 0 lights the red dot on every tab that ships with a real version.
        uint version = 0;
        uint listVersion = 0;
        uint refreshNum = 0;

        if (GameData.ShopTabData.TryGetValue(shopId, out var tab))
        {
            version = tab.Version;
            listVersion = tab.ListVersion;

            // Roll the shelf / purchase-limit periods before answering. The client computes the same
            // boundaries locally (Shop.lua:403 GetNextRefreshTime), so agreeing here is what keeps the
            // countdown it shows honest and makes a new period actually restore the limits.
            ShopRefreshService.RollWindows(player, tab, out _, out var limitsRolled, out var windowsChanged);
            if (limitsRolled)
            {
                var cleared = ShopRefreshService.ResetPurchaseCounters(player, tab);
                Logger.GetByClassName().Info($"Shop {shopId}: reset {cleared} purchase counter(s) for uid={player.Uid}.");
            }

            if (windowsChanged)
                DatabaseHelper.SaveDatabaseType(player.Data);

            refreshNum = ShopRefreshService.RefreshNum(player, shopId);

            foreach (var goods in tab.Goods.OrderBy(x => x.PosIndex).ThenBy(x => x.Id))
            {
                goodsList.Add(new JsonObject { ["goodsid"] = (int)goods.Id });
                onlineGoods.Add(BuildOnlineGoods(goods.Id, goods.Discount));
                SyncGoodsData(player, goods.Id, sync);
            }
        }
        else if (GameData.DlcGoodsByShop.TryGetValue(shopId, out var rows))
        {
            foreach (var row in rows.OrderBy(x => x.PosIndex).ThenBy(x => x.GoodsId))
            {
                goodsList.Add(new JsonObject { ["goodsid"] = (int)row.GoodsId });

                // GetOnLineGoodsInfo returns (goodsid, discountindex); BuyGoods treats a falsy
                // discountindex as "not on sale" and aborts, so every entry carries one.
                onlineGoods.Add(BuildOnlineGoods(row.GoodsId, null));

                SyncGoodsData(player, row.GoodsId, sync);
            }
        }
        else
        {
            Logger.GetByClassName().Warn($"No goods configured for shop {shopId}.");
        }

        var shopData = new JsonObject
        {
            ["shopid"] = (int)shopId,
            ["version"] = (int)version,
            ["listversion"] = (int)listVersion,
            ["refreshnum"] = (int)refreshNum,
            ["displaynum"] = 0,
            ["refreshtime"] = 0,
            ["tbgoods"] = onlineGoods
        };

        var shopAttr = player.Attributes.SetString(ShopStrGid, shopId, shopData.ToJsonString());
        player.Attributes.SyncTo(sync, shopAttr);

        var payload = new JsonObject
        {
            ["shopId"] = (int)shopId,
            ["goodsList"] = goodsList
        };

        Logger.GetByClassName().Info($"Returned {goodsList.Count} goods for shop {shopId}.");

        return CallGSResult.Ok(payload.ToJsonString(), sync, api);
    }

    // Shop.lua:582 GetOnLineGoodsInfo returns (goodsid, discountindex) and BuyGoods treats a falsy
    // discountindex as "not on sale" and aborts, so every entry carries one. The client only enters
    // its discount branch when the index is greater than 0 (Shop.lua:715), so 0 stays safe while a
    // configured Discount column is passed through for shops that do run one.
    private static JsonObject BuildOnlineGoods(uint goodsId, uint[]? discount)
    {
        var index = discount is { Length: > 0 } ? (int)discount[0] : 0;

        return new JsonObject
        {
            ["goodsid"] = (int)goodsId,
            ["discountindex"] = Math.Max(0, index)
        };
    }

    // Shop.lua:472 GetBuyNum reads (1, GoodsStart + goodsid).buynum; the same counter is kept
    // numerically in AttrIds.Shop.PurchaseGid so the purchase handler can enforce the limit.
    private static void SyncGoodsData(PlayerInstance player, uint goodsId, NtfSyncPlayer sync)
    {
        var bought = player.Attributes.GetValue(BuyGroupId, goodsId);
        var goodsData = new JsonObject
        {
            ["buynum"] = (int)bought,
            ["invalidtime"] = 0
        };

        var attr = player.Attributes.SetString(ShopStrGid, GoodsStartSid + goodsId, goodsData.ToJsonString());
        player.Attributes.SyncTo(sync, attr);
    }
}

public sealed class ShopLogicGetGoodsListParam
{
    [JsonPropertyName("nShopId")] public uint ShopId { get; set; }
}
