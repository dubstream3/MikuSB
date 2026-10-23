using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

// 异绪消解 (GirlRush, DLC27 "DlcFuncGirlrush" -> FunctionType.GirlRush) keeps three tables under
// girlrush/: the season row (girlrushlist), the nine stage rows (girlrushlevel) and the season mission
// board (girlrushaward). The client loads the same files through GirlRushConfig.LoadGirlRush*Cfg
// (GirlRush/GirlRushConfig.lua:132-231); the server only needs them to gate entry, to persist the
// per level bitfield the client treats as its save file (GirlRush.GID = 158) and to pay the board out.
[ResourceEntity("girlrush/girlrushlist.json")]
public class GirlRushSeasonExcel : ExcelResource
{
    [JsonProperty("Id")] public uint Id { get; set; }
    [JsonProperty("StartTime")] public string? StartRaw { get; set; }
    [JsonProperty("EndTime")] public string? EndRaw { get; set; }
    [JsonProperty("LevelList")] public JToken? LevelListRaw { get; set; }
    [JsonProperty("PlotID")] public JToken? PlotIdRaw { get; set; }

    /// <summary>Stage ids of this season, flattened out of the nested LevelList column ([[8656,..]]).</summary>
    [JsonIgnore]
    public List<uint> LevelIds
    {
        get
        {
            var ids = new List<uint>();
            if (LevelListRaw is not JArray outer)
                return ids;

            foreach (var entry in outer)
            {
                if (entry is JArray inner)
                {
                    foreach (var cell in inner)
                        ids.Add(ReadUInt(cell));
                }
                else
                {
                    ids.Add(ReadUInt(entry));
                }
            }

            ids.RemoveAll(id => id == 0);
            return ids;
        }
    }

    [JsonIgnore] public uint PlotId => ReadUInt(PlotIdRaw);

    public override uint GetId() => Id;

    public override void Loaded() => GameData.GirlRushSeasonData[Id] = this;

    private static uint ReadUInt(JToken? token) => token?.Type switch
    {
        JTokenType.Integer => (uint)token.Value<long>(),
        JTokenType.Float => (uint)token.Value<double>(),
        JTokenType.String when uint.TryParse(token.Value<string>()?.Trim(), out var value) => value,
        _ => 0
    };
}

// girlrushlevel.json: one row per stage. Condition is a dict ({"2": 8656}) meaning "clear stage
// 8656 first"; stage 8656 itself has an empty Condition. LevelType 1/2/3 picks the verdict rule
// (GirlRush.lua:94-113): 1 counts remaining time (higher is better), 2 counts remaining defence HP,
// 3 counts cleared rounds - all of them map to a rank 1..4 that the client shows as Ace/S/A/B.
[ResourceEntity("girlrush/girlrushlevel.json")]
public class GirlRushLevelExcel : ExcelResource
{
    [JsonProperty("ID")] public uint Id { get; set; }
    [JsonProperty("LevelType")] public JToken? LevelTypeRaw { get; set; }
    [JsonProperty("MapID")] public JToken? MapIdRaw { get; set; }
    [JsonProperty("Condition")] public JToken? ConditionRaw { get; set; }
    [JsonProperty("StartTime")] public string? StartRaw { get; set; }
    [JsonProperty("EndTime")] public string? EndRaw { get; set; }

    [JsonIgnore] public uint LevelType => ReadUInt(LevelTypeRaw);
    [JsonIgnore] public uint MapId => ReadUInt(MapIdRaw);

    /// <summary>Stage that must be cleared before this one opens; 0 when the row has no condition.</summary>
    [JsonIgnore]
    public uint RequiredLevelId
    {
        get
        {
            if (ConditionRaw is not JObject obj)
                return 0;

            foreach (var pair in obj)
            {
                if (pair.Key == "2")
                    return ReadUInt(pair.Value);
            }

            return 0;
        }
    }

    public override uint GetId() => Id;

    public override void Loaded() => GameData.GirlRushLevelData[Id] = this;

    private static uint ReadUInt(JToken? token) => token?.Type switch
    {
        JTokenType.Integer => (uint)token.Value<long>(),
        JTokenType.Float => (uint)token.Value<double>(),
        JTokenType.String when uint.TryParse(token.Value<string>()?.Trim(), out var value) => value,
        _ => 0
    };
}

// girlrushaward.json: the season mission board. Condition rows are [1, levelId, n] = "clear that stage
// n times" and [2, levelId, rank] = "reach rank <= rank on that stage" (rank 1 is the best).
// Claim flags live in GirlRush.GID_AWARD = 160 keyed by Index, see GirlRush.GetAwardState.
[ResourceEntity("girlrush/girlrushaward.json")]
public class GirlRushAwardExcel : ExcelResource
{
    [JsonProperty("SeasonID")] public JToken? SeasonIdRaw { get; set; }
    [JsonProperty("Index")] public JToken? IndexRaw { get; set; }
    [JsonProperty("Level")] public JToken? LevelRaw { get; set; }
    [JsonProperty("Condition")] public JToken? ConditionRaw { get; set; }
    [JsonProperty("Award")] public JToken? AwardRaw { get; set; }

    [JsonIgnore] public uint SeasonId => ReadUInt(SeasonIdRaw);
    [JsonIgnore] public uint Index => ReadUInt(IndexRaw);
    [JsonIgnore] public uint LevelId => ReadUInt(LevelRaw);

    /// <summary>Reward lines of this mission, already filtered to well formed 5 element rows.</summary>
    [JsonIgnore]
    public List<List<uint>> AwardRows
    {
        get
        {
            var rows = new List<List<uint>>();
            if (AwardRaw is not JArray outer)
                return rows;

            foreach (var entry in outer)
            {
                if (entry is not JArray row || row.Count < 5)
                    continue;

                var values = new List<uint>(row.Count);
                foreach (var cell in row)
                    values.Add(ReadUInt(cell));

                if (values[4] > 0)
                    rows.Add(values);
            }

            return rows;
        }
    }

    /// <summary>Condition triples ([type, levelId, param]); empty means "no gate".</summary>
    [JsonIgnore]
    public List<uint[]> Conditions
    {
        get
        {
            var rows = new List<uint[]>();
            if (ConditionRaw is not JArray outer)
                return rows;

            foreach (var entry in outer)
            {
                if (entry is not JArray row || row.Count < 3)
                    continue;

                rows.Add([ReadUInt(row[0]), ReadUInt(row[1]), ReadUInt(row[2])]);
            }

            return rows;
        }
    }

    public override uint GetId() => SeasonId * 1000 + Index;

    public override void Loaded() => GameData.GirlRushAwardData[GetId()] = this;

    private static uint ReadUInt(JToken? token) => token?.Type switch
    {
        JTokenType.Integer => (uint)token.Value<long>(),
        JTokenType.Float => (uint)token.Value<double>(),
        JTokenType.String when uint.TryParse(token.Value<string>()?.Trim(), out var value) => value,
        _ => 0
    };
}
