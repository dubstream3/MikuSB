using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// 荣誉冲突 (Defend) activity periods (challenge/defend/defend_act.json). The client reads the same
/// table locally; the server only needs the act ids that belong to the pinned DLC so it can override
/// their expired windows through the DefendLogic_CheckOpenAct callback.
/// </summary>
[ResourceEntity("challenge/defend/defend_act.json")]
public class DefendActExcel : ExcelResource
{
    [JsonProperty("ID")] public uint Id { get; set; }
    [JsonProperty("DlcId")] public JToken? DlcId { get; set; }
    [JsonProperty("StartTime")] public JToken? StartTime { get; set; }
    [JsonProperty("EndTime")] public JToken? EndTime { get; set; }

    public override uint GetId() => Id;

    public override void Loaded()
    {
        GameData.DefendActData[Id] = this;
    }
}
