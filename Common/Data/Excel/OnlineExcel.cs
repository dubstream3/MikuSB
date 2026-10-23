using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// Online (co-op / 联机) activity rows from online/online.json. 海姆优化作战 (Rush) is not a row of its
/// own: dlc_activities maps Rush to a *group* id (105 for DLC35) and the client resolves that group
/// through Online.tbShowGroup, which is built from the Group column. Both rows of the group therefore
/// need their own time override, because Online_GMChangeTime looks rows up by nId.
/// </summary>
[ResourceEntity("online/online.json")]
public class OnlineExcel : ExcelResource
{
    [JsonProperty("Id")] public uint Id { get; set; }

    /// <summary>Primary key of the group this row belongs to; DLC Rush ids point at this, not at Id.</summary>
    [JsonProperty("Group")] public JToken? Group { get; set; }

    [JsonProperty("Type")] public JToken? Type { get; set; }

    /// <summary>
    /// Candidate map ids of the activity; the room bootstrap picks the level to travel to from here.
    /// Three shapes occur: [[9633]], [[9602, 9605, ...]] and {"9634": 9626}.
    /// </summary>
    [JsonProperty("ResetLevel")] public JToken? ResetLevel { get; set; }

    [JsonProperty("MaxPlayer")] public JToken? MaxPlayer { get; set; }
    [JsonProperty("StartTime")] public JToken? StartTime { get; set; }
    [JsonProperty("EndTime")] public JToken? EndTime { get; set; }
    [JsonProperty("Condition")] public JToken? Condition { get; set; }

    public override uint GetId() => Id;

    public override void Loaded()
    {
        GameData.OnlineData[Id] = this;
    }
}
