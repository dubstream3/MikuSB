using Newtonsoft.Json;

namespace MikuSB.Data.Excel;

/// <summary>
/// Staff recruitment offer (dlc/vircapture/build/recruitment_staff.json).
/// Rows with <c>StaffID == 0</c> are placeholders and must not be offered.
/// </summary>
[ResourceEntity("dlc/vircapture/build/recruitment_staff.json")]
public class VirCaptureRecruitExcel : ExcelResource
{
    [JsonProperty("Mapid")] public uint MapId { get; set; }
    [JsonProperty("StaffID")] public uint StaffId { get; set; }
    [JsonProperty("Money")] public uint Money { get; set; }
    [JsonProperty("Finishtaskid")] public string FinishTaskId { get; set; } = string.Empty;

    public override uint GetId() => StaffId;

    public override void Loaded()
    {
        if (StaffId == 0)
            return;
        GameData.VirCaptureRecruitData[StaffId] = this;
    }
}
