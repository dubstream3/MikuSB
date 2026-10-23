using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// Donoduel rogue catch battles, dlc/DonoDuel/donoduel_catch_battle.json (33 rows).
///
/// CatchID is the id the client settles with (Req_RoomSettlement nCatchId comes from
/// DonoDuel:GetCatchId(), which the C++ side sets per room). Phase/Stack locate the room inside
/// the run; donorogue_level.json maps each settled LevelID to its CatchID. Only CatchID 2
/// (the guide room) carries Guide_Reward1 = [moneyType, amount] and Guide_Reward2 = a list of
/// [heroCardId, star, quality] the run is meant to grant; every other row leaves both columns
/// absent, hence the tolerant readers.
/// </summary>
[ResourceEntity("dlc/DonoDuel/donoduel_catch_battle.json")]
public class DonoDuelCatchBattleExcel : ExcelResource
{
    [JsonProperty("CatchID")] public JToken? CatchIdRaw { get; set; }
    [JsonProperty("Phase")] public JToken? PhaseRaw { get; set; }
    [JsonProperty("Stack")] public JToken? StackRaw { get; set; }
    [JsonProperty("Guide_Reward1")] public JToken? GuideReward1Raw { get; set; }
    [JsonProperty("Guide_Reward2")] public JToken? GuideReward2Raw { get; set; }

    [JsonIgnore] public uint CatchId => ReadUint(CatchIdRaw);
    [JsonIgnore] public uint Phase => ReadUint(PhaseRaw);
    [JsonIgnore] public uint Stack => ReadUint(StackRaw);

    [JsonIgnore] public IReadOnlyList<uint> GuideReward1 => ReadList(GuideReward1Raw);

    /// <summary>Each entry is [heroCardId, star, quality]; empty unless this is the guide room.</summary>
    [JsonIgnore]
    public IReadOnlyList<IReadOnlyList<uint>> GuideReward2 => ReadNested(GuideReward2Raw);

    public override uint GetId() => CatchId;

    public override void Loaded()
    {
        if (CatchId != 0)
            GameData.DonoDuelCatchBattleData[CatchId] = this;
    }

    private static IReadOnlyList<uint> ReadList(JToken? token)
    {
        var values = new List<uint>();
        if (token is not JArray array)
            return values;

        foreach (var item in array)
            values.Add(ReadUint(item));

        return values;
    }

    private static IReadOnlyList<IReadOnlyList<uint>> ReadNested(JToken? token)
    {
        var rows = new List<IReadOnlyList<uint>>();
        if (token is not JArray array)
            return rows;

        foreach (var item in array)
        {
            var row = ReadList(item);
            if (row.Count > 0)
                rows.Add(row);
        }

        return rows;
    }

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
