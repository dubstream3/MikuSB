using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// DLC mission group windows (dlc/dlc_mission_time.json). Every shipped window has long expired,
/// so the server rewrites them at login through the GM script channel.
/// </summary>
[ResourceEntity("dlc/dlc_mission_time.json")]
public class DlcMissionTimeExcel : ExcelResource
{
    [JsonProperty("GroupID")] public uint GroupId { get; set; }
    [JsonProperty("StartTime")] public JToken? StartTime { get; set; }
    [JsonProperty("EndTime")] public JToken? EndTime { get; set; }

    public override uint GetId() => GroupId;

    public override void Loaded()
    {
        GameData.DlcMissionTimeData[GroupId] = this;
    }
}
