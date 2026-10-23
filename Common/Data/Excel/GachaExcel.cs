using MikuSB.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

[ResourceEntity("gacha/gacha.json")]
public class GachaExcel : ExcelResource
{
    public uint ID { get; set; }
    public List<string>? Pool { get; set; }
    public uint Probability { get; set; }
    public uint ProbabilityTen { get; set; }
    public JToken? ProtectNum { get; set; }
    public JToken? UpNum { get; set; }
    public uint? ProtectTag { get; set; }
    public uint? ProtectType { get; set; }
    public JToken? ProtectCount { get; set; }
    public uint? UpSelect { get; set; }

    // gacha.json is generated from the client's gacha table, so the cast columns arrive in more than
    // one spelling: CastOne/CastTen as JSON arrays, CastSpecial as the empty string when the banner
    // has no event ticket. The raw tokens are kept and normalized by the properties below, the same
    // way OtherItemExcel tolerates several spellings of its numeric columns.
    [JsonProperty("CastOne")] public JToken? CastOneRaw { get; set; }
    [JsonProperty("CastTen")] public JToken? CastTenRaw { get; set; }
    [JsonProperty("CastSpecial")] public JToken? CastSpecialRaw { get; set; }
    [JsonProperty("DailyTimeLimit")] public JToken? DailyTimeLimitRaw { get; set; }

    /// <summary>
    /// Price of a single pull: [[Genre, Detail, Particular, Level, Count], ...], the table
    /// Gacha.lua:916 hands to GachaTemplate:CheckCost. A row without a count keeps 0 so the caller
    /// can fall back to the pull size, exactly like the client's "tbCost[1][5] or nTime"
    /// (Gacha.lua:837).
    /// </summary>
    [JsonIgnore]
    public List<uint[]> CastOne => ReadCostRows(CastOneRaw);

    /// <summary>Price of a ten pull; its count may be discounted (gacha.json charges 8 for 10).</summary>
    [JsonIgnore]
    public List<uint[]> CastTen => ReadCostRows(CastTenRaw);

    /// <summary>
    /// [Genre, Detail, Particular, Level, Count] of the event ticket a pull mixes in, or null when
    /// the banner has none. Gacha.lua:817 addresses it with table.unpack, so only the first four
    /// fields name the item and the count field is unused.
    /// </summary>
    [JsonIgnore]
    public uint[]? CastSpecial
    {
        get
        {
            if (AsArray(CastSpecialRaw) is not { Count: > 0 } array)
                return null;

            // Stored flat ([5,21,4,5,1]); a wrapped [[5,21,4,5,1]] from a txt-derived row works too.
            return ReadCostRow(array[0] is JArray ? array[0] : array);
        }
    }

    /// <summary>Daily pull limit of the banner (Gacha.lua:967). Parsed for reference; not enforced yet.</summary>
    [JsonIgnore]
    public uint DailyTimeLimit => ReadUInt(DailyTimeLimitRaw);

    public override uint GetId() => ID;
    public override void Loaded() => GameData.GachaData[ID] = this;

    public override void AfterAllDone()
    {
        foreach (var poolName in Pool ?? [])
        {
            if (GameData.GachaPoolData.ContainsKey(poolName)) continue;
            var path = ConfigManager.Config.Path.ResourcePath + "/gacha/pool/" + poolName + ".json";
            if (!File.Exists(path)) continue;
            var json = File.ReadAllText(path);
            var items = JsonConvert.DeserializeObject<List<GachaPoolItem>>(json) ?? [];
            GameData.GachaPoolData[poolName] = items;
        }
    }

    /// <summary>
    /// Normalizes one cast column into rows of exactly 5 fields. Rows that carry no usable GDPL are
    /// dropped, so an empty result means "this banner has no price for that pull size".
    /// </summary>
    private static List<uint[]> ReadCostRows(JToken? token)
    {
        var rows = new List<uint[]>();
        if (AsArray(token) is not { } array)
            return rows;

        foreach (var entry in array)
        {
            if (ReadCostRow(entry) is { } row)
                rows.Add(row);
        }

        return rows;
    }

    private static uint[]? ReadCostRow(JToken? token)
    {
        if (AsArray(token) is not { Count: >= 4 } array)
            return null;

        var row = new uint[5];
        for (var i = 0; i < row.Length && i < array.Count; i++)
            row[i] = ReadUInt(array[i]);

        return row[0] != 0 ? row : null;
    }

    /// <summary>
    /// Reads a column as an array: JSON arrays pass through, a string is either JSON text
    /// ("[[5,21,1,4,1]]") or a bare "5,21,1,4,1" list, and an empty string such as the CastSpecial
    /// of a banner without event tickets means "not set".
    /// </summary>
    private static JArray? AsArray(JToken? token)
    {
        if (token == null)
            return null;

        if (token is JArray array)
            return array;

        if (token.Type != JTokenType.String)
            return null;

        var text = token.Value<string>()?.Trim();
        if (string.IsNullOrEmpty(text))
            return null;

        if (text.StartsWith('['))
        {
            try
            {
                return JArray.Parse(text);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        return new JArray(text.Split([','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static uint ReadUInt(JToken? token)
    {
        if (token == null)
            return 0;

        return token.Type switch
        {
            JTokenType.Integer => token.Value<uint>(),
            JTokenType.Float => (uint)Math.Max(0, token.Value<decimal>()),
            JTokenType.String when uint.TryParse(token.Value<string>(), out var value) => value,
            _ => 0
        };
    }
}

public class GachaPoolItem
{
    public int ID { get; set; }
    public int Rarity { get; set; }
    public List<uint> GDPL { get; set; } = [];
    public int Weight { get; set; }
    public int? UPTag { get; set; }
    public int? UPSelectTag { get; set; }
}
