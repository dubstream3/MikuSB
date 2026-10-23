using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// DLC shop goods (dlc/dlc_goods.json). The client resolves price, stock, icon and time window from
/// its own copy of this table (Shop.lua:199 LoadDlcShopConf -> ShopLogic.tbGoodsConf), so the server
/// only needs the goods id list per tab in order to answer ShopLogic_GetGoodsList.
/// </summary>
[ResourceEntity("dlc/dlc_goods.json")]
public class DlcGoodsExcel : ExcelResource
{
    [JsonProperty("ShopId")] public uint ShopId { get; set; }
    [JsonProperty("GoodsId")] public uint GoodsId { get; set; }
    [JsonProperty("PosIndex")] public uint PosIndex { get; set; }
    [JsonProperty("Class")] public string? Class { get; set; }

    [JsonExtensionData] public IDictionary<string, JToken> ExtraData { get; set; } =
        new Dictionary<string, JToken>();

    public override uint GetId() => GoodsId;

    public override void Loaded()
    {
        GameData.DlcGoodsData[GoodsId] = this;

        if (!GameData.DlcGoodsByShop.TryGetValue(ShopId, out var list))
        {
            list = [];
            GameData.DlcGoodsByShop[ShopId] = list;
        }

        list.Add(this);
    }
}
