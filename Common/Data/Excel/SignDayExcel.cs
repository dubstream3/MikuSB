using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// activity/sign/signday.json — the monthly sign-in ("signin_monthly") reward table: one row per
/// (ActivityId, DayId) pair, DayId running 1..31 for ActivityId 1.
///
/// SignReward is already a list of GDPL+count rows ([[genre, detail, particular, level, count]]),
/// i.e. exactly the shape the client hands to Item.Gain (Activity/Sign.lua:435 ->
/// Item/Item.lua:639 -> UI.Open("GainItem", tbItems)), so the server can push it straight back
/// through the DaySign callback without any translation.
///
/// The client reads the same table from its own copy (activity/sign/signday.txt,
/// Sign.LoadSignDaysConfig at Activity/Sign.lua:32) to work out whether a sign-in is still
/// possible (Sign.CheckEnd), so the server has to grant precisely these rows.
/// </summary>
[ResourceEntity("activity/sign/signday.json")]
public class SignDayExcel : ExcelResource
{
    [JsonProperty("ActivityId")] public JToken? ActivityIdRaw { get; set; }
    [JsonProperty("DayId")] public JToken? DayIdRaw { get; set; }
    [JsonProperty("SignReward")] public JToken? SignReward { get; set; }

    [JsonIgnore] public uint ActivityId => ReadUint(ActivityIdRaw);
    [JsonIgnore] public uint DayId => ReadUint(DayIdRaw);

    public override uint GetId() => ActivityId * 1000 + DayId;

    public override void Loaded()
    {
        if (ActivityId != 0 && DayId != 0)
            GameData.SignDayData[(ActivityId, DayId)] = this;
    }

    private static uint ReadUint(JToken? token)
    {
        var raw = token?.ToString().Trim().Trim('[', ']', '"') ?? string.Empty;
        return uint.TryParse(raw, out var value) ? value : 0;
    }
}
