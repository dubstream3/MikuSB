using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// 弑神斗场 (season boss) rows from challenge/seasonboss/seasonlist.json. dlc_activities maps Teamboss
/// to an ActID (10 for DLC35) and the rows are per (ActID, TermID, ZoneID), each carrying its own
/// window plus a separate award window. SeasonBoss_GMChangeTime patches the client table by the row's
/// primary Id, so every row of the act has to be sent.
///
/// LevelList and TaskID are positionally paired -- LevelList[i] is the level that finishes TaskID[i]
/// (the client reads them the same way: SeasonBoss.lua:69/:74 store both lists and :1086-1087 walks
/// tbTaskID next to tbLevelList). This is the AUTHORITATIVE level -> quest mapping. The ReferAward
/// column of seasonleveldes.json must NOT be used for it: that column is misaligned for several acts
/// (e.g. for ActID 10 the 3411-3415 block points at 19016-19020 instead of 19006-19010, and
/// 3431-3435 repeats the same ids), so relying on it silently strands quests and credits the wrong
/// block. Only seasonlist's positional pairing matches the localized mission titles.
/// </summary>
[ResourceEntity("challenge/seasonboss/seasonlist.json")]
public class SeasonBossExcel : ExcelResource
{
    [JsonProperty("Id")] public uint Id { get; set; }
    [JsonProperty("ActID")] public JToken? ActID { get; set; }
    [JsonProperty("TermID")] public JToken? TermID { get; set; }
    [JsonProperty("ZoneID")] public JToken? ZoneID { get; set; }
    [JsonProperty("StartTime")] public JToken? StartTime { get; set; }
    [JsonProperty("EndTime")] public JToken? EndTime { get; set; }
    [JsonProperty("AwardTime")] public JToken? AwardTime { get; set; }
    [JsonProperty("Condition")] public JToken? Condition { get; set; }
    [JsonProperty("LevelList")] public JToken? LevelListRaw { get; set; }
    [JsonProperty("TaskID")] public JToken? TaskIdRaw { get; set; }

    [JsonIgnore] public uint ActId { get; private set; }
    [JsonIgnore] public uint TermId { get; private set; }
    [JsonIgnore] public uint ZoneId { get; private set; }
    [JsonIgnore] public IReadOnlyList<uint> LevelList { get; private set; } = [];
    [JsonIgnore] public IReadOnlyList<uint> TaskList { get; private set; } = [];

    public override uint GetId() => Id;

    public override void Loaded()
    {
        ActId = ReadUint(ActID);
        TermId = ReadUint(TermID);
        ZoneId = ReadUint(ZoneID);
        LevelList = ReadList(LevelListRaw);
        TaskList = ReadList(TaskIdRaw);
        GameData.SeasonBossData[Id] = this;
    }

    /// <summary>
    /// Quest finished by clearing <paramref name="levelId"/> in this row, or 0 when the level does not
    /// belong to it.
    /// </summary>
    public uint FindQuestForLevel(uint levelId)
    {
        for (var i = 0; i < LevelList.Count && i < TaskList.Count; i++)
        {
            if (LevelList[i] == levelId)
                return TaskList[i];
        }

        return 0;
    }

    /// <summary>
    /// Finds the row owning a level, optionally constrained to an act. Level ids are unique across the
    /// whole seasonlist, so the act filter is only a guard against a mis-pinned activity.
    /// </summary>
    public static SeasonBossExcel? FindByLevel(uint levelId, uint actId)
    {
        foreach (var row in GameData.SeasonBossData.Values)
        {
            if (actId != 0 && row.ActId != actId)
                continue;

            if (row.FindQuestForLevel(levelId) != 0)
                return row;
        }

        return null;
    }

    private static IReadOnlyList<uint> ReadList(JToken? token)
    {
        if (token is not JArray array)
            return [];

        var values = new List<uint>(array.Count);
        foreach (var entry in array)
            values.Add(ReadUint(entry));

        return values;
    }

    private static uint ReadUint(JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null)
            return 0;

        var text = token.ToString().Trim().Trim('[', ']', '"');
        return uint.TryParse(text, out var value) ? value : 0;
    }
}
