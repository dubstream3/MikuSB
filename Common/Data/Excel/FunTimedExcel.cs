using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

// 抢滩登陆 / 趣味限时关卡 (fun_timed). Two tables drive the mode:
//
//   fun_timed/fun_timed.json -> [ { Session, OpenTime[2], Condition, Levels[7], LevelIcons[7], Intro } ]
//   fun_timed/level.json     -> [ { ID, Type, MapID, StarCondition, StarAwards[3][n][5], TimeLimit[2], ... } ]
//
// The client (Launch/FunTimed/FunTimed.lua:282-336) loads both CSVs into plain globals and reads the
// star bits from (21, levelID) and the best score from (22, levelID) (:68-86). Settlement arrives
// wrapped in Chapter_DealLevelSettlement with sCmd = "FunTimed_LevelSettlement" (:372), so the server
// needs the level rows to know which StarAwards tiers a clear earned.
//
// Availability of the mode is a pure client-side decision: FunTimedLevel.IsOpenTime (:30-41) and
// FunTimedSession.CheckOpen (:161-180) compare GetTime() against the CSV windows directly instead of
// going through the global IsInTime helper, which is why the era patch has to rewrite these tables
// (DlcClientPatches.FunTimedWindows) rather than relaxing a shared helper.

/// <summary>
/// One season of 抢滩登陆 (fun_timed/fun_timed.json). The rows carry no id column: the client keys them
/// by <c>Session</c> (FunTimed.lua:334), which is also the value a DlcFuncSevendays button stores in
/// <c>DlcFuncConfId.Sevendays</c> (DLC28 -> 4, DLC31 -> 5, DLC36 -> 6).
/// </summary>
[ResourceEntity("fun_timed/fun_timed.json")]
public class FunTimedSessionExcel : ExcelResource
{
    [JsonProperty("Session")] public uint Session { get; set; }
    [JsonProperty("Levels")] public JToken? LevelsRaw { get; set; }
    [JsonProperty("LevelIcons")] public JToken? LevelIconsRaw { get; set; }
    [JsonProperty("Intro")] public JToken? IntroRaw { get; set; }

    [JsonIgnore] public List<uint> Levels => ReadUIntList(LevelsRaw);
    [JsonIgnore] public List<uint> LevelIcons => ReadUIntList(LevelIconsRaw);
    [JsonIgnore] public uint Intro => ReadUInt(IntroRaw);

    public override uint GetId() => Session;

    public override void Loaded()
    {
        GameData.FunTimedSessionData[Session] = this;
    }

    private static int ReadInt(JToken? token)
    {
        if (token == null)
            return 0;

        return token.Type switch
        {
            JTokenType.Integer => token.Value<int>(),
            JTokenType.Float => (int)token.Value<decimal>(),
            JTokenType.String when int.TryParse(token.Value<string>(), out var value) => value,
            _ => 0
        };
    }

    private static uint ReadUInt(JToken? token)
    {
        var value = ReadInt(token);
        return value > 0 ? (uint)value : 0;
    }

    private static List<uint> ReadUIntList(JToken? token)
    {
        if (token is not JArray array)
            return [];

        var result = new List<uint>(array.Count);
        foreach (var item in array)
        {
            var value = ReadUInt(item);
            if (value > 0)
                result.Add(value);
        }

        return result;
    }
}

/// <summary>
/// One 抢滩登陆 level (fun_timed/level.json). <c>StarAwards</c> is a three level array: the outer index
/// is the star tier (1, 2, 3 stars), the middle level holds the reward rows of that tier and each row is
/// the usual [genre, detail, particular, level, count] tuple (level.json:8406 ->
/// [[[5,29,8,1,20]] / [[5,1,1,3,2]] / [[4,3,1,1,5000]]]).
/// </summary>
[ResourceEntity("fun_timed/level.json")]
public class FunTimedLevelExcel : ExcelResource
{
    [JsonProperty("ID")] public uint Id { get; set; }
    [JsonProperty("Type")] public JToken? TypeRaw { get; set; }
    [JsonProperty("MapID")] public JToken? MapIdRaw { get; set; }
    [JsonProperty("FightID")] public JToken? FightIdRaw { get; set; }
    [JsonProperty("StarCondition")] public JToken? StarConditionRaw { get; set; }
    [JsonProperty("LevelStrength")] public JToken? LevelStrengthRaw { get; set; }
    [JsonProperty("TeamRuleID")] public JToken? TeamRuleIdRaw { get; set; }
    [JsonProperty("StarAwards")] public JToken? StarAwardsRaw { get; set; }
    [JsonProperty("TimeLimit")] public JToken? TimeLimitRaw { get; set; }

    [JsonIgnore] public uint Type => ReadUInt(TypeRaw);
    [JsonIgnore] public uint MapId => ReadUInt(MapIdRaw);
    [JsonIgnore] public uint FightId => ReadUInt(FightIdRaw);
    [JsonIgnore] public uint LevelStrength => ReadUInt(LevelStrengthRaw);
    [JsonIgnore] public uint TeamRuleId => ReadUInt(TeamRuleIdRaw);
    [JsonIgnore] public List<List<List<uint>>> StarAwards => ReadStarAwards(StarAwardsRaw);

    public override uint GetId() => Id;

    public override void Loaded()
    {
        GameData.FunTimedLevelData[Id] = this;
    }

    /// <summary>
    /// The reward rows of a single star tier (1 based), in the flat five element shape the client reads
    /// from <c>tbAwards</c> (FunTimed.lua:382-392).
    /// </summary>
    public List<List<uint>> TierAwards(int tier)
    {
        var awards = StarAwards;
        if (tier < 1 || tier > awards.Count)
            return [];

        var result = new List<List<uint>>(awards[tier - 1].Count);
        foreach (var row in awards[tier - 1])
        {
            if (row.Count >= 5 && row[4] > 0)
                result.Add(row);
        }

        return result;
    }

    private static int ReadInt(JToken? token)
    {
        if (token == null)
            return 0;

        return token.Type switch
        {
            JTokenType.Integer => token.Value<int>(),
            JTokenType.Float => (int)token.Value<decimal>(),
            JTokenType.String when int.TryParse(token.Value<string>(), out var value) => value,
            _ => 0
        };
    }

    private static uint ReadUInt(JToken? token)
    {
        var value = ReadInt(token);
        return value > 0 ? (uint)value : 0;
    }

    private static List<List<List<uint>>> ReadStarAwards(JToken? token)
    {
        if (token is not JArray tiers)
            return [];

        var result = new List<List<List<uint>>>(tiers.Count);
        foreach (var tier in tiers.OfType<JArray>())
        {
            var rows = new List<List<uint>>(tier.Count);
            foreach (var row in tier.OfType<JArray>())
            {
                var values = new List<uint>(row.Count);
                foreach (var item in row)
                    values.Add(ReadUInt(item));
                rows.Add(values);
            }

            result.Add(rows);
        }

        return result;
    }
}
