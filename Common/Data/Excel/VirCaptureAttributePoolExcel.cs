using MikuSB.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// dlc/vircapture/attributepool.json - attribute affix roll table referenced by a monster card
/// template (AttackRandomId / DefenseRandomId / HealthRandomId, MonsterCardExcel).
/// AttributeList maps an attribute multiplier ("0.9" / "1" / "1.1") to its roll weight and
/// LevelList holds the affix tiers (VirCapture.lua:109 tbAttrFlag = {"A","B","C","S"}). The two
/// lists are paired position by position in ascending multiplier order, so pool 1 produces
/// A/B/C (20/60/20) while pool 2 only produces S.
/// </summary>
[ResourceEntity("dlc/vircapture/attributepool.json")]
public class VirCaptureAttributePoolExcel : ExcelResource
{
    [JsonProperty("Id")] public uint Id { get; set; }

    [JsonProperty("AttributeList")] public JToken? AttributeListRaw { get; set; }

    [JsonProperty("LevelList")] public List<uint> LevelList { get; set; } = [];

    [JsonIgnore]
    public (string Multiplier, uint Weight)[] Attributes { get; private set; } = [];

    public override uint GetId() => Id;

    public override void Loaded()
    {
        Attributes = VirCapturePoolParse.Weighted(AttributeListRaw);
        GameData.VirCaptureAttributePoolData[Id] = this;
    }
}

/// <summary>Shared helpers for the {key: weight} columns of the VirCapture roll tables.</summary>
public static class VirCapturePoolParse
{
    /// <summary>Parses a {"2": 100, "0": 900} pool column into ascending-key weighted rows.</summary>
    public static (string Key, uint Weight)[] Weighted(JToken? token)
    {
        var rows = new List<(string Key, uint Weight)>();
        if (token is not JObject obj)
            return [.. rows];

        foreach (var property in obj.Properties())
        {
            var weight = property.Value.Type == JTokenType.Integer
                ? property.Value.Value<uint>()
                : 0u;
            if (weight == 0)
                continue;
            rows.Add((property.Name, weight));
        }

        return [.. rows];
    }

    /// <summary>Lenient integer read for cells that may arrive as number or string.</summary>
    public static uint ToInt(JToken? token)
    {
        if (token == null)
            return 0;

        return token.Type switch
        {
            JTokenType.Integer => token.Value<ulong>() is var value && value > uint.MaxValue
                ? uint.MaxValue
                : (uint)token.Value<ulong>(),
            JTokenType.Float => (uint)Math.Max(0, Math.Min(uint.MaxValue, token.Value<double>())),
            JTokenType.String => uint.TryParse(token.Value<string>(), out var parsed) ? parsed : 0u,
            _ => 0u
        };
    }

    /// <summary>Rolls one key against the row weights; null when the column is empty.</summary>
    public static string? Roll((string Key, uint Weight)[] rows)
    {
        if (rows.Length == 0)
            return null;

        var total = rows.Aggregate(0u, (sum, row) => sum + row.Weight);
        if (total == 0)
            return null;

        var pick = (uint)Random.Shared.Next(1, (int)Math.Min(int.MaxValue, total + 1));
        foreach (var row in rows)
        {
            if (pick <= row.Weight)
                return row.Key;
            pick -= row.Weight;
        }

        return rows[^1].Key;
    }
}
