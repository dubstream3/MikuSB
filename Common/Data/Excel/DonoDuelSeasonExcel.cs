using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// 多诺-巅峰对决 (Donoduel) season rows from dlc/DonoDuel/donoduel_season.json. The gate
/// DlcFuncDonoduel.OnGetTimeInfo -> DonoDuelConfig:GetSeasonConfig(cfg) reads OpenTime/CloseTime out
/// of DonoDuelConfig.tbSeasonList, which the client builds from this table, and
/// DonoDuel:GetActId() (attribute gid 185, sid 1) has to name a row that exists here or the lookup
/// returns nil and the entry stays at FuncState.Over.
/// </summary>
[ResourceEntity("dlc/DonoDuel/donoduel_season.json")]
public class DonoDuelSeasonExcel : ExcelResource
{
    [JsonProperty("SeasonID")] public JToken? SeasonID { get; set; }
    [JsonProperty("Condition")] public JToken? Condition { get; set; }
    [JsonProperty("OpenTime")] public JToken? OpenTime { get; set; }
    [JsonProperty("CloseTime")] public JToken? CloseTime { get; set; }
    [JsonProperty("Name")] public string? Name { get; set; }
    [JsonProperty("EnableFunction")] public JToken? EnableFunction { get; set; }
    [JsonProperty("mapID")] public JToken? MapId { get; set; }
    [JsonProperty("Currency")] public JToken? Currency { get; set; }

    public override uint GetId() => 0;

    public override void Loaded()
    {
        // Local tolerant read: the shipped tables mix numbers and strings ("1", "[1]", ""), and
        // JToken.ToObject<uint>() throws a FormatException on the empty string.
        var raw = SeasonID?.ToString().Trim().Trim('[', ']', '"') ?? string.Empty;
        if (uint.TryParse(raw, out var seasonId) && seasonId != 0)
            GameData.DonoDuelSeasonData[seasonId] = this;
    }
}
