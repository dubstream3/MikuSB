using Newtonsoft.Json;

namespace MikuSB.Data.Excel;

/// <summary>
/// Mission type table (dlc/vircapture/build/vircapture_task_type.json).
/// <c>Type</c> selects the client-side progress comparator for the mission type.
/// </summary>
[ResourceEntity("dlc/vircapture/build/vircapture_task_type.json")]
public class VirCaptureTaskTypeExcel : ExcelResource
{
    [JsonProperty("Id")] public uint Id { get; set; }
    [JsonProperty("Type")] public uint Type { get; set; }

    public override uint GetId() => Id;

    public override void Loaded()
    {
        GameData.VirCaptureTaskTypeData[Id] = this;
    }
}
