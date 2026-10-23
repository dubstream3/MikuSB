using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// DLC mission board rows (dlc/dlc_mission.json). The client owns the list and the progress bars;
/// the server only needs the reward rows (to answer AchievementDLC_GetReward) and the trigger type
/// (to advance progress on Achievement_OnAddSomeOne).
///
/// Every numeric field is read through JToken: the shipped table mixes numbers with strings, and one
/// row carries "SeasonBossLogic.CheckOpen(true)" in GroupID. A hard uint property makes Newtonsoft
/// throw there and ResourceManager then abandons the rest of the file (1545 rows collapse to 677).
/// </summary>
[ResourceEntity("dlc/dlc_mission.json")]
public class DlcMissionExcel : ExcelResource
{
    [JsonProperty("GroupID")] public JToken? GroupIdRaw { get; set; }
    [JsonProperty("MissionID")] public JToken? MissionIdRaw { get; set; }
    [JsonProperty("Type")] public JToken? TypeRaw { get; set; }
    [JsonProperty("CheckValue")] public JToken? CheckValueRaw { get; set; }
    [JsonProperty("RefreshRule")] public JToken? RefreshRule { get; set; }
    [JsonProperty("Priority")] public JToken? Priority { get; set; }
    [JsonProperty("PreID")] public JToken? PreId { get; set; }
    [JsonProperty("Param")] public JToken? Param { get; set; }
    [JsonProperty("Rewards")] public JToken? Rewards { get; set; }
    [JsonProperty("StartTime")] public JToken? StartTime { get; set; }
    [JsonProperty("EndTime")] public JToken? EndTime { get; set; }

    [JsonIgnore] public uint GroupId => ToUint(GroupIdRaw);
    [JsonIgnore] public uint MissionId => ToUint(MissionIdRaw);
    [JsonIgnore] public uint Type => ToUint(TypeRaw);

    /// <summary>Progress target; the shipped table leaves it blank on rows that are always one-shot.</summary>
    [JsonIgnore]
    public uint CheckValue
    {
        get
        {
            var value = ToUint(CheckValueRaw);
            return value == 0 ? 1u : value;
        }
    }

    /// <summary>
    /// 0 = one-shot (always listed), 1 = daily, 2 = weekly. The client only lists a row whose refresh
    /// rule is 0 or whose mission id sits in the matching activation slot, which is why the server has
    /// to fill those slots on login (see DlcMissionActiveService).
    /// </summary>
    [JsonIgnore] public uint RefreshRuleValue => ToUint(RefreshRule);

    public override uint GetId() => MissionId;

    public override void Loaded()
    {
        // The malformed row above resolves to id 0 and must not become a mission.
        if (MissionId == 0)
            return;

        GameData.DlcMissionData[MissionId] = this;
        if (!GameData.DlcMissionsByType.TryGetValue(Type, out var list))
        {
            list = [];
            GameData.DlcMissionsByType[Type] = list;
        }

        list.Add(this);
    }

    private static uint ToUint(JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null)
            return 0;

        var raw = token.ToString().Trim().Trim('[', ']', '"');
        return uint.TryParse(raw, out var value) ? value : 0;
    }
}
