using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// activity/activities.json — activity master table (455 rows on the CN client build).
/// The client also ships its own copy (activity/activities.txt); this server side table is used to
/// answer Activity_Refresh by pushing an open window back through the ActivityQuest_Open callback.
///
/// Field typing is deliberately tolerant: the rows mix integers and strings for the same key
/// (GroupIcon/TitleIcon/ImgPic/ModeId/Tab/Index/Hide/...), Condition and CustomData/GotoUI are
/// sometimes arrays or objects, and ResourceManager aborts the whole file on the first record it
/// cannot deserialize. Only Id/Class/StartTime/EndTime/CloseFlag are guaranteed to be a
/// uint/string/string/string/string on every row.
/// </summary>
[ResourceEntity("activity/activities.json")]
public class ActivityExcel : ExcelResource
{
    [JsonProperty("Id")] public uint Id { get; set; }
    [JsonProperty("Class")] public string Class { get; set; } = "";
    [JsonProperty("StartTime")] public string StartTime { get; set; } = "";
    [JsonProperty("EndTime")] public string EndTime { get; set; } = "";
    [JsonProperty("CloseFlag")] public string CloseFlag { get; set; } = "";
    [JsonProperty("GroupId")] public JToken? GroupId { get; set; }
    [JsonProperty("Index")] public JToken? Index { get; set; }
    [JsonProperty("Tab")] public JToken? Tab { get; set; }
    [JsonProperty("Hide")] public JToken? Hide { get; set; }
    [JsonProperty("ModeId")] public JToken? ModeId { get; set; }
    [JsonProperty("Condition")] public JToken? Condition { get; set; }
    [JsonProperty("CustomData")] public JToken? CustomData { get; set; }
    [JsonProperty("GotoUI")] public JToken? GotoUI { get; set; }

    public override uint GetId() => Id;

    public override void Loaded()
    {
        GameData.ActivityData[Id] = this;
    }
}
