using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// daily/daily.json — repeatable daily dungeon master table (9 rows).
/// Used by Daily_GetActivityInfo to answer with the limited-time windows the client expects in
/// Daily.tbCfg[nID].tbActivity (an array of {startTime, endTime, type} triples with type 3).
/// </summary>
[ResourceEntity("daily/daily.json")]
public class DailyExcel : ExcelResource
{
    [JsonProperty("ID")] public uint Id { get; set; }
    [JsonProperty("Activity")] public JToken? Activity { get; set; }

    public override uint GetId() => Id;

    public override void Loaded()
    {
        GameData.DailyData[Id] = this;
    }
}
