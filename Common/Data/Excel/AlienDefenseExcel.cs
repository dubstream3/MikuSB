using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// 异星守护 (alien defense / Mow) act rows from dlc/AlienDefense/alienlist.json. The pinned DLC35
/// row carries Mow=4 in DlcFuncConfId, and act 4 owns levels 6231..6236 (normal), 6719..6723
/// (challenge) and 6910..6915 (endless) plus the 31 NormalMission quest ids.
///
/// LevelList is three positional groups (index 0 = normal, 1 = challenge, 2 = endless), which is
/// what AlienDefense.GetLevelType reads back (AlienDefense.lua:487-498: the group's position in
/// tbConf.tbLevels IS the level type). NormalMission is a flat allowlist, NOT positionally paired
/// with levels: the per-level quest is found through activity_quest rows whose Type == 2 and whose
/// Param names the level (e.g. 20001 -> [6231]), see AlienDefenseQuestService.
/// </summary>
[ResourceEntity("dlc/AlienDefense/alienlist.json")]
public class AlienDefenseExcel : ExcelResource
{
    [JsonProperty("Id")] public uint Id { get; set; }
    [JsonProperty("StartTime")] public JToken? StartTime { get; set; }
    [JsonProperty("EndTime")] public JToken? EndTime { get; set; }
    [JsonProperty("DailyMission")] public JToken? DailyMissionRaw { get; set; }
    [JsonProperty("WeeklyMission")] public JToken? WeeklyMissionRaw { get; set; }
    [JsonProperty("NormalMission")] public JToken? NormalMissionRaw { get; set; }
    [JsonProperty("Condition")] public JToken? Condition { get; set; }
    [JsonProperty("LevelList")] public JToken? LevelListRaw { get; set; }

    [JsonIgnore] public IReadOnlyList<IReadOnlyList<uint>> LevelGroups { get; private set; } = [];
    [JsonIgnore] public IReadOnlyList<uint> NormalMissions { get; private set; } = [];

    public override uint GetId() => Id;

    public override void Loaded()
    {
        LevelGroups = ReadGroups(LevelListRaw);
        NormalMissions = ReadList(NormalMissionRaw);
        if (Id != 0)
            GameData.AlienDefenseData[Id] = this;
    }

    /// <summary>
    /// Level type (1 normal / 2 challenge / 3 endless) of <paramref name="levelId"/> in this act,
    /// or 0 when the level does not belong to it. Mirrors AlienDefense.GetLevelType.
    /// </summary>
    public uint FindLevelType(uint levelId)
    {
        for (var i = 0; i < LevelGroups.Count; i++)
        {
            if (LevelGroups[i].Contains(levelId))
                return (uint)(i + 1);
        }

        return 0;
    }

    /// <summary>
    /// Flattened position of <paramref name="levelId"/> (groups in order), or 0 when unknown.
    /// Mirrors AlienDefenseConfig.GetLevelIdx, which flattens tbLevels the same way.
    /// </summary>
    public uint GetLevelIdx(uint levelId)
    {
        var idx = 0u;
        foreach (var group in LevelGroups)
        {
            foreach (var id in group)
            {
                idx++;
                if (id == levelId)
                    return idx;
            }
        }

        return 0;
    }

    /// <summary>Act row owning <paramref name="levelId"/>, optionally constrained to an act.</summary>
    public static AlienDefenseExcel? FindByLevel(uint levelId, uint actId)
    {
        foreach (var row in GameData.AlienDefenseData.Values)
        {
            if (actId != 0 && row.Id != actId)
                continue;

            if (row.FindLevelType(levelId) != 0)
                return row;
        }

        return null;
    }

    private static IReadOnlyList<IReadOnlyList<uint>> ReadGroups(JToken? token)
    {
        var groups = new List<IReadOnlyList<uint>>();
        if (token is not JArray outer)
            return groups;

        foreach (var entry in outer)
            groups.Add(ReadList(entry));

        return groups;
    }

    private static IReadOnlyList<uint> ReadList(JToken? token)
    {
        var values = new List<uint>();
        if (token is not JArray array)
            return values;

        foreach (var entry in array)
        {
            var raw = entry?.ToString().Trim().Trim('[', ']', '"') ?? string.Empty;
            if (uint.TryParse(raw, out var value))
                values.Add(value);
        }

        return values;
    }
}
