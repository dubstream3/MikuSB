using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// 荣誉冲突 (Defend) per level target rewards (challenge/defend/level_target.json). The client loads
/// the same rows as DefendLogic.tbTarget[Id] and gates them on the wave counter attribute
/// (GetTargetState: nType == 1 requires PassWave >= nWave), so Type maps to nType and Param to nWave.
/// </summary>
[ResourceEntity("challenge/defend/level_target.json")]
public class DefendTargetExcel : ExcelResource
{
    [JsonProperty("Id")] public uint Id { get; set; }
    [JsonProperty("Type")] public JToken? Type { get; set; }
    [JsonProperty("Param")] public JToken? Param { get; set; }

    /// <summary>Reward rows: [[genre, detail, particular, level, count], ...].</summary>
    [JsonProperty("Award")] public JToken? Award { get; set; }

    public override uint GetId() => Id;

    public override void Loaded()
    {
        GameData.DefendTargetData[Id] = this;
    }
}
