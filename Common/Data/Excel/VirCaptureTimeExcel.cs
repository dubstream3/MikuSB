using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

[ResourceEntity("dlc/vircapture/timelist.json")]
public class VirCaptureTimeExcel : ExcelResource
{
    [JsonProperty("Id")] public uint Id { get; set; }
    [JsonProperty("StartTime")] public string StartTime { get; set; } = "";
    [JsonProperty("EndTime")] public string EndTime { get; set; } = "";
    [JsonProperty("CaptureRegionId")] public List<uint> CaptureRegionId { get; set; } = [];
    [JsonProperty("MaxExp")] public uint MaxExp { get; set; }
    /// <summary>Max monster cards held by the bag in this period.</summary>
    [JsonProperty("BagLimit")] public uint BagLimit { get; set; }
    /// <summary>Per-color capture cap, e.g. {"3":100,"4":50,"5":20}.</summary>
    [JsonProperty("ColorLimit")] public JToken? ColorLimit { get; set; }
    /// <summary>Optional rank-award settle time, e.g. "[202507011000]". Absent in the current table.</summary>
    [JsonProperty("AwardTime")] public string? AwardTime { get; set; }

    public override uint GetId() => Id;

    public override void Loaded()
    {
        GameData.VirCaptureTimeData[Id] = this;
    }
}
