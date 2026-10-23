using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// activity/jigsaw_puzzle/jigsawpuzzle.json — one row per schedule (期数) of the jigsaw activity
/// ("适格启示录", client Activity/JigsawPuzzle.lua). The client loads the same table from txt
/// (JigsawPuzzle.lua:56) and keys it by ScheduleID; exactly three rows exist (1..3).
///
/// The schedule id is what the client calls "nId"/"nActId" in every request payload — it is NOT the
/// activity row id (390/417/602 in activity/activities.json), so the server must resolve
/// activity -> ScheduleID itself.
/// </summary>
[ResourceEntity("activity/jigsaw_puzzle/jigsawpuzzle.json")]
public class JigsawScheduleExcel : ExcelResource
{
    [JsonProperty("ScheduleID")] public uint ScheduleId { get; set; }
    [JsonProperty("RoleID")] public JToken? RoleRaw { get; set; }
    [JsonProperty("HelpImages")] public JToken? HelpImagesRaw { get; set; }
    [JsonProperty("TaskID")] public JToken? TaskIdsRaw { get; set; }
    [JsonProperty("Time-limitedTaskID")] public JToken? TimedTaskIdsRaw { get; set; }
    [JsonProperty("StartTime")] public string StartTime { get; set; } = "";
    [JsonProperty("EndTime")] public string EndTime { get; set; } = "";
    [JsonProperty("JigsawCG")] public JToken? JigsawCgRaw { get; set; }
    [JsonProperty("HitInterval")] public JToken? HitIntervalRaw { get; set; }
    [JsonProperty("PhaseTime")] public JToken? PhaseTimeRaw { get; set; }
    [JsonProperty("PhasePiece")] public JToken? PhasePieceRaw { get; set; }
    [JsonProperty("KeyID")] public JToken? KeyIdRaw { get; set; }
    [JsonProperty("KeyLimit")] public JToken? KeyLimitRaw { get; set; }
    [JsonProperty("ExchangeRate")] public JToken? ExchangeRateRaw { get; set; }
    [JsonProperty("Multiple")] public JToken? MultipleRaw { get; set; }

    /// <summary>RoleGDLP: [g,d,p,l,n,nTime] — the "girl" reward echoed back as tbGirl.</summary>
    [JsonIgnore] public IReadOnlyList<uint> Role => JigsawJson.ReadUIntList(RoleRaw);

    [JsonIgnore] public IReadOnlyList<uint> TaskIds => JigsawJson.ReadUIntList(TaskIdsRaw);

    [JsonIgnore] public IReadOnlyList<uint> JigsawCg => JigsawJson.ReadUIntList(JigsawCgRaw);

    /// <summary>PhaseTime: seconds after the activity start time at which each phase unlocks.</summary>
    [JsonIgnore] public IReadOnlyList<uint> PhaseTimes => JigsawJson.ReadUIntList(PhaseTimeRaw);

    /// <summary>PhasePiece: per phase, the piece ids that phase can draw.</summary>
    [JsonIgnore] public IReadOnlyList<IReadOnlyList<uint>> PhasePieces => JigsawJson.ReadUIntMatrix(PhasePieceRaw);

    [JsonIgnore] public uint KeyId => JigsawJson.ReadUInt(KeyIdRaw);
    [JsonIgnore] public uint KeyLimit => JigsawJson.ReadUInt(KeyLimitRaw);
    [JsonIgnore] public uint ExchangeRate => JigsawJson.ReadUInt(ExchangeRateRaw);
    [JsonIgnore] public uint HitInterval => JigsawJson.ReadUInt(HitIntervalRaw);

    public override uint GetId() => ScheduleId;

    public override void Loaded()
    {
        GameData.JigsawScheduleData[ScheduleId] = this;
    }
}

/// <summary>
/// activity/jigsaw_puzzle/jigsawpuzzle_piece.json — 32 pieces per schedule (96 rows total),
/// and the piece id restarts at 1 in every schedule, so the key must be the
/// (schedule, id) pair; a plain id dictionary would silently drop two thirds of the rows.
/// </summary>
[ResourceEntity("activity/jigsaw_puzzle/jigsawpuzzle_piece.json")]
public class JigsawPieceExcel : ExcelResource
{
    [JsonProperty("ScheduleID")] public uint ScheduleId { get; set; }
    [JsonProperty("ID")] public uint PieceId { get; set; }
    [JsonProperty("Coordinates")] public JToken? CoordinatesRaw { get; set; }
    [JsonProperty("PiecePic")] public JToken? PiecePicRaw { get; set; }
    [JsonProperty("ItemID")] public JToken? ItemRaw { get; set; }

    /// <summary>Board position 1..32 (row major), see JigsawPuzzle.GetPieceNameByPos.</summary>
    [JsonIgnore] public uint Position => JigsawJson.ReadUInt(CoordinatesRaw);

    [JsonIgnore] public uint PiecePic => JigsawJson.ReadUInt(PiecePicRaw);

    /// <summary>Reward row [g,d,p,l] (no count — the client wraps it with n=1).</summary>
    [JsonIgnore] public IReadOnlyList<uint> Item => JigsawJson.ReadUIntList(ItemRaw);

    public override uint GetId() => ScheduleId * 1000 + PieceId;

    public override void Loaded()
    {
        GameData.JigsawPieceData[GetId()] = this;
    }
}

internal static class JigsawJson
{
    public static uint ReadUInt(JToken? token)
    {
        if (token is null)
            return 0;

        switch (token.Type)
        {
            case JTokenType.Integer:
                return (uint)token.Value<long>();
            case JTokenType.Float:
                return (uint)token.Value<double>();
            case JTokenType.String:
                return uint.TryParse(token.Value<string>(), out var parsed) ? parsed : 0;
            default:
                return 0;
        }
    }

    public static List<uint> ReadUIntList(JToken? token)
    {
        var result = new List<uint>();
        if (token is not JArray array)
            return result;

        foreach (var item in array)
        {
            var value = ReadUInt(item);
            if (value > 0)
                result.Add(value);
        }

        return result;
    }

    public static List<IReadOnlyList<uint>> ReadUIntMatrix(JToken? token)
    {
        var result = new List<IReadOnlyList<uint>>();
        if (token is not JArray array)
            return result;

        foreach (var row in array)
            result.Add(ReadUIntList(row));

        return result;
    }
}
