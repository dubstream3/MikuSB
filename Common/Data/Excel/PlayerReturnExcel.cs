using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// activity/player_return/return.json — the 回归活动 (player return) schedule table, one row per
/// ActId, read by the client as activity/player_return/return.txt (Activity/PlayerReturn.lua:22
/// LoadCsv inside PlayerReturn.Init).
///
/// The row supplies the reward window length: the client computes
/// endTime = startTime + (nActiveDay + protectAdd) * 86400 (PlayerReturn.lua:78 GetTimeConf), where
/// startTime and protectAdd are attributes the server owns and nActiveDay is this row's ActiveDay.
/// IntervalDay is the offline gap that triggers the activity (42 days here), FirstOfflineDay the
/// shorter gap used for the first trigger and OfflineProtectionDay the extra protection days.
///
/// ActId is also the activities.txt row id in this version (return.txt lists ... 405, 560, 604 and
/// activities.txt carries id 560 with Class "player_return"), which is why the same number can be
/// handed to Activity.GetActivityConfig for the help text.
/// </summary>
[ResourceEntity("activity/player_return/return.json")]
public class PlayerReturnExcel : ExcelResource
{
    [JsonProperty("ActId")] public JToken? ActIdRaw { get; set; }
    [JsonProperty("IntervalDay")] public JToken? IntervalDayRaw { get; set; }
    [JsonProperty("FirstOfflineDay")] public JToken? FirstOfflineDayRaw { get; set; }
    [JsonProperty("OfflineDay")] public JToken? OfflineDayRaw { get; set; }
    [JsonProperty("Condition")] public JToken? Condition { get; set; }
    [JsonProperty("OfflineProtectionDay")] public JToken? OfflineProtectionDayRaw { get; set; }
    [JsonProperty("ActiveDay")] public JToken? ActiveDayRaw { get; set; }

    [JsonIgnore] public uint ActId => ReadUint(ActIdRaw);
    [JsonIgnore] public uint IntervalDay => ReadUint(IntervalDayRaw);
    [JsonIgnore] public uint FirstOfflineDay => ReadUint(FirstOfflineDayRaw);
    [JsonIgnore] public uint OfflineDay => ReadUint(OfflineDayRaw);

    /// <summary>Extra days added to ActiveDay; empty for act 560, so it reads as 0.</summary>
    [JsonIgnore] public uint OfflineProtectionDay => ReadUint(OfflineProtectionDayRaw);

    /// <summary>Reward window length in days, the same number the client reads from its CSV.</summary>
    [JsonIgnore] public uint ActiveDay => ReadUint(ActiveDayRaw);

    public override uint GetId() => ActId;

    public override void Loaded()
    {
        if (ActId != 0)
            GameData.PlayerReturnData[ActId] = this;
    }

    private static uint ReadUint(JToken? token)
    {
        var raw = token?.ToString().Trim().Trim('[', ']', '"') ?? string.Empty;
        return uint.TryParse(raw, out var value) ? value : 0;
    }
}
