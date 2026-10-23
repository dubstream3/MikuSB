using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MikuSB.Util;

namespace MikuSB.Data.Excel;

// farmwar/farmwarout.json -- one row per FarmWar (蔬菜大战) period. Only two rows exist (id 1 and 2);
// the second one is the DLC34 window (2026-01-08 .. 01-29), which is the one the era pin selects.
[ResourceEntity("farmwar/farmwarout.json")]
public class FarmWarActExcel : ExcelResource
{
    [JsonProperty("ID")] public JToken? IdRaw { get; set; }
    [JsonProperty("StoryID")] public JToken? StoryIdRaw { get; set; }
    [JsonProperty("TaskID")] public JToken? TaskIdsRaw { get; set; }
    [JsonProperty("StartTime")] public JToken? StartRaw { get; set; }
    [JsonProperty("EndTime")] public JToken? EndRaw { get; set; }

    [JsonIgnore] public uint Id => ReadUInt(IdRaw);
    [JsonIgnore] public uint StoryId => ReadUInt(StoryIdRaw);
    [JsonIgnore] public string? Start => StartRaw?.ToString();
    [JsonIgnore] public string? End => EndRaw?.ToString();

    [JsonIgnore] public List<uint> TaskIds => ReadUIntList(TaskIdsRaw);

    public override uint GetId() => Id;

    public override void Loaded() => GameData.FarmWarActData[Id] = this;

    private static uint ReadUInt(JToken? token)
    {
        switch (token)
        {
            case null:
                return 0;
            case JValue value when value.Value is long l:
                return (uint)Math.Max(0, l);
            case JValue value when value.Value is int i:
                return (uint)Math.Max(0, i);
            case JValue value when value.Value is double d:
                return (uint)Math.Max(0, d);
            default:
                return uint.TryParse(token.ToString().Trim(), out var parsed) ? parsed : 0;
        }
    }

    private static List<uint> ReadUIntList(JToken? token)
    {
        var list = new List<uint>();
        if (token is not JArray array)
            return list;

        foreach (var item in array)
        {
            var value = ReadUInt(item);
            if (value != 0)
                list.Add(value);
        }

        return list;
    }
}

// farmwar/farmwarout_level.json -- 33 combat stages. Rewards is the first-clear payload; the star
// style tiers of FunTimed do not exist here (the stage is pass/fail only).
[ResourceEntity("farmwar/farmwarout_level.json")]
public class FarmWarLevelExcel : ExcelResource
{
    [JsonProperty("ID")] public JToken? IdRaw { get; set; }
    [JsonProperty("MapID")] public JToken? MapIdRaw { get; set; }
    [JsonProperty("LevelType")] public JToken? LevelTypeRaw { get; set; }
    [JsonProperty("PreviousLevel")] public JToken? PreviousRaw { get; set; }
    [JsonProperty("StartTime")] public JToken? StartRaw { get; set; }
    [JsonProperty("EndTime")] public JToken? EndRaw { get; set; }
    [JsonProperty("Rewards")] public JToken? RewardsRaw { get; set; }

    [JsonIgnore] public uint Id => ReadUInt(IdRaw);
    [JsonIgnore] public uint MapId => ReadUInt(MapIdRaw);
    [JsonIgnore] public uint LevelType => ReadUInt(LevelTypeRaw);
    [JsonIgnore] public uint PreviousLevel => ReadUInt(PreviousRaw);
    [JsonIgnore] public string? Start => StartRaw?.ToString();
    [JsonIgnore] public string? End => EndRaw?.ToString();

    [JsonIgnore]
    public List<List<uint>> Rewards
    {
        get
        {
            var rows = new List<List<uint>>();
            if (RewardsRaw is not JArray outer)
                return rows;

            foreach (var row in outer)
            {
                if (row is not JArray cells)
                    continue;

                var parsed = new List<uint>();
                foreach (var cell in cells)
                    parsed.Add(ReadUInt(cell));

                if (parsed.Count >= 5 && parsed[4] > 0)
                    rows.Add(parsed);
            }

            return rows;
        }
    }

    public override uint GetId() => Id;

    public override void Loaded() => GameData.FarmWarLevelData[Id] = this;

    private static uint ReadUInt(JToken? token)
    {
        switch (token)
        {
            case null:
                return 0;
            case JValue value when value.Value is long l:
                return (uint)Math.Max(0, l);
            case JValue value when value.Value is int i:
                return (uint)Math.Max(0, i);
            case JValue value when value.Value is double d:
                return (uint)Math.Max(0, d);
            default:
                return uint.TryParse(token.ToString().Trim(), out var parsed) ? parsed : 0;
        }
    }
}
