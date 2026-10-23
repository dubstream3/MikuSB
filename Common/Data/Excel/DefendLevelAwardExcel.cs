using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// 荣誉冲突 (Defend) battle pass style progression rewards (challenge/defend/levelaward_list.json).
/// The client keeps the same table locally as tbLevelAwardList[ActID][Index]; it claims entries whose
/// nNeedExp is covered by the current score attribute and answers under DefendLogic_GetLevelAward.
/// The primary key of the shipped table is the (ActID, Index) pair, so a composite id is used here.
/// </summary>
[ResourceEntity("challenge/defend/levelaward_list.json")]
public class DefendLevelAwardExcel : ExcelResource
{
    [JsonProperty("ActID")] public uint ActId { get; set; }
    [JsonProperty("Index")] public uint Index { get; set; }
    [JsonProperty("NeedExp")] public JToken? NeedExp { get; set; }

    /// <summary>One flat reward row: [genre, detail, particular, level, count].</summary>
    [JsonProperty("AwardList")] public JToken? AwardList { get; set; }

    public override uint GetId() => ActId * 100 + Index;

    public override void Loaded()
    {
        GameData.DefendLevelAwardData[GetId()] = this;
    }
}
