using MikuSB.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// Item box content table (item/box.json). Maps the client's <c>Item.tbBox</c>, which
/// <c>Script/Item/Item.lua:1376-1395</c> (Item.LoadBoxConf, from item/box.txt) builds as
/// <c>tbBox[Index][ObtainType][group] = { rows }</c> with each row
/// <c>{ tbGDPLN, sObtainType, nWeight, nGroup, nRecommend }</c>. An item whose template
/// <c>LuaType</c> is "itembox" points at one index of this table through its <c>Param1</c>
/// (<c>Script/Item/Item.lua:861-869</c> Item.IsItemBox, and the Select path Item.IsSelectBox at
/// <c>Script/Item/Item.lua:833-845</c>).
///
/// A row's <c>GetId()</c> is the box index, and the file really does carry several rows per index
/// (1491 rows over 394 distinct indexes in the shipped table), so the rows are collected into
/// <see cref="BoxData"/> as a list instead of a one-row-per-id dictionary. Rows are appended in file
/// order, which also keeps the client's own iteration order (it walks
/// <c>tbBox[Index][ObtainType][nGroup]</c> with <c>pairs()</c>).
///
/// A "Random" index is one weighted draw per group, not per index: index 101 carries two groups of
/// five rows ("A" and "B" rewards) and each opened box has to yield one row out of each group.
/// </summary>
[ResourceEntity("item/box.json")]
public class BoxExcel : ExcelResource
{
    private static readonly Logger Log = new("BoxExcel");

    /// <summary>Grant every row of the index ("Fix").</summary>
    public const string ObtainFix = "Fix";

    /// <summary>Player picks one of the rows; the client sends the pick in <c>tbSelect</c>.</summary>
    public const string ObtainSelect = "Select";

    /// <summary>Weighted pick of exactly one row per group.</summary>
    public const string ObtainRandom = "Random";

    /// <summary>Independent weighted roll for every row (weight is in ten-thousandths).</summary>
    public const string ObtainProb = "Prob";

    /// <summary>
    /// Box index (the client's <c>Item.tbBox[Param1]</c> key). Not unique across the file: 1491 rows
    /// share 394 indexes, so <see cref="GetId"/> intentionally returns this value for many rows.
    /// </summary>
    [JsonProperty("Index")] public uint Index { get; set; }

    /// <summary>One of Fix / Select / Random / Prob (the client's <c>sObtainType</c>).</summary>
    [JsonProperty("ObtainType")] public string ObtainType { get; set; } = "";

    /// <summary>
    /// The reward row, kept raw because the CSV converter writes it as the string "[5,9,1,2,10]"
    /// while a hand-edited file may use a real array. Use <see cref="Reward"/> / <see cref="RewardCount"/>.
    /// </summary>
    [JsonProperty("GDPLN")] public JToken? Gdpln { get; set; }

    /// <summary>Roll weight. In ten-thousandths for "Prob" (see the 20000 row sums of index 102).</summary>
    [JsonProperty("Weight")] public JToken? WeightRaw { get; set; }

    /// <summary>Selection group; the client defaults a missing value to 1 (<c>Item.lua:1382</c>).</summary>
    [JsonProperty("Group")] public JToken? GroupRaw { get; set; }

    /// <summary>Non-zero marks the recommended entry of a Select group.</summary>
    [JsonProperty("Recommend")] public JToken? RecommendRaw { get; set; }

    [JsonIgnore] public uint Weight => ReadUInt(WeightRaw);
    [JsonIgnore] public uint Group => ReadUInt(GroupRaw, 1);
    [JsonIgnore] public uint Recommend => ReadUInt(RecommendRaw);
    [JsonIgnore] public bool IsFix => ObtainType == ObtainFix;
    [JsonIgnore] public bool IsSelect => ObtainType == ObtainSelect;
    [JsonIgnore] public bool IsRandom => ObtainType == ObtainRandom;
    [JsonIgnore] public bool IsProb => ObtainType == ObtainProb;

    /// <summary>The award row itself, already validated to be a 5 element [G, D, P, L, N] row.</summary>
    [JsonIgnore] public IReadOnlyList<uint> Reward { get; private set; } = [];

    /// <summary>Number of the awarded item per box; 0 for a malformed row.</summary>
    [JsonIgnore] public uint RewardCount => Reward.Count >= 5 ? Reward[4] : 0;

    /// <summary>
    /// Every box in the game, grouped by index. The dictionary itself lives on <c>GameData</c>
    /// (<see cref="GameData.BoxData"/>) next to the other resource tables; rows are appended here in
    /// file order.
    /// </summary>
    public override uint GetId() => Index;

    public override void Loaded()
    {
        var row = ParseGdpln(Gdpln);
        if (row == null)
        {
            // 20 of the shipped rows carry no GDPLN at all: index 841 (4 rows), 868 (4) and
            // 899-902 (3 each) are Fix boxes with nothing to grant. Skipping them keeps a single
            // malformed row from breaking the whole box; the handler answers error.BadParam for
            // those six indexes instead of consuming the item.
            Log.Warn($"Skipping box {Index} ({ObtainType}): GDPLN is missing or not a usable row.");
            return;
        }

        Reward = row;

        if (!GameData.BoxData.TryGetValue(Index, out var rows))
        {
            rows = [];
            GameData.BoxData[Index] = rows;
        }

        rows.Add(this);
    }

    /// <summary>
    /// Parses a "<c>[G,D,P,L,N]</c>"/"<c>{G,D,P,L,N}</c>" cell (the conversion script writes the CSV
    /// brace list as a JSON string) as well as a real JSON array. Returns null when the cell does not
    /// hold 5 numbers with a non-zero count.
    /// </summary>
    public static uint[]? ParseGdpln(JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null)
            return null;

        if (token.Type == JTokenType.String)
        {
            var text = token.Value<string>()?.Trim();
            if (string.IsNullOrEmpty(text))
                return null;

            using var reader = new JsonTextReader(new StringReader(text));
            if (!reader.Read() || reader.TokenType != JsonToken.StartArray)
                return null;

            token = JToken.ReadFrom(reader);
        }

        if (token is not JArray array || array.Count < 5)
            return null;

        var row = new uint[5];
        for (var i = 0; i < 5; i++)
            row[i] = ReadUInt(array[i]);

        // GDPLN[5] is the award count and the client multiplies it by the opened count
        // (umg_common_itemselect.lua:216), so a row without a count is not grantable.
        return row[4] == 0 ? null : row;
    }

    private static uint ReadUInt(JToken? token, uint fallback = 0)
    {
        if (token == null || token.Type == JTokenType.Null)
            return fallback;

        return token.Type switch
        {
            JTokenType.Integer => (uint)Math.Max(0, token.Value<long>()),
            JTokenType.Float => (uint)Math.Max(0, token.Value<decimal>()),
            JTokenType.String when uint.TryParse(token.Value<string>()?.Trim().Trim('[', ']', '{', '}'),
                out var value) => value,
            _ => fallback
        };
    }
}
