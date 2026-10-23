using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

// DLC 3.3 爬塔 (DlcFuncClimbTower -> UI.Open("ClimbTower")) is a separate mode from 逆境之塔
// (challenge/climbtower/*). Its tables live under dlc/climb_tower/ and are only ever read by the client
// config loader in DLC/ClimbTower/ClimbTower.lua:143-278 - nothing but the star milestone payouts had to
// move to the server, so only the three tables needed by ClimbTower_Award are mirrored here.
[ResourceEntity("dlc/climb_tower/climbtower_season.json")]
public class DlcClimbTowerSeasonExcel : ExcelResource
{
    [JsonProperty("ID")] public uint ID { get; set; }
    [JsonProperty("OpenTime")] public string? OpenTimeRaw { get; set; }
    [JsonProperty("CloseTime")] public string? CloseTimeRaw { get; set; }
    [JsonProperty("Battles")] public List<uint> Battles { get; set; } = [];

    public override uint GetId() => ID;

    public override void Loaded() => GameData.ClimbTowerSeasonData[ID] = this;
}

// climbtower_battle.json: only Type/Level matter. Type 2 == ClimbTower.BattleType.Challenge, and
// ClimbTower.GetTotalStar (DLC/ClimbTower/ClimbTower.lua:329-342) counts stars from challenge battles
// only - normal battles contribute nothing to the milestone totals.
[ResourceEntity("dlc/climb_tower/climbtower_battle.json")]
public class DlcClimbTowerBattleExcel : ExcelResource
{
    [JsonProperty("ID")] public uint ID { get; set; }
    [JsonProperty("Type")] public JToken? TypeRaw { get; set; }
    [JsonProperty("Level")] public JToken? LevelRaw { get; set; }

    [JsonIgnore] public uint Type => ReadUInt(TypeRaw);
    [JsonIgnore] public uint Level => ReadUInt(LevelRaw);
    [JsonIgnore] public bool IsChallenge => Type == 2;

    public override uint GetId() => ID;

    public override void Loaded() => GameData.ClimbTowerBattleData[ID] = this;

    private static uint ReadUInt(JToken? token) => token?.Type switch
    {
        JTokenType.Integer => (uint)token.Value<long>(),
        JTokenType.Float => (uint)token.Value<double>(),
        JTokenType.String when uint.TryParse(token.Value<string>()?.Trim(), out var value) => value,
        _ => 0
    };
}

// climbtower_reward.json rows are flat 5 element reward lines ([g,d,p,l,count]) keyed by TargetNum, the
// star total needed to claim. The client reads the same table through ClimbTower.tbAward and shows
// TargetNum/Award locally, so the server only has to enforce the threshold and the one-shot flag.
[ResourceEntity("dlc/climb_tower/climbtower_reward.json")]
public class DlcClimbTowerRewardExcel : ExcelResource
{
    [JsonProperty("ID")] public uint ID { get; set; }
    [JsonProperty("TargetNum")] public JToken? TargetNumRaw { get; set; }
    [JsonProperty("Reward")] public JToken? RewardRaw { get; set; }

    [JsonIgnore] public uint TargetNum => ReadUInt(TargetNumRaw);

    /// <summary>Reward lines shaped as one [g,d,p,l,count] row; empty when the column is malformed.</summary>
    [JsonIgnore]
    public List<List<uint>> Rows => RewardRaw is JArray array && ReadRow(array) is { } row ? [row] : [];

    public override uint GetId() => ID;

    public override void Loaded() => GameData.ClimbTowerRewardData[ID] = this;

    private static List<uint>? ReadRow(JArray array)
    {
        if (array.Count < 5)
            return null;

        var row = new List<uint>(array.Count);
        foreach (var cell in array)
        {
            var value = ReadUInt(cell);
            if (value == 0 && cell?.Type is not JTokenType.Integer and not JTokenType.Float &&
                cell?.Type is not JTokenType.String)
                return null;

            row.Add(value);
        }

        return row[^1] == 0 ? null : row;
    }

    private static uint ReadUInt(JToken? token) => token?.Type switch
    {
        JTokenType.Integer => (uint)token.Value<long>(),
        JTokenType.Float => (uint)token.Value<double>(),
        JTokenType.String when uint.TryParse(token.Value<string>()?.Trim(), out var value) => value,
        _ => 0
    };
}
