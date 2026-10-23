using MikuSB.Data;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.Util;
using System.Text.Json.Nodes;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Shop;

// Answers the shop tab time request the client sends when a shop UI is opened. The client copies
// nBegin/nEnd straight into ShopLogic.tbShopTabConf[shopId] (Shop.lua:651-660) and re-runs its tab
// refresh, so every shipped tab window - all of which expired long ago - is replaced with an
// open-ended one here (崇福集市 / Dlc35Shop 145,146 among them).
// The main-shop tabs are merged in as well: Shop.lua:655 looks each key up in the SAME tab
// configuration table, and unlike the DLC tabs the main tabs are not dead code (see
// docs/progress/012 for the DLC-only reading). ShopId and DLC shop id are disjoint spaces, so the
// two sets cannot collide.
// Request: {} (Shop.lua:647, no parameters)
// Response: { "<shopId>": { nBegin, nEnd } } with unix seconds
[CallGSApi("ShopLogic_GetOpenTime")]
public class ShopLogic_GetOpenTime : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var payload = DlcRuntimeOverrides.BuildShopOpenTimePayload();
        var (start, end) = DlcRuntimeOverrides.Window();

        foreach (var tab in GameData.ShopTabData.Values)
        {
            var key = tab.Id.ToString();
            if (payload.ContainsKey(key))
                continue;

            payload[key] = new JsonObject
            {
                ["nBegin"] = start,
                ["nEnd"] = end
            };
        }

        Logger.GetByClassName().Info(
            $"Returned open times for {GameData.DlcShopData.Count} DLC shop tab(s) and " +
            $"{GameData.ShopTabData.Count} main shop tab(s).");

        return Task.FromResult(CallGSResult.Ok(payload.ToJsonString()));
    }
}
