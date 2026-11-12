using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// Blueprint island mission table (dlc/vircapture/build/vircapture_task.json).
/// The mission progress lives in the <see cref="AttrIds.VirCapture.TaskGroupId"/> attribute
/// group: sid = mission id, bit0 = reward received, bits 1..31 = accumulated progress.
/// </summary>
[ResourceEntity("dlc/vircapture/build/vircapture_task.json")]
public class VirCaptureTaskExcel : ExcelResource
{
    [JsonProperty("ID")] public uint Id { get; set; }
    [JsonProperty("TeamID")] public uint TeamId { get; set; }
    [JsonProperty("BigType")] public uint BigType { get; set; }
    [JsonProperty("Type")] public uint Type { get; set; }
    [JsonProperty("CheckValue")] public uint CheckValue { get; set; }
    [JsonProperty("WindowType")] public JToken? WindowType { get; set; }
    [JsonProperty("QuestWindow")] public JToken? QuestWindow { get; set; }
    /// <summary>Type specific match parameters, e.g. [326,512].</summary>
    [JsonProperty("Param")] public JToken? Param { get; set; }
    /// <summary>
    /// Either a list of 6-element reward rows ([kind, itemType, detail, particular, level, count])
    /// or an object keyed by reward kind ({"4":121} = free worker 121).
    /// </summary>
    [JsonProperty("Rewards")] public JToken? Rewards { get; set; }
    /// <summary>Mission ids that must be received before this one unlocks.</summary>
    [JsonProperty("PreID")] public JToken? PreId { get; set; }
    [JsonProperty("Condition")] public JToken? Condition { get; set; }
    [JsonProperty("BuildID")] public JToken? BuildId { get; set; }
    [JsonProperty("StaffID")] public JToken? StaffId { get; set; }

    public override uint GetId() => Id;

    public override void Loaded()
    {
        GameData.VirCaptureTaskData[Id] = this;
    }
}
