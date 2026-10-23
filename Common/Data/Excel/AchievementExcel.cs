using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// achievement/achievement.json — the standard achievement / task table the client loads as
/// Achievement.LoadConf (Achievement/Achievement.lua:79 `LoadCsv('achievement/achievement.txt')`,
/// looked up by Achievement.GetConfig(nId) at :264).
///
/// Rows are keyed by ID in the attribute group AttrIds.Achievement.GroupId = 2, packed exactly like
/// the activity quest rows: bit 0 = reward claimed, bits 1..31 = progress
/// (Achievement.lua:743 GetProgres = GetBits(attr, 1, 31), :783 IsReceive = GetBits(attr, 0, 0),
/// :670 IsFinished = GetProgres >= nCheckValue).
///
/// Claiming goes through Achievement_GetReward / Achievement_QuickGetReward
/// (Achievement.lua:846 / :913); Achievement.GetExtraReward (:793, the per-chapter bonus row whose
/// Group is GROUP_ExtraReward = 5) reuses the same api, so no special casing is needed here.
///
/// Rewards is the nested [ [G, D, P, L, Count], ... ] shape and the table mixes empty strings into
/// almost every numeric column (`"Priority": ""`, `"PreID": ""`, ...), so every numeric column is read
/// through a tolerant accessor — a non-tolerant binding would make ResourceManager swallow the whole
/// file (see docs/contracts/README.md §6).
/// </summary>
[ResourceEntity("achievement/achievement.json")]
public class AchievementExcel : ExcelResource
{
    [JsonProperty("ID")] public uint Id { get; set; }
    [JsonProperty("Name")] public JToken? Name { get; set; }
    [JsonProperty("Describe")] public JToken? Describe { get; set; }
    [JsonProperty("Group")] public JToken? GroupRaw { get; set; }
    [JsonProperty("ChapterGroup")] public JToken? ChapterGroupRaw { get; set; }
    [JsonProperty("Priority")] public JToken? PriorityRaw { get; set; }
    [JsonProperty("Condition")] public JToken? Condition { get; set; }
    [JsonProperty("PreID")] public JToken? PreIdRaw { get; set; }
    [JsonProperty("ReceivedShow")] public JToken? ReceivedShowRaw { get; set; }
    [JsonProperty("Type")] public JToken? TypeRaw { get; set; }
    [JsonProperty("CheckValue")] public JToken? CheckValueRaw { get; set; }
    [JsonProperty("Param")] public JToken? Param { get; set; }
    [JsonProperty("FinishLimit")] public JToken? FinishLimitRaw { get; set; }
    [JsonProperty("Rewards")] public JToken? Rewards { get; set; }
    [JsonProperty("FunctionID")] public JToken? FunctionIdRaw { get; set; }
    [JsonProperty("GotoUI")] public JToken? GotoUi { get; set; }
    [JsonProperty("AchievementPoint")] public JToken? AchievementPointRaw { get; set; }

    /// <summary>Achievement.GROUP_* (Achievement.lua:19-23): 1 daily, 2 weekly, 4 branch line, 5 extra.</summary>
    [JsonIgnore] public uint Group => ReadUint(GroupRaw);

    [JsonIgnore] public uint ChapterGroup => ReadUint(ChapterGroupRaw);
    [JsonIgnore] public uint Priority => ReadUint(PriorityRaw);
    [JsonIgnore] public uint PreId => ReadUint(PreIdRaw);
    [JsonIgnore] public uint ReceivedShow => ReadUint(ReceivedShowRaw);

    /// <summary>The trigger type the client reports through Achievement_OnAddSomeOne.</summary>
    [JsonIgnore] public uint Type => ReadUint(TypeRaw);

    /// <summary>Progress needed before the row can be claimed (the client defaults an empty column to 1).</summary>
    [JsonIgnore] public uint CheckValue
    {
        get
        {
            var value = ReadUint(CheckValueRaw);
            return value == 0 ? 1u : value;
        }
    }

    [JsonIgnore] public uint FinishLimit => ReadUint(FinishLimitRaw);
    [JsonIgnore] public uint FunctionId => ReadUint(FunctionIdRaw);
    [JsonIgnore] public uint AchievementPoint => ReadUint(AchievementPointRaw);

    public override uint GetId() => Id;

    public override void Loaded()
    {
        if (Id != 0)
            GameData.AchievementData[Id] = this;
    }

    private static uint ReadUint(JToken? token)
    {
        var raw = token?.ToString().Trim().Trim('[', ']', '"') ?? string.Empty;
        return uint.TryParse(raw, out var value) ? value : 0;
    }
}
