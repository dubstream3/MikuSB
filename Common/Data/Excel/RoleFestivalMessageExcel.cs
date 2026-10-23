using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// 心愿寄语 (Dorm / RoleFestival) activity rows from house/MessageActivity/ActivityTime.json. The
/// window is pushed at runtime through GMRoleFestivalSetId, but the act id itself lives in the player
/// attribute (gid 72, sid 1) and RoleFestivalMessage.GetIsInActTime() is literally "GetActId() ~= 0",
/// so the row also has to be known here to seed that attribute at login.
/// </summary>
[ResourceEntity("house/MessageActivity/ActivityTime.json")]
public class RoleFestivalMessageExcel : ExcelResource
{
    [JsonProperty("ActivityID")] public JToken? ActivityID { get; set; }
    [JsonProperty("StartTime")] public JToken? StartTime { get; set; }
    [JsonProperty("EndTime")] public JToken? EndTime { get; set; }
    [JsonProperty("IntroPic")] public JToken? IntroPic { get; set; }
    [JsonProperty("IntroName")] public string? IntroName { get; set; }
    [JsonProperty("UnlockNum")] public JToken? UnlockNum { get; set; }

    public override uint GetId() => 0;

    public override void Loaded()
    {
        // Local tolerant read: the shipped tables mix numbers and strings ("3", "[3]", ""), and
        // JToken.ToObject<uint>() throws a FormatException on the empty string.
        var raw = ActivityID?.ToString().Trim().Trim('[', ']', '"') ?? string.Empty;
        if (uint.TryParse(raw, out var actId) && actId != 0)
            GameData.RoleFestivalMessageData[actId] = this;
    }
}
