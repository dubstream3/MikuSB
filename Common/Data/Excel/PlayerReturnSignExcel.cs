using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// activity/player_return/return_sign.json — the 回归活动 sign-in reward table, one row per
/// (ActId, DayId) pair, read by the client as activity/player_return/return_sign.txt
/// (Activity/PlayerReturn.lua:53). Act 560 carries seven rows (DayId 1..7, Special on 1/3/7).
///
/// SignReward is a list of GDPL+count rows, the shape Item.Gain and
/// RewardManager.GrantConfiguredRewardsAsync both take. The client decides which day is due from
/// the Sign attribute bits (PlayerReturn.GetSignDay: bits 0..7 = days signed, bits 8..15 = days
/// already claimed), so the server pays the row for claimedDays + 1.
/// </summary>
[ResourceEntity("activity/player_return/return_sign.json")]
public class PlayerReturnSignExcel : ExcelResource
{
    [JsonProperty("ActId")] public JToken? ActIdRaw { get; set; }
    [JsonProperty("DayId")] public JToken? DayIdRaw { get; set; }
    [JsonProperty("Special")] public JToken? SpecialRaw { get; set; }
    [JsonProperty("SignReward")] public JToken? SignReward { get; set; }

    [JsonIgnore] public uint ActId => ReadUint(ActIdRaw);
    [JsonIgnore] public uint DayId => ReadUint(DayIdRaw);

    /// <summary>The CSV marks the bonus days with 1 and leaves the rest empty.</summary>
    [JsonIgnore] public bool Special => ReadUint(SpecialRaw) == 1;

    public override uint GetId() => ActId * 1000 + DayId;

    public override void Loaded()
    {
        if (ActId != 0 && DayId != 0)
            GameData.PlayerReturnSignData[(ActId, DayId)] = this;
    }

    private static uint ReadUint(JToken? token)
    {
        var raw = token?.ToString().Trim().Trim('[', ']', '"') ?? string.Empty;
        return uint.TryParse(raw, out var value) ? value : 0;
    }
}
