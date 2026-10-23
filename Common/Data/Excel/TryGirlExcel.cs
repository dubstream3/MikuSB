using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// activity/gacha_try/try_girl.json — the 先行试用 roster, one row per try girl id, read by the client
/// as activity/gacha_try/try_girl.txt (Activity/GachaTry.lua LoadTryGirlConfig ->
/// GachaTry.GetTryGirlConf).
///
/// Girl is the [girl, skin, ...] pair the client uses to build the preview spine, LevelId the
/// gacha_try/level.txt row the trial fight runs on (134 -> 5133, map 5054) and Reward the one-shot
/// clear reward in the nested [[G,D,P,L,Count], ...] shape DlcRuntimeOverrides.ReadRewardRows wants.
/// The server pays Reward exactly once per (act, girlIdx) and remembers it in the same attribute bit
/// the client reads back through GachaTry.IsLevelPassed.
/// </summary>
[ResourceEntity("activity/gacha_try/try_girl.json")]
public class TryGirlExcel : ExcelResource
{
    [JsonProperty("ID")] public JToken? IdRaw { get; set; }
    [JsonProperty("Girl")] public JToken? Girl { get; set; }
    [JsonProperty("Reward")] public JToken? Reward { get; set; }
    [JsonProperty("TrialId")] public JToken? TrialIdRaw { get; set; }
    [JsonProperty("LevelId")] public JToken? LevelIdRaw { get; set; }
    [JsonProperty("Spine")] public JToken? Spine { get; set; }

    [JsonIgnore] public uint Id => ReadUint(IdRaw);
    [JsonIgnore] public uint TrialId => ReadUint(TrialIdRaw);
    [JsonIgnore] public uint LevelId => ReadUint(LevelIdRaw);

    public override uint GetId() => Id;

    public override void Loaded()
    {
        if (Id != 0)
            GameData.TryGirlData[Id] = this;
    }

    private static uint ReadUint(JToken? token)
    {
        var raw = token?.ToString().Trim().Trim('[', ']', '"') ?? string.Empty;
        return uint.TryParse(raw, out var value) ? value : 0;
    }
}
