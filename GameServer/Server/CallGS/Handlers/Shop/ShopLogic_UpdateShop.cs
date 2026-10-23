using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Shop;
using MikuSB.Proto;
using MikuSB.Util;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Shop;

// Manual shop refresh (Shop.lua:845 ShopLogic.UpdateShop -> :881 me:CallGS("ShopLogic_UpdateShop")).
// The client registers no callback under this request name - Shop.lua only has three shop
// registrations (:651 ShopLogic_GetOpenTime, :890 ShopLogic_GetGoodsList, :931 ShopLogic_BuyGoods).
// UpdateShop shows the connection spinner first (:884 UI.ShowConnection()) and only the goods-list
// callback closes it (:891 UI.CloseConnection()), so the answer must be sent under that name.
// Echoing ShopLogic_UpdateShop back made Misc/S2C.lua:32 raise
// "s2c.Dispatch error. No command named: ShopLogic_UpdateShop" (2026-10-05 23:32:50) and left the
// spinner up forever.
// Request: { nShopId }
// Response: the ShopLogic_GetGoodsList payload, under the api name ShopLogic_GetGoodsList.
[CallGSApi("ShopLogic_UpdateShop")]
public class ShopLogic_UpdateShop : CallGSHandler<ShopLogicUpdateShopParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, ShopLogicUpdateShopParam request)
    {
        if (request.ShopId == 0)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var player = context.Connection.Player!;

        // The client refuses the refresh locally once ShopLogic.GetShopRefreshNnm reaches
        // info.nRefreshLimits (Shop.lua:856) and pre-checks the fee (Shop.lua:862), but both checks
        // read values we author, so the charge is enforced here as well.
        if (GameData.ShopTabData.TryGetValue(request.ShopId, out var tab))
        {
            var sync = new NtfSyncPlayer();

            if (ShopRefreshService.TryManualRefresh(player, tab, sync) is { } refreshError)
                return Task.FromResult(CallGSResult.Error(refreshError));

            DatabaseHelper.SaveDatabaseType(player.Data);

            Logger.GetByClassName().Info(
                $"Manual refresh of shop {request.ShopId} for uid={player.Uid} " +
                $"({ShopRefreshService.RefreshNum(player, request.ShopId)}/{tab.RefreshLimits}).");

            // Reuse the sync that carries the fee deduction, so the client sees the new balance and
            // the refreshed shelf in the same answer.
            return Task.FromResult(
                ShopLogic_GetGoodsList.BuildGoodsList(player, request.ShopId, "ShopLogic_GetGoodsList", sync));
        }

        Logger.GetByClassName().Info(
            $"Manual refresh of shop {request.ShopId} for uid={player.Uid} (no main-shop tab; list re-sent).");

        return Task.FromResult(
            ShopLogic_GetGoodsList.BuildGoodsList(player, request.ShopId, "ShopLogic_GetGoodsList"));
    }
}

public sealed class ShopLogicUpdateShopParam
{
    [JsonPropertyName("nShopId")] public uint ShopId { get; set; }
}
