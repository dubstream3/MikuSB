using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

// Resource tables for the DLC1 2D rogue (Script/DLC/Rogue/Rogue.lua). The client loads the same
// tables as CSV (LoadCsv("dlc/dlc1/rogue/<name>.txt", 1)); the resource tree ships the JSON mirror.
public static class RogueJson
{
    public static uint ReadUInt(JToken? token) => token switch
    {
        null => 0,
        JValue value when value.Type == JTokenType.Integer => (uint)value.Value<long>(),
        JValue value when value.Type == JTokenType.Float => (uint)value.Value<double>(),
        JValue value when value.Type == JTokenType.String =>
            uint.TryParse(value.Value<string>(), out var parsed) ? parsed : 0,
        JArray array => array.Select(ReadUInt).FirstOrDefault(number => number != 0),
        _ => 0
    };

    public static string? ReadText(JToken? token) => token switch
    {
        null => null,
        JValue value when value.Type == JTokenType.String => value.Value<string>(),
        JValue value => value.ToString(),
        _ => token.ToString()
    };

    public static List<uint> ReadUIntList(JToken? token)
    {
        var result = new List<uint>();
        if (token is not JArray array)
            return result;

        foreach (var item in array)
        {
            var number = ReadUInt(item);
            if (number != 0)
                result.Add(number);
        }

        return result;
    }

    public static List<List<uint>> ReadRows(JToken? token)
    {
        var rows = new List<List<uint>>();
        if (token is not JArray array)
            return rows;

        foreach (var row in array)
        {
            if (row is not JArray cells)
                continue;

            var values = cells.Select(ReadUInt).ToList();
            if (values.Count >= 5 && values[4] > 0)
                rows.Add(values);
        }

        return rows;
    }

    // shop.json goods columns are weighted dictionaries: {"1101":5000,"1202":5000}.
    public static List<(uint Id, uint Weight)> ReadWeighted(JToken? token)
    {
        var result = new List<(uint, uint)>();
        if (token is not JObject obj)
            return result;

        foreach (var property in obj.Properties())
        {
            if (!uint.TryParse(property.Name, out var id))
                continue;

            var weight = ReadUInt(property.Value);
            if (id != 0 && weight != 0)
                result.Add((id, weight));
        }

        return result;
    }

    // activities.StartTime/EndTime look like "[202308210400]"; the client strips the brackets and
    // parses yyyyMMddHHmm as local time (Rogue.lua:74-75).
    public static long ToUnix(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return 0;

        var text = raw.Trim().Trim('[', ']');
        if (text.Length == 0 || text == "-1")
            return 0;

        return DateTime.TryParseExact(text, "yyyyMMddHHmm", null,
            System.Globalization.DateTimeStyles.None, out var time)
            ? new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Local)).ToUnixTimeSeconds()
            : 0;
    }
}

// dlc/dlc1/rogue/activities.json -- 3 rows, all 2023 windows. The client compares real GetTime()
// against its own copy, so the module only opens when the DLC1 window is faked through (25,9999).
[ResourceEntity("dlc/dlc1/rogue/activities.json")]
public class RogueActivityExcel : ExcelResource
{
    [JsonProperty("ID")] public JToken? IdRaw { get; set; }
    [JsonProperty("StartTime")] public JToken? StartRaw { get; set; }
    [JsonProperty("EndTime")] public JToken? EndRaw { get; set; }
    [JsonProperty("MapID")] public JToken? MapIdRaw { get; set; }
    [JsonProperty("ShopID")] public JToken? ShopIdRaw { get; set; }
    [JsonProperty("Price")] public JToken? PriceRaw { get; set; }
    [JsonProperty("Refresh")] public JToken? RefreshRaw { get; set; }
    [JsonProperty("TaskList")] public JToken? TaskListRaw { get; set; }

    [JsonIgnore] public uint Id => RogueJson.ReadUInt(IdRaw);
    [JsonIgnore] public string? Start => RogueJson.ReadText(StartRaw);
    [JsonIgnore] public string? End => RogueJson.ReadText(EndRaw);
    [JsonIgnore] public uint MapId => RogueJson.ReadUInt(MapIdRaw);
    [JsonIgnore] public uint ShopId => RogueJson.ReadUInt(ShopIdRaw);
    [JsonIgnore] public List<uint> Prices => RogueJson.ReadUIntList(PriceRaw);
    [JsonIgnore] public List<uint> Refreshes => RogueJson.ReadUIntList(RefreshRaw);
    [JsonIgnore] public List<uint> TaskIds => RogueJson.ReadUIntList(TaskListRaw);

    public override uint GetId() => Id;

    public override void Loaded() => GameData.RogueActivityData[Id] = this;
}

// dlc/dlc1/rogue/map1.json -- 294 rows = 3 maps x 98 nodes. The client owns the graph (Next edges);
// the server only needs the entry node and the node type to decide what to roll.
[ResourceEntity("dlc/dlc1/rogue/map1.json")]
public class RogueMapExcel : ExcelResource
{
    [JsonProperty("MapID")] public JToken? MapIdRaw { get; set; }
    [JsonProperty("ID")] public JToken? IdRaw { get; set; }
    [JsonProperty("Node")] public JToken? NodeRaw { get; set; }
    [JsonProperty("LevelID")] public JToken? LevelIdRaw { get; set; }
    [JsonProperty("ShopID")] public JToken? ShopIdRaw { get; set; }
    [JsonProperty("Next")] public JToken? NextRaw { get; set; }

    [JsonIgnore] public uint MapId => RogueJson.ReadUInt(MapIdRaw);
    [JsonIgnore] public uint Id => RogueJson.ReadUInt(IdRaw);
    [JsonIgnore] public uint Node => RogueJson.ReadUInt(NodeRaw);
    [JsonIgnore] public uint LevelId => RogueJson.ReadUInt(LevelIdRaw);
    [JsonIgnore] public uint ShopId => RogueJson.ReadUInt(ShopIdRaw);
    [JsonIgnore] public List<uint> NextIds => RogueJson.ReadUIntList(NextRaw);

    // Node ids restart at 10001 for every map (3 maps x 98 identical ids), so the dictionary key has
    // to carry the map id or two thirds of the table silently disappear.
    public override uint GetId() => MapId * 100000 + Id;

    public override void Loaded() => GameData.RogueMapData[GetId()] = this;
}

// dlc/dlc1/rogue/level.json -- node battle rows; ShowAward is the clear drop ([[G,D,P,L,N], ...]).
[ResourceEntity("dlc/dlc1/rogue/level.json")]
public class RogueLevelExcel : ExcelResource
{
    [JsonProperty("ID")] public JToken? IdRaw { get; set; }
    [JsonProperty("MapID")] public JToken? MapIdRaw { get; set; }
    [JsonProperty("ShowAward")] public JToken? ShowAwardRaw { get; set; }

    [JsonIgnore] public uint Id => RogueJson.ReadUInt(IdRaw);
    [JsonIgnore] public uint MapId => RogueJson.ReadUInt(MapIdRaw);
    [JsonIgnore] public List<List<uint>> AwardRows => RogueJson.ReadRows(ShowAwardRaw);

    public override uint GetId() => Id;

    public override void Loaded() => GameData.RogueLevelData[Id] = this;
}

// dlc/dlc1/rogue/shop.json -- 19 rows keyed by Periods (= activities.ShopID). Goods1..6 are weighted
// pools; the server picks one id per group to fill (25,10)[nodeId].
[ResourceEntity("dlc/dlc1/rogue/shop.json")]
public class RogueShopExcel : ExcelResource
{
    [JsonProperty("Periods")] public JToken? PeriodsRaw { get; set; }
    [JsonProperty("ID")] public JToken? IdRaw { get; set; }
    [JsonProperty("Goods1")] public JToken? Goods1Raw { get; set; }
    [JsonProperty("Goods2")] public JToken? Goods2Raw { get; set; }
    [JsonProperty("Goods3")] public JToken? Goods3Raw { get; set; }
    [JsonProperty("Goods4")] public JToken? Goods4Raw { get; set; }
    [JsonProperty("Goods5")] public JToken? Goods5Raw { get; set; }
    [JsonProperty("Goods6")] public JToken? Goods6Raw { get; set; }

    [JsonIgnore] public uint Periods => RogueJson.ReadUInt(PeriodsRaw);
    [JsonIgnore] public uint Id => RogueJson.ReadUInt(IdRaw);

    [JsonIgnore]
    public List<List<(uint Id, uint Weight)>> GoodsPools =>
    [
        RogueJson.ReadWeighted(Goods1Raw),
        RogueJson.ReadWeighted(Goods2Raw),
        RogueJson.ReadWeighted(Goods3Raw),
        RogueJson.ReadWeighted(Goods4Raw),
        RogueJson.ReadWeighted(Goods5Raw),
        RogueJson.ReadWeighted(Goods6Raw)
    ];

    public override uint GetId() => Periods;

    public override void Loaded() => GameData.RogueShopData[Periods] = this;
}
