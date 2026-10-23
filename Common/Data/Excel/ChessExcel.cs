using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MikuSB.Data;

namespace MikuSB.Data.Excel;

// challenge/chess/chess.json -- one row per "board season" (ID 1..4). The client keeps these rows in
// ChessLogic.tbTimeConf and uses BeginTime/EndTime as the *display* window, Award as the currency id
// that rank rewards are paid in, and Story as the opening plot id.
[ResourceEntity("challenge/chess/chess.json")]
public class ChessActivityExcel : ExcelResource
{
    [JsonProperty("ID")] public JToken? IdRaw { get; set; }
    [JsonProperty("BeginTime")] public JToken? BeginRaw { get; set; }
    [JsonProperty("EndTime")] public JToken? EndRaw { get; set; }
    [JsonProperty("Story")] public JToken? StoryRaw { get; set; }
    [JsonProperty("Award")] public JToken? AwardRaw { get; set; }
    [JsonProperty("AwardDayMax")] public JToken? AwardDayMaxRaw { get; set; }

    [JsonIgnore] public uint Id => ChessJson.ReadUInt(IdRaw);
    [JsonIgnore] public string Begin => ChessJson.ReadText(BeginRaw);
    [JsonIgnore] public string End => ChessJson.ReadText(EndRaw);
    [JsonIgnore] public string Story => ChessJson.ReadText(StoryRaw);

    /// <summary>Currency id rank rewards are paid in; 0 / empty means "pay FansItem instead".</summary>
    [JsonIgnore] public uint AwardId => ChessJson.ReadUInt(AwardRaw);

    [JsonIgnore] public uint AwardDayMax => ChessJson.ReadUInt(AwardDayMaxRaw);

    public override uint GetId() => Id;

    public override void Loaded() => GameData.ChessActivityData[Id] = this;
}

// challenge/chess/chess_map.json -- one row per (season, map). RankAward is indexed *by rank* (1-based,
// ipairs), so [20,10] means "1st gets 20, 2nd gets 10" of ChessActivityExcel.AwardId.
[ResourceEntity("challenge/chess/chess_map.json")]
public class ChessMapExcel : ExcelResource
{
    [JsonProperty("ID")] public JToken? ActivityRaw { get; set; }
    [JsonProperty("MapId")] public JToken? MapRaw { get; set; }
    [JsonProperty("UnlockTime")] public JToken? UnlockRaw { get; set; }
    [JsonProperty("RankAward")] public JToken? RankAwardRaw { get; set; }
    [JsonProperty("MapTarget")] public JToken? MapTargetRaw { get; set; }

    [JsonIgnore] public uint ActivityId => ChessJson.ReadUInt(ActivityRaw);
    [JsonIgnore] public uint MapId => ChessJson.ReadUInt(MapRaw);
    [JsonIgnore] public string UnlockTime => ChessJson.ReadText(UnlockRaw);
    [JsonIgnore] public IReadOnlyList<uint> RankAwards => ChessJson.ReadUIntList(RankAwardRaw);
    [JsonIgnore] public uint MapTarget => ChessJson.ReadUInt(MapTargetRaw);

    public override uint GetId() => ActivityId * 100 + MapId;

    public override void Loaded() => GameData.ChessMapData[GetId()] = this;

    public uint RankAward(uint rank)
    {
        var awards = RankAwards;
        if (rank == 0 || rank > awards.Count)
            return 0;
        return awards[(int)rank - 1];
    }
}

// challenge/chess/chess_richmanawards.json -- the eight "vote for your girl" reward tiers. Vote is the
// vote count needed, Award is the payout row (4 elements, count appended by the caller).
[ResourceEntity("challenge/chess/chess_richmanawards.json")]
public class ChessRichmanAwardExcel : ExcelResource
{
    [JsonProperty("Id")] public JToken? IdRaw { get; set; }
    [JsonProperty("Vote")] public JToken? VoteRaw { get; set; }
    [JsonProperty("Award")] public JToken? AwardRaw { get; set; }

    [JsonIgnore] public uint Id => ChessJson.ReadUInt(IdRaw);
    [JsonIgnore] public uint Vote => ChessJson.ReadUInt(VoteRaw);
    [JsonIgnore] public IReadOnlyList<uint> Award => ChessJson.ReadUIntList(AwardRaw);

    public override uint GetId() => Id;

    public override void Loaded() => GameData.ChessRichmanAwardData[Id] = this;
}

// dlc/richman/def/map_def.json -- four richman boards. The client only reads eight columns
// (RichmanLogic.lua:124-145); max_round and init_money are the two the server needs for bookkeeping.
[ResourceEntity("dlc/richman/def/map_def.json")]
public class RichmanMapDefExcel : ExcelResource
{
    [JsonProperty("id")] public JToken? IdRaw { get; set; }
    [JsonProperty("max_round")] public JToken? MaxRoundRaw { get; set; }
    [JsonProperty("init_money")] public JToken? InitMoneyRaw { get; set; }
    [JsonProperty("map_id")] public JToken? LevelRaw { get; set; }
    [JsonProperty("type")] public JToken? TypeRaw { get; set; }

    [JsonIgnore] public uint Id => ChessJson.ReadUInt(IdRaw);
    [JsonIgnore] public uint MaxRound => ChessJson.ReadUInt(MaxRoundRaw);
    [JsonIgnore] public uint InitMoney => ChessJson.ReadUInt(InitMoneyRaw);
    [JsonIgnore] public uint LevelId => ChessJson.ReadUInt(LevelRaw);
    [JsonIgnore] public uint Type => ChessJson.ReadUInt(TypeRaw);

    public override uint GetId() => Id;

    public override void Loaded() => GameData.RichmanMapDefData[Id] = this;
}

// dlc/richman_love/def/map_def.json -- single row (id 1). Only max_round is needed server side.
[ResourceEntity("dlc/richman_love/def/map_def.json")]
public class RichmanLoveMapDefExcel : ExcelResource
{
    [JsonProperty("id")] public JToken? IdRaw { get; set; }
    [JsonProperty("max_round")] public JToken? MaxRoundRaw { get; set; }
    [JsonProperty("map_id")] public JToken? LevelRaw { get; set; }

    [JsonIgnore] public uint Id => ChessJson.ReadUInt(IdRaw);
    [JsonIgnore] public uint MaxRound => ChessJson.ReadUInt(MaxRoundRaw);
    [JsonIgnore] public uint LevelId => ChessJson.ReadUInt(LevelRaw);

    public override uint GetId() => Id;

    public override void Loaded() => GameData.RichmanLoveMapDefData[Id] = this;
}

// dlc/richman_love/def/12_end.json -- ending rows. Type drives the reward branch, Content is the
// message body shown after unlocking.
[ResourceEntity("dlc/richman_love/def/12_end.json")]
public class RichmanLoveEndExcel : ExcelResource
{
    [JsonProperty("id")] public JToken? IdRaw { get; set; }
    [JsonProperty("Type")] public JToken? TypeRaw { get; set; }
    [JsonProperty("Content")] public JToken? ContentRaw { get; set; }

    [JsonIgnore] public uint Id => ChessJson.ReadUInt(IdRaw);
    [JsonIgnore] public uint Type => ChessJson.ReadUInt(TypeRaw);
    [JsonIgnore] public string Content => ChessJson.ReadText(ContentRaw);

    public override uint GetId() => Id;

    public override void Loaded() => GameData.RichmanLoveEndData[Id] = this;
}

internal static class ChessJson
{
    public static uint ReadUInt(JToken? token)
    {
        switch (token)
        {
            case null:
                return 0;
            case JValue value:
                return value.Type switch
                {
                    JTokenType.Integer => (uint)Math.Clamp(value.Value<long>(), 0, uint.MaxValue),
                    JTokenType.Float => (uint)Math.Clamp((long)value.Value<double>(), 0, uint.MaxValue),
                    JTokenType.String => uint.TryParse(value.Value<string>(), out var parsed) ? parsed : 0,
                    _ => 0
                };
            case JArray array:
                foreach (var item in array)
                {
                    var parsed = ReadUInt(item);
                    if (parsed != 0)
                        return parsed;
                }

                return 0;
            default:
                return 0;
        }
    }

    public static string ReadText(JToken? token)
    {
        if (token is JValue value)
        {
            return value.Type switch
            {
                JTokenType.String => value.Value<string>()?.Trim('[', ']') ?? string.Empty,
                JTokenType.Integer => value.Value<long>().ToString(),
                _ => string.Empty
            };
        }

        return string.Empty;
    }

    public static IReadOnlyList<uint> ReadUIntList(JToken? token)
    {
        var result = new List<uint>();
        if (token is JArray array)
        {
            foreach (var item in array)
            {
                var parsed = ReadUInt(item);
                if (parsed != 0)
                    result.Add(parsed);
            }
        }
        else
        {
            var single = ReadUInt(token);
            if (single != 0)
                result.Add(single);
        }

        return result;
    }
}
