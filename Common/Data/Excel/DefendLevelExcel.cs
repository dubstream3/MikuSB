using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// 荣誉冲突 (Defend) level table (challenge/defend/level.json). The Target array lists the target ids
/// of one level in display order; the index inside that array selects the claim bit the client checks
/// in GetTargetState (attribute sid AwardsGot / AwardsGot_2), so the server needs the same ordering.
/// The table is keyed by (LevelId, Diff).
/// </summary>
[ResourceEntity("challenge/defend/level.json")]
public class DefendLevelExcel : ExcelResource
{
    [JsonProperty("LevelId")] public uint LevelId { get; set; }
    [JsonProperty("Diff")] public uint Diff { get; set; }

    /// <summary>Target ids in display order.</summary>
    [JsonProperty("Target")] public JToken? Target { get; set; }

    /// <summary>Reward rows: [[genre, detail, particular, level, count], ...].</summary>
    [JsonProperty("ShowReward")] public JToken? ShowReward { get; set; }

    public override uint GetId() => LevelId * 100 + Diff;

    public override void Loaded()
    {
        GameData.DefendLevelData[GetId()] = this;
    }
}
