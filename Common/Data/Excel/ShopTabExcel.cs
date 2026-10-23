using Newtonsoft.Json;

namespace MikuSB.Data.Excel;

/// <summary>
/// Main shop tab (shop/shop_tab.json, converted from the client's
/// Game/Content/Settings/shop/shop_tab.txt by scripts/parse_shop_csv.py).
///
/// The client resolves the whole tab - icons, condition, refresh rule, version - from its own copy
/// of the table (Shop.lua:79 LoadShopTabConf), so the server only needs the fields that drive the
/// goods list and the refresh bookkeeping: the tab id, the new-version markers the red dot compares
/// against (Shop.lua:254 IsShowRedDot) and the loaded goods.
/// </summary>
[ResourceEntity("shop/shop_tab.json")]
public class ShopTabExcel : ExcelResource
{
    [JsonProperty("ID")] public uint Id { get; set; }
    [JsonProperty("GroupId")] public uint GroupId { get; set; }
    [JsonProperty("GroupName")] public string? GroupName { get; set; }
    [JsonProperty("Name")] public string? Name { get; set; }
    [JsonProperty("Sort")] public uint Sort { get; set; }
    [JsonProperty("OnOff")] public uint OnOff { get; set; }
    [JsonProperty("WidgetType")] public uint WidgetType { get; set; } = 1;
    [JsonProperty("Columns")] public uint Columns { get; set; }
    [JsonProperty("GotoUI")] public string? GotoUi { get; set; }
    [JsonProperty("Condition")] public string? Condition { get; set; }
    [JsonProperty("RefreshRule")] public uint RefreshRule { get; set; }
    [JsonProperty("RefreshTime")] public string? RefreshTime { get; set; }
    [JsonProperty("Activity")] public string? Activity { get; set; }
    [JsonProperty("RefreshFee")] public string? RefreshFee { get; set; }
    [JsonProperty("RefreshLimits")] public uint RefreshLimits { get; set; }
    [JsonProperty("MoneyType")] public uint MoneyType { get; set; }
    [JsonProperty("ShopMoneyType")] public string? ShopMoneyType { get; set; }
    [JsonProperty("Version")] public uint Version { get; set; }
    [JsonProperty("ListVersion")] public uint ListVersion { get; set; }
    [JsonProperty("FragmentLimit")] public uint FragmentLimit { get; set; }
    [JsonProperty("CashLimit")] public uint CashLimit { get; set; }
    [JsonProperty("Label")] public uint Label { get; set; }
    [JsonProperty("Begin")] public string? Begin { get; set; }
    [JsonProperty("End")] public string? End { get; set; }

    [JsonProperty("Goods")] public List<ShopGoodsItem> Goods { get; set; } = [];

    public override uint GetId() => Id;

    public override void Loaded()
    {
        GameData.ShopTabData[Id] = this;

        foreach (var goods in Goods)
            GameData.ShopGoodsData[goods.Id] = goods;
    }
}

/// <summary>
/// One goods row nested inside its tab. Prices keep the raw config shape because the client reads
/// them positionally (Shop.lua:608 GetBuyPrice + :726):
///   {moneyType, cost}                  - a currency id paid through Cash.GetMoneyCount
///   {Genre, Detail, Particular, Level, cost} - an inventory item; cost is the last element
/// </summary>
public class ShopGoodsItem
{
    [JsonProperty("ID")] public uint Id { get; set; }
    [JsonProperty("ShopId")] public uint ShopId { get; set; }
    [JsonProperty("PosIndex")] public uint PosIndex { get; set; }
    [JsonProperty("GDPLN")] [JsonConverter(typeof(JsonCsvUintArrayConverter))] public uint[]? Gdpln { get; set; }
    [JsonProperty("Calculation")] public uint Calculation { get; set; }
    [JsonProperty("Price1")] [JsonConverter(typeof(JsonCsvUintArrayConverter))] public uint[]? Price1 { get; set; }
    [JsonProperty("Price2")] [JsonConverter(typeof(JsonCsvUintArrayConverter))] public uint[]? Price2 { get; set; }
    [JsonProperty("LimitType")] public uint LimitType { get; set; }
    [JsonProperty("LimitNum")] public long LimitNum { get; set; } = -1;
    [JsonProperty("Condition")] public string? Condition { get; set; }
    [JsonProperty("DiscountType")] public uint DiscountType { get; set; }
    [JsonProperty("Discount")] [JsonConverter(typeof(JsonCsvUintArrayConverter))] public uint[]? Discount { get; set; }
    [JsonProperty("Begin")] public string? Begin { get; set; }
    [JsonProperty("End")] public string? End { get; set; }
}
