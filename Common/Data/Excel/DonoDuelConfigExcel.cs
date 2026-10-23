using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// The Donoduel tuning table (dlc/DonoDuel/donoduel_config.json): 27 rows, each an opaque
/// <c>Parameter</c> keyed by <c>ConfigID</c>. The shapes differ per row, so the accessors below
/// tolerate all three forms the shipped table uses:
///   flat list          e.g. 5  -&gt; [20, 50, 160]          (skill reshaping cost)
///   list of rows       e.g. 1  -&gt; [[quality, cost, min, max], ...]  (attribute gain)
///   map keyed by index e.g. 2  -&gt; {"2": 700, ...} or {"2": [[36, 103]], ...}
///
/// Client references: DonoDuelConfig.lua:530-600 (GetDonoDuelConfig / season cash id),
/// DonoDuel.lua:811-862 (recycle rewards), uw_donoduel_culture_skill.lua:372-495 (reshaping cost
/// indexed by the number of locked skill slots).
///
/// NOTE the shipped table also carries two malformed rows (ConfigID [] and a map) and one row where
/// ConfigID itself is a map - all of them parse to 0 here and are simply dropped, exactly like the
/// client's <c>tonumber(...) or 0</c> handling.
/// </summary>
[ResourceEntity("dlc/DonoDuel/donoduel_config.json")]
public class DonoDuelConfigExcel : ExcelResource
{
    [JsonProperty("ConfigID")] public JToken? ConfigIdRaw { get; set; }
    [JsonProperty("Parameter")] public JToken? ParameterRaw { get; set; }

    [JsonIgnore] public uint ConfigId => ReadUint(ConfigIdRaw);
    [JsonIgnore] public JToken? Parameter => ParameterRaw;

    public override uint GetId() => ConfigId;

    public override void Loaded()
    {
        if (ConfigId != 0)
            GameData.DonoDuelConfigData[ConfigId] = this;
    }

    /// <summary>Flat numeric list: [20, 50, 160] -&gt; 20, 50, 160 (empty for any other shape).</summary>
    public IReadOnlyList<uint> Flat()
    {
        var values = new List<uint>();
        if (ParameterRaw is not JArray { Count: > 0 } array)
            return values;

        foreach (var item in array)
        {
            // A list of rows is NOT flat - report nothing so callers cannot misread it.
            if (item is JArray or JObject)
                return [];

            values.Add(ReadUint(item));
        }

        return values;
    }

    /// <summary>Scalar behind a one element list: ConfigID 16 [6] -&gt; 6.</summary>
    public uint Scalar() => Flat().Count > 0 ? Flat()[0] : 0;

    /// <summary>Map lookup: {"2": 700, "3": 1500, ...} -&gt; ValueAt(2) = 700.</summary>
    public uint ValueAt(uint key)
    {
        if (ParameterRaw is not JObject map)
            return 0;

        foreach (var property in map.Properties())
        {
            if (uint.TryParse(property.Name, out var parsed) && parsed == key)
                return ReadUint(property.Value);
        }

        return 0;
    }

    /// <summary>
    /// [[quality, cost, minimum, maximum], ...] -&gt; the row whose first column is
    /// <paramref name="quality"/> (ConfigID 1/23/24: attack / defence / health upgrades).
    /// </summary>
    public IReadOnlyList<uint> QualityRow(uint quality)
    {
        if (ParameterRaw is not JArray rows)
            return [];

        foreach (var row in rows)
        {
            if (row is not JArray values || values.Count == 0)
                continue;

            if (ReadUint(values[0]) == quality)
                return values.Select(ReadUint).ToList();
        }

        return [];
    }

    /// <summary>
    /// {"2": [[36, 103]], ...} -&gt; the condition rows behind one key (ConfigID 3: the quality
    /// unlock requirements - "36" = a Donoduel story mission was claimed, "37" = an arena level
    /// was cleared).
    /// </summary>
    public IReadOnlyList<IReadOnlyList<uint>> RowsAt(uint key)
    {
        if (ParameterRaw is not JObject map)
            return [];

        JToken? target = null;
        foreach (var property in map.Properties())
        {
            if (uint.TryParse(property.Name, out var parsed) && parsed == key)
            {
                target = property.Value;
                break;
            }
        }

        if (target is not JArray rows)
            return [];

        var result = new List<IReadOnlyList<uint>>();
        foreach (var row in rows)
        {
            if (row is not JArray values)
                continue;

            result.Add(values.Select(ReadUint).ToList());
        }

        return result;
    }

    /// <summary>Same as <see cref="RowsAt"/> but for maps of plain numbers (ConfigID 13/14).</summary>
    public uint ValueOfMapKey(uint key) => ValueAt(key);

    private static uint ReadUint(JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null)
            return 0;

        try
        {
            return token.Type switch
            {
                JTokenType.Integer => token.Value<uint>(),
                JTokenType.Float => (uint)token.Value<double>(),
                JTokenType.Array or JTokenType.Object => 0,
                _ => uint.TryParse(token.ToString().Trim().Trim('[', ']', '"'), out var parsed) ? parsed : 0
            };
        }
        catch
        {
            return 0;
        }
    }
}
