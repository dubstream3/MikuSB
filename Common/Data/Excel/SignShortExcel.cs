using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// activity/sign/signshort.json - the "short sign-in" (短签) reward table used by every event that
/// runs a 7 day check-in card: one row per (ActivityId, DayId), DayId 1..7 for DLC era events and
/// up to 14 for the older 14 day cards (activity 9/15/17).
///
/// The client loads the very same file (Activity/Sign.lua:52 Sign.LoadShortSignReward) and keys it by
/// ActivityId -> DayId, then decides from it whether another day can be signed (Sign.CheckEnd
/// :230-254). DayId 7 rows carry Special=2 and a Param1 id (the "big" day reward, e.g. a furniture /
/// costume record for activity 603), so the 7th day is the one a player actually wants.
///
/// SignReward is already a list of [genre, detail, particular, level, count] rows, i.e. the shape
/// Item.Gain expects, so the server can echo it straight back through the ShortSign callback.
/// </summary>
[ResourceEntity("activity/sign/signshort.json")]
public class SignShortExcel : ExcelResource
{
    [JsonProperty("ActivityId")] public JToken? ActivityIdRaw { get; set; }
    [JsonProperty("DayId")] public JToken? DayIdRaw { get; set; }
    [JsonProperty("SignReward")] public JToken? SignReward { get; set; }
    [JsonProperty("Special")] public JToken? SpecialRaw { get; set; }
    [JsonProperty("Param1")] public JToken? Param1Raw { get; set; }
    [JsonProperty("Notice")] public JToken? Notice { get; set; }

    [JsonIgnore] public uint ActivityId => ReadUint(ActivityIdRaw);
    [JsonIgnore] public uint DayId => ReadUint(DayIdRaw);
    [JsonIgnore] public uint Special => ReadUint(SpecialRaw);
    [JsonIgnore] public uint Param1 => ReadUint(Param1Raw);

    public override uint GetId() => ActivityId * 1000 + DayId;

    public override void Loaded()
    {
        if (ActivityId != 0 && DayId != 0)
            GameData.SignShortData[(ActivityId, DayId)] = this;
    }

    private static uint ReadUint(JToken? token)
    {
        var raw = token?.ToString().Trim().Trim('[', ']', '"') ?? string.Empty;
        return uint.TryParse(raw, out var value) ? value : 0;
    }
}
