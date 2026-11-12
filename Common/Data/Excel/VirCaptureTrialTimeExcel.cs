using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// `dlc/vircapture/trial_timelist.json` - 试炼 period table (17 rows).
///
/// `TrialId` names the level row that runs in the period and `Condition` is the entry condition;
/// both are needed by the server to decide whether a trial is really running and who may enter
/// (the client only reads the window fields today).
/// </summary>
[ResourceEntity("dlc/vircapture/trial_timelist.json")]
public class VirCaptureTrialTimeExcel : ExcelResource
{
    [JsonProperty("Id")] public uint Id { get; set; }

    [JsonProperty("StartTime")] public string StartTime { get; set; } = "";

    [JsonProperty("EndTime")] public string EndTime { get; set; } = "";

    [JsonProperty("AwardTime")] public string AwardTime { get; set; } = "";

    [JsonProperty("Condition")] public JToken? ConditionRaw { get; set; }

    /// <summary>`TrialId` is a single id in every shipped row; kept as a token so a list also loads.</summary>
    [JsonProperty("TrialId")] public JToken? TrialIdRaw { get; set; }

    [JsonIgnore]
    public List<uint> TrialIds { get; } = [];

    /// <summary>First trial level id of the period (0 when the column is empty).</summary>
    [JsonIgnore]
    public uint TrialId => TrialIds.Count > 0 ? TrialIds[0] : 0;

    public override uint GetId() => Id;

    public override void Loaded()
    {
        TrialIds.Clear();

        switch (TrialIdRaw)
        {
            case JArray array:
                foreach (var item in array)
                {
                    var id = VirCapturePoolParse.ToInt(item);
                    if (id > 0)
                        TrialIds.Add(id);
                }

                break;
            default:
                var single = VirCapturePoolParse.ToInt(TrialIdRaw);
                if (single > 0)
                    TrialIds.Add(single);
                break;
        }

        GameData.VirCaptureTrialTimeData[Id] = this;
    }
}
