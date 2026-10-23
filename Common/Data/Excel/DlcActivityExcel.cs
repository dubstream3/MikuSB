using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

[ResourceEntity("dlc/dlc_activities.json")]
public class DlcActivityExcel : ExcelResource
{
    [JsonProperty("ID")] public uint Id { get; set; }
    [JsonProperty("StartTime")] public string StartTime { get; set; } = "";
    [JsonProperty("EndTime")] public string EndTime { get; set; } = "";
    [JsonProperty("EnterStartTime")] public string EnterStartTime { get; set; } = "";
    [JsonProperty("CloseEndTime")] public string CloseEndTime { get; set; } = "";
    [JsonProperty("Condition")] public string Condition { get; set; } = "";
    [JsonProperty("MainUI")] public string MainUI { get; set; } = "";

    /// <summary>
    /// Raw "<c>{Refresh=35,Defense=35,Dorm=3,Mow=4,Rush=105,...}</c>" map from the activity row. It
    /// carries the per feature config id the client uses to look the sub feature up in its own tables
    /// (chapter / defend_act / online), which is what the server needs to push time overrides for.
    /// </summary>
    [JsonProperty("DlcFuncConfId")] public string DlcFuncConfId { get; set; } = "";

    /// <summary>
    /// Activity group ids of the DLC's 先行试用 roster (DLC 35 -> [561, 576]). DLC_Logic.lua:72 turns
    /// the field into tbDlcFuncConfId.Try, and the try page lists every live row of those groups, so the
    /// server reads it to know which acts belong to the pinned era.
    ///
    /// Kept as a JToken on purpose: the shipped table writes it as a JSON array ("TryID": [ 561, 576 ]),
    /// and typing it as a string makes the deserializer throw - which ResourceManager answers by
    /// dropping the WHOLE file, leaving GameData.DlcActivityData empty and every pinned-era lookup
    /// (ActivationFloor, the Rush/Festival/SeasonBoss window pushes) silently falling back to "no row".
    /// </summary>
    [JsonProperty("TryID")] public JToken? TryID { get; set; }

    [JsonProperty("ShopID")] public JToken? ShopID { get; set; }
    [JsonProperty("UnlockChapter")] public JToken? UnlockChapter { get; set; }

    /// <summary>
    /// Mission board group ids of the activity (DLC 35 -&gt; [145..152]). The client builds the board
    /// from the local dlc_mission rows, so the server only needs the groups to know which missions
    /// belong to the pinned era when it fills the day/week activation slots.
    ///
    /// JToken on purpose, same as TryID/ShopID: the shipped table writes arrays.
    /// </summary>
    [JsonProperty("MissionID")] public JToken? MissionId { get; set; }

    [JsonIgnore]
    public List<uint> MissionGroups
    {
        get
        {
            var groups = new List<uint>();
            if (MissionId is JArray array)
            {
                foreach (var item in array)
                {
                    var value = ToUint(item);
                    if (value != 0)
                        groups.Add(value);
                }
            }

            return groups;
        }
    }

    private static uint ToUint(JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null)
            return 0;

        var raw = token.ToString().Trim().Trim('[', ']', '"');
        return uint.TryParse(raw, out var value) ? value : 0;
    }

    public override uint GetId() => Id;

    public override void Loaded()
    {
        GameData.DlcActivityData[Id] = this;
    }
}
