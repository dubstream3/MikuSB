using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MikuSB.Util;

namespace MikuSB.Data.Excel;

// dlc/starwish/*.json -- the StarWish (星祈心愿, DLC34 "DlcFuncCelebrate") tables. Only five files
// exist and every one of them is small (1 / 28 / 7 / 10 / 21 rows), so they are loaded whole and
// addressed by their `ID` column.
//
// Every numeric column is read through JToken so a single malformed cell (the exporter writes some
// of them as strings, and the time columns as "[YYYYMMDDHHmm]") cannot kill the rest of the file --
// see docs/contracts/README.md §1 failure mode 4.
internal static class StarWishJson
{
    public static uint ReadUInt(JToken? token)
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
                return uint.TryParse(token.ToString().Trim().Trim('[', ']'), out var parsed) ? parsed : 0;
        }
    }

    /// <summary>Reads a uint list, keeping zero entries (storyquest page 1 legitimately starts at
    /// level 0 and DormGift may carry zeros); use <see cref="ReadUIntListNoZero"/> where the table
    /// uses 0 as "empty slot".</summary>
    public static List<uint> ReadUIntList(JToken? token)
    {
        var list = new List<uint>();
        if (token is not JArray array)
            return list;

        foreach (var item in array)
            list.Add(ReadUInt(item));

        return list;
    }

    /// <summary>Reads a reward column shaped [ [G, D, P, L, N], ... ]. Rows that are not five
    /// numbers or carry a zero count are dropped, exactly like
    /// <see cref="MikuSB.GameServer.Game.Achievement.AchievementClaimService.ReadRewardRows"/>.</summary>
    public static List<List<uint>> ReadRows(JToken? token)
    {
        var rows = new List<List<uint>>();
        if (token is not JArray outer)
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

    /// <summary>Reads a single reward row shaped [G, D, P, L, N] (main.DormGift,
    /// score_award.Award, main.RepeatQuestAward[n]) and normalizes it into a one-element row list.</summary>
    public static List<List<uint>> ReadSingleRow(JToken? token)
    {
        var rows = new List<List<uint>>();
        if (token is not JArray cells)
            return rows;

        var parsed = new List<uint>();
        foreach (var cell in cells)
        {
            if (cell is JArray)
                return ReadRows(token); // already a row list, be forgiving
            parsed.Add(ReadUInt(cell));
        }

        // The client tolerates a missing count (uw_starwish_taskitem.lua:210 uses `v[5] or 1`).
        if (parsed.Count == 4)
            parsed.Add(1);

        if (parsed.Count >= 5 && parsed[4] > 0)
            rows.Add(parsed);

        return rows;
    }
}

// dlc/starwish/main.json -- one row per period (only ID=1 exists). StartTime/EndTime keep their
// bracket form ("[202512251000]"); DlcRuntimeOverrides.ParseConfigTime knows how to strip it.
[ResourceEntity("dlc/starwish/main.json")]
public class StarWishMainExcel : ExcelResource
{
    [JsonProperty("ID")] public JToken? IdRaw { get; set; }
    [JsonProperty("StartTime")] public JToken? StartRaw { get; set; }
    [JsonProperty("EndTime")] public JToken? EndRaw { get; set; }
    [JsonProperty("RepeatQuest")] public JToken? RepeatQuestRaw { get; set; }
    [JsonProperty("StoryQuest")] public JToken? StoryQuestRaw { get; set; }
    [JsonProperty("CashID")] public JToken? CashIdRaw { get; set; }
    [JsonProperty("RepeatQuestAward")] public JToken? RepeatQuestAwardRaw { get; set; }
    [JsonProperty("DormGift")] public JToken? DormGiftRaw { get; set; }
    [JsonProperty("StartPV")] public JToken? StartPvRaw { get; set; }
    [JsonProperty("EndPV")] public JToken? EndPvRaw { get; set; }

    [JsonIgnore] public uint Id => StarWishJson.ReadUInt(IdRaw);
    [JsonIgnore] public string? Start => StartRaw?.ToString();
    [JsonIgnore] public string? End => EndRaw?.ToString();
    [JsonIgnore] public uint StartPv => StarWishJson.ReadUInt(StartPvRaw);
    [JsonIgnore] public uint EndPv => StarWishJson.ReadUInt(EndPvRaw);

    /// <summary>Repeat (achievement-style) quest ids, daily refresh: 23001..23007 for ID=1.</summary>
    [JsonIgnore] public List<uint> RepeatQuests => StarWishJson.ReadUIntList(RepeatQuestRaw);

    /// <summary>Story quest ids in page order: 101..704 (28 rows).</summary>
    [JsonIgnore] public List<uint> StoryQuests => StarWishJson.ReadUIntList(StoryQuestRaw);

    /// <summary>[cashId, pricePerDraw] -- jigsaw lottery cost ([61, 100] for ID=1).</summary>
    [JsonIgnore]
    public (uint MoneyType, uint Price) LotteryCost
    {
        get
        {
            var pair = StarWishJson.ReadUIntList(CashIdRaw);
            return pair.Count >= 2 ? (pair[0], pair[1]) : (0u, 0u);
        }
    }

    /// <summary>Three rows, one per extra-award slot (nPos 1..3).</summary>
    [JsonIgnore] public List<List<uint>> RepeatQuestAwards => StarWishJson.ReadRows(RepeatQuestAwardRaw);

    /// <summary>Single [8, 99, 34, 3] row handed to the dorm gift pipeline when all 28 story quests
    /// are done (StarWishLogic.lua:1115-1118).</summary>
    [JsonIgnore] public List<List<uint>> DormGift => StarWishJson.ReadSingleRow(DormGiftRaw);

    public override uint GetId() => Id;

    public override void Loaded() => GameData.StarWishMainData[Id] = this;
}

// dlc/starwish/storyquest.json -- 28 rows, id step 100 per page (101..104, 201..204, ... 701..704).
[ResourceEntity("dlc/starwish/storyquest.json")]
public class StarWishStoryQuestExcel : ExcelResource
{
    [JsonProperty("ID")] public JToken? IdRaw { get; set; }
    [JsonProperty("Prequest")] public JToken? PrequestRaw { get; set; }
    [JsonProperty("CheckValue")] public JToken? CheckValueRaw { get; set; }
    [JsonProperty("Award")] public JToken? AwardRaw { get; set; }

    [JsonIgnore] public uint Id => StarWishJson.ReadUInt(IdRaw);
    [JsonIgnore] public uint Prequest => StarWishJson.ReadUInt(PrequestRaw);

    /// <summary>Number of sub steps; the client falls back to 1 when the column is empty
    /// (StarWishConfig.lua), so mirror that.</summary>
    [JsonIgnore]
    public uint CheckValue
    {
        get
        {
            var value = StarWishJson.ReadUInt(CheckValueRaw);
            return value == 0 ? 1u : value;
        }
    }

    [JsonIgnore] public List<List<uint>> Awards => StarWishJson.ReadRows(AwardRaw);

    public override uint GetId() => Id;

    public override void Loaded() => GameData.StarWishStoryQuestData[Id] = this;
}

// dlc/starwish/storyquest_page.json -- 7 rows, each holding 4 story quest ids and one reward row.
[ResourceEntity("dlc/starwish/storyquest_page.json")]
public class StarWishStoryPageExcel : ExcelResource
{
    [JsonProperty("ID")] public JToken? IdRaw { get; set; }
    [JsonProperty("Quests")] public JToken? QuestsRaw { get; set; }
    [JsonProperty("Time")] public JToken? TimeRaw { get; set; }
    [JsonProperty("Award")] public JToken? AwardRaw { get; set; }

    [JsonIgnore] public uint Id => StarWishJson.ReadUInt(IdRaw);
    [JsonIgnore] public string? Time => TimeRaw?.ToString();
    [JsonIgnore] public List<uint> Quests => StarWishJson.ReadUIntList(QuestsRaw);
    [JsonIgnore] public List<List<uint>> Awards => StarWishJson.ReadRows(AwardRaw);

    public override uint GetId() => Id;

    public override void Loaded() => GameData.StarWishStoryPageData[Id] = this;
}

// dlc/starwish/score_award.json -- 10 tiers, Score is the cumulative collection score required.
[ResourceEntity("dlc/starwish/score_award.json")]
public class StarWishScoreAwardExcel : ExcelResource
{
    [JsonProperty("ID")] public JToken? IdRaw { get; set; }
    [JsonProperty("Score")] public JToken? ScoreRaw { get; set; }
    [JsonProperty("Award")] public JToken? AwardRaw { get; set; }

    [JsonIgnore] public uint Id => StarWishJson.ReadUInt(IdRaw);
    [JsonIgnore] public uint Score => StarWishJson.ReadUInt(ScoreRaw);
    [JsonIgnore] public List<List<uint>> Awards => StarWishJson.ReadSingleRow(AwardRaw);

    public override uint GetId() => Id;

    public override void Loaded() => GameData.StarWishScoreAwardData[Id] = this;
}

// dlc/starwish/jigsaw.json -- 21 tree parts. Type 1..7 (7 shapes), Score 0 for the first row and 10
// for the rest, which is why a full collection is exactly 200 points (the last score tier).
[ResourceEntity("dlc/starwish/jigsaw.json")]
public class StarWishJigsawExcel : ExcelResource
{
    [JsonProperty("ID")] public JToken? IdRaw { get; set; }
    [JsonProperty("Type")] public JToken? TypeRaw { get; set; }
    [JsonProperty("Score")] public JToken? ScoreRaw { get; set; }

    [JsonIgnore] public uint Id => StarWishJson.ReadUInt(IdRaw);
    [JsonIgnore] public uint Type => StarWishJson.ReadUInt(TypeRaw);
    [JsonIgnore] public uint Score => StarWishJson.ReadUInt(ScoreRaw);

    public override uint GetId() => Id;

    public override void Loaded() => GameData.StarWishJigsawData[Id] = this;
}
