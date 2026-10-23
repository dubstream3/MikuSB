using Newtonsoft.Json;

namespace MikuSB.Data.Excel;

/// <summary>
/// 数据金 -> 体力 exchange ladder (cash/vigor_exchange.json). The client picks the row with
/// <c>tbConfig[me:GetAttribute(AttrIds.CashExchange.GroupId, VigorLimitSid) + 1]</c>
/// (Script/Cash/Exchange.lua:106-124 GetInfo) and treats the last row's Times as the per-account
/// ceiling it shows as "n/N" (Exchange.lua:121 tbLimit = {attr, tbConfig.nMax}).
/// So Times is both the ladder step and the limit; the server re-derives the same row on its own
/// and never lets the cost the client sends decide what is charged.
/// </summary>
[ResourceEntity("cash/vigor_exchange.json")]
public class VigorExchangeExcel : ExcelResource
{
    [JsonProperty("Times")] public uint Times { get; set; }
    [JsonProperty("Cost")] public uint Cost { get; set; }
    [JsonProperty("Vigor")] public uint Vigor { get; set; }

    public override uint GetId() => Times;

    public override void Loaded()
    {
        GameData.VigorExchangeData[Times] = this;
    }
}

/// <summary>
/// 数据金 -> 通用银 exchange ladder (cash/silver_exchange.json). Same shape and same attribute
/// group as <see cref="VigorExchangeExcel"/>, but keyed on
/// <see cref="AttrIds.CashExchange.SilverLimitSid"/>; see Script/Cash/Exchange.lua:130-148.
/// </summary>
[ResourceEntity("cash/silver_exchange.json")]
public class SilverExchangeExcel : ExcelResource
{
    [JsonProperty("Times")] public uint Times { get; set; }
    [JsonProperty("Cost")] public uint Cost { get; set; }
    [JsonProperty("Silver")] public uint Silver { get; set; }

    public override uint GetId() => Times;

    public override void Loaded()
    {
        GameData.SilverExchangeData[Times] = this;
    }
}
