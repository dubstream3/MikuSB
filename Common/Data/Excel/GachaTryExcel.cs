using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// activity/gacha_try/gacha_try.json — 先行试用 / 角色试玩 (gacha try) act table, one row per ActId,
/// read by the client as activity/gacha_try/gacha_try.txt (Activity/GachaTry.lua LoadTryConfig ->
/// GachaTry.GetConfig).
///
/// TryGirl lists the try_girl.json ids the act offers; the client walks them through
/// GachaTry.GetTryGirlConf and the 1-based position in that list is the "girl index" every request
/// and every progress bit uses (GachaTry.GetLevelID, IsLevelPassed).
///
/// For the pinned DLC 35 era the only act whose own window sits inside the DLC window is 580
/// ([202601290400]..[202603110400], TryGirl [134]) — see GachaTryService.SelectActId.
/// </summary>
[ResourceEntity("activity/gacha_try/gacha_try.json")]
public class GachaTryExcel : ExcelResource
{
    [JsonProperty("ActId")] public JToken? ActIdRaw { get; set; }
    [JsonProperty("TryGirl")] public JToken? TryGirl { get; set; }
    [JsonProperty("Desc")] public JToken? Desc { get; set; }
    [JsonProperty("UI")] public JToken? UI { get; set; }

    [JsonIgnore] public uint ActId => ReadUint(ActIdRaw);

    public override uint GetId() => ActId;

    public override void Loaded()
    {
        if (ActId != 0)
            GameData.GachaTryData[ActId] = this;
    }

    /// <summary>try_girl.json ids of the act, in the client's girlIdx order (1-based).</summary>
    public List<uint> TryGirlIds => ReadUints(TryGirl);

    /// <summary>try_girl id at the client's 1-based <paramref name="girlIdx"/>, or 0 when out of range.</summary>
    public uint TryGirlAt(uint girlIdx)
    {
        var ids = TryGirlIds;
        return girlIdx >= 1 && girlIdx <= (uint)ids.Count ? ids[(int)girlIdx - 1] : 0u;
    }

    private static List<uint> ReadUints(JToken? token)
    {
        var values = new List<uint>();
        if (token is not JArray array)
            return values;

        foreach (var item in array)
        {
            var value = ReadUint(item);
            if (value != 0)
                values.Add(value);
        }

        return values;
    }

    private static uint ReadUint(JToken? token)
    {
        var raw = token?.ToString().Trim().Trim('[', ']', '"') ?? string.Empty;
        return uint.TryParse(raw, out var value) ? value : 0;
    }
}
