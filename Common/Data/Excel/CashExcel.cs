using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// In-game currency catalogue (cash/cash.json). Cash.GetMoneyCount on the client reads
/// (AttrIds.Currency.GroupId, moneyType * 2 + 1) - see Script/Cash/Cash.lua:55-63 - so the "ID"
/// column here is the moneyType the rest of the server keys its attributes with. The Name column
/// holds a localization key (supplies.NumansToken, ui.silver, ...) rather than a literal; the
/// fallback table in CurrencyService maps the ones the GM console and the shop errors surface.
/// </summary>
[ResourceEntity("cash/cash.json")]
public class CashExcel : ExcelResource
{
    [JsonProperty("ID")] public uint Id { get; set; }
    [JsonProperty("Type")] public string? Type { get; set; }
    [JsonProperty("Name")] public string? Name { get; set; }
    [JsonProperty("Desc")] public string? Desc { get; set; }
    [JsonProperty("GDPL")] public JToken? GdplRaw { get; set; }
    [JsonProperty("Icon")] public uint Icon { get; set; }

    public override uint GetId() => Id;

    public override void Loaded()
    {
        GameData.CashData[Id] = this;
    }
}
