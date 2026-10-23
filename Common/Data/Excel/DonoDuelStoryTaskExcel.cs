using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// Donoduel mission rows from dlc/DonoDuel/donoduel_story_task.json. DonoDuelTask:LoadMissionConfig
/// (DLC/DonoDuel/DonoDuelTask.lua:39-79) keeps only ID >= 100 and injects nGroupId = 186
/// (DonoDuelTask.GroupId) because the shipped table carries no GroupId column. The per mission state
/// lives in attribute (186, ID) as bit0 = accepted, bit1 = reward taken and bits2..31 = progress
/// (DonoDuelTask.lua:182-222).
/// </summary>
[ResourceEntity("dlc/DonoDuel/donoduel_story_task.json")]
public class DonoDuelStoryTaskExcel : ExcelResource
{
    [JsonProperty("ID")] public JToken? Id { get; set; }
    [JsonProperty("Type")] public JToken? Type { get; set; }
    [JsonProperty("PreID")] public JToken? PreId { get; set; }
    [JsonProperty("AcceptAuto")] public JToken? AcceptAuto { get; set; }
    [JsonProperty("SubmitAuto")] public JToken? SubmitAuto { get; set; }
    [JsonProperty("CheckValue")] public JToken? CheckValue { get; set; }
    [JsonProperty("Objectives")] public JToken? Objectives { get; set; }
    [JsonProperty("Param")] public JToken? Param { get; set; }
    [JsonProperty("Reward1")] public JToken? Reward1 { get; set; }
    [JsonProperty("Reward2")] public JToken? Reward2 { get; set; }
    [JsonProperty("Reward3")] public JToken? Reward3 { get; set; }

    public override uint GetId() => 0;

    public override void Loaded()
    {
        // Local tolerant read: the shipped tables mix numbers and strings ("101", "[101]", "").
        var raw = Id?.ToString().Trim().Trim('[', ']', '"') ?? string.Empty;
        if (uint.TryParse(raw, out var id) && id >= 100)
            GameData.DonoDuelStoryTaskData[id] = this;
    }
}
