using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// Blueprint island building (dlc/vircapture/build/building.json).
/// Every per-level column is indexed by the building level; <c>Attr1..3</c> are dictionaries
/// keyed by the attribute value that the level grants, valued by the coin cost of that level.
/// </summary>
[ResourceEntity("dlc/vircapture/build/building.json")]
public class VirCaptureBuildingExcel : ExcelResource
{
    [JsonProperty("ID")] public uint Id { get; set; }
    [JsonProperty("MapID")] public uint MapId { get; set; }
    [JsonProperty("Area")] public uint Area { get; set; }
    [JsonProperty("BuildType")] public uint BuildType { get; set; }
    [JsonProperty("OrginNeed")] public JToken? OrginNeedRaw { get; set; }
    [JsonProperty("OrginInCome")] public List<long> OrginInCome { get; set; } = [];
    [JsonProperty("AttributeEarnings")] public List<long> AttributeEarnings { get; set; } = [];
    [JsonProperty("OpeCost")] public List<long> OpeCost { get; set; } = [];
    [JsonProperty("BuildLevel")] public List<long> BuildLevel { get; set; } = [];
    [JsonProperty("Attr1")] public JToken? Attr1Raw { get; set; }
    [JsonProperty("Attr2")] public JToken? Attr2Raw { get; set; }
    [JsonProperty("Attr3")] public JToken? Attr3Raw { get; set; }

    [JsonIgnore] public List<List<long>> OrginNeed { get; private set; } = [];
    [JsonIgnore] public List<List<KeyValuePair<uint, uint>>> Attributes { get; private set; } = [];

    [JsonIgnore] public uint MaxLevel => (uint)Math.Max(0, OrginInCome.Count - 1);

    public override uint GetId() => Id;

    public long IncomeAt(uint level) => level < OrginInCome.Count ? OrginInCome[(int)level] : 0;

    public long CostAt(uint level) => level < OpeCost.Count ? OpeCost[(int)level] : 0;

    /// <summary>Coin price of moving from <paramref name="level"/> to the next level.</summary>
    public long UpgradeCost(uint level) => level + 1 < BuildLevel.Count ? BuildLevel[(int)(level + 1)] : 0;

    public long AttributeEarningAt(uint level) => level < AttributeEarnings.Count ? AttributeEarnings[(int)level] : 0;

    public long AttributeUpgradeCost(uint attributeIndex, uint attributeLevel)
    {
        if (attributeIndex is < 1 or > 3)
            return 0;
        var rows = Attributes[(int)(attributeIndex - 1)];
        return attributeLevel < rows.Count ? rows[(int)attributeLevel].Value : 0;
    }

    public uint AttributeValue(uint attributeIndex, uint attributeLevel)
    {
        if (attributeIndex is < 1 or > 3)
            return 0;
        var rows = Attributes[(int)(attributeIndex - 1)];
        return attributeLevel >= 1 && attributeLevel <= rows.Count ? rows[(int)(attributeLevel - 1)].Key : 0;
    }

    public uint MaxAttributeLevel(uint attributeIndex) =>
        attributeIndex is < 1 or > 3 ? 0 : (uint)Attributes[(int)(attributeIndex - 1)].Count;

    public override void Loaded()
    {
        OrginNeed = ReadRows(OrginNeedRaw);
        Attributes = [Normalize(Attr1Raw), Normalize(Attr2Raw), Normalize(Attr3Raw)];
        GameData.VirCaptureBuildingData[Id] = this;
    }

    private static List<List<long>> ReadRows(JToken? token)
    {
        var rows = new List<List<long>>();
        if (token is not JArray outer)
            return rows;

        foreach (var row in outer)
        {
            var cells = new List<long>();
            if (row is JArray array)
            {
                foreach (var cell in array)
                    cells.Add(cell.Value<long?>() ?? 0);
            }
            rows.Add(cells);
        }

        return rows;
    }

    private static List<KeyValuePair<uint, uint>> Normalize(JToken? token)
    {
        var rows = new List<KeyValuePair<uint, uint>>();
        if (token is not JObject map)
            return rows;

        foreach (var pair in map.Properties().OrderBy(property => uint.TryParse(property.Name, out var key) ? key : 0))
        {
            if (!uint.TryParse(pair.Name, out var value))
                continue;
            rows.Add(new KeyValuePair<uint, uint>(value, pair.Value.Value<uint?>() ?? 0));
        }

        return rows;
    }
}
