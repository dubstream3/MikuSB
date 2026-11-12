using Newtonsoft.Json;

namespace MikuSB.Data.Excel;

[ResourceEntity("dlc/vircapture/build/operating_data.json")]
public class VirCaptureOperatingDataExcel : ExcelResource
{
    [JsonProperty("Mapid")] public uint MapId { get; set; }
    [JsonProperty("Cashid")] public uint CashId { get; set; }
    [JsonProperty("EarningsBonus")] public uint EarningsBonus { get; set; }
    [JsonProperty("TaskID")] public List<uint> TaskId { get; set; } = [];
    [JsonProperty("Max")] public uint Max { get; set; }
    [JsonProperty("StaffProgressbar")] public List<uint> StaffProgressbar { get; set; } = [];
    [JsonProperty("BuildingProgressbar")] public List<uint> BuildingProgressbar { get; set; } = [];

    public override uint GetId() => MapId;

    public override void Loaded()
    {
        GameData.VirCaptureOperatingData[MapId] = this;
    }
}
