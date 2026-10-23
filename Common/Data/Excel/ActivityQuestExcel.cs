using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// achievement/activity_quest.json — the activity achievement/quest table the client loads as
/// Achievement.LoadQuestConf (Achievement/Achievement.lua:190, `LoadCsv('achievement/activity_quest.txt')`).
///
/// Rows are keyed by ID and grouped into the "quest" attribute group
/// (Achievement.GroupID_Quest = 7, Achievement.lua:16). Per-row state lives in attribute (7, ID)
/// packed exactly like the DLC mission board: bit 0 = reward claimed, bits 1..31 = progress
/// (Achievement.lua:743 GetProgres = GetBits(attr, 1, 31), :783 IsReceive = GetBits(attr, 0, 0),
/// :670 IsFinished = GetProgres(...) >= nCheckValue).
///
/// 弑神斗场 uses the twenty rows 19001..19020 (Type 2 for the first four levels of a zone, Type 257
/// for the zone's fifth/endless level). Each seasonleveldes row names its row through ReferAward,
/// so the level -> quest mapping is table driven and lives in SeasonBossLevelExcel.
///
/// Rewards is the nested [ [G,D,P,L,Count], ... ] shape, so DlcRuntimeOverrides.ReadRewardRows
/// applies directly.
/// </summary>
[ResourceEntity("achievement/activity_quest.json")]
public class ActivityQuestExcel : ExcelResource
{
    [JsonProperty("ID")] public uint Id { get; set; }
    [JsonProperty("Name")] public JToken? Name { get; set; }
    [JsonProperty("Describe")] public JToken? Describe { get; set; }
    [JsonProperty("Type")] public JToken? TypeRaw { get; set; }
    [JsonProperty("CheckValue")] public JToken? CheckValueRaw { get; set; }
    [JsonProperty("Param")] public JToken? Param { get; set; }
    [JsonProperty("Rewards")] public JToken? Rewards { get; set; }
    [JsonProperty("PreID")] public JToken? PreIdRaw { get; set; }
    [JsonProperty("Group")] public JToken? GroupRaw { get; set; }
    [JsonProperty("ChapterGroup")] public JToken? ChapterGroupRaw { get; set; }
    [JsonProperty("FinishLimit")] public JToken? FinishLimitRaw { get; set; }
    [JsonProperty("ActivityID")] public JToken? ActivityId { get; set; }
    [JsonProperty("StartTime")] public JToken? StartTime { get; set; }
    [JsonProperty("EndTime")] public JToken? EndTime { get; set; }

    [JsonIgnore] public uint Type => ReadUint(TypeRaw);

    /// <summary>Progress needed before the row can be claimed; 1 for every season boss row.</summary>
    [JsonIgnore] public uint CheckValue => ReadUint(CheckValueRaw);

    [JsonIgnore] public uint PreId => ReadUint(PreIdRaw);
    [JsonIgnore] public uint Group => ReadUint(GroupRaw);
    [JsonIgnore] public uint ChapterGroup => ReadUint(ChapterGroupRaw);
    [JsonIgnore] public uint FinishLimit => ReadUint(FinishLimitRaw);

    public override uint GetId() => Id;

    public override void Loaded()
    {
        if (Id != 0)
            GameData.ActivityQuestData[Id] = this;
    }

    private static uint ReadUint(JToken? token)
    {
        var raw = token?.ToString().Trim().Trim('[', ']', '"') ?? string.Empty;
        return uint.TryParse(raw, out var value) ? value : 0;
    }
}
