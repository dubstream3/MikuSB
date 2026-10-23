using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// 异星守护 (alien defense / Mow) score award rows from dlc/AlienDefense/alienaward.json. Act 4
/// carries 50 rows (NeedScore 3000..86500) gated by the cumulative endless score in attribute
/// (143, 2). Claimed bits live in (143, 3) for Index 1..30 and (143, 4) for Index 31..50, one bit
/// per index (AlienDefense.CheckScoreGot: bit nIdx, 0-based GetBits, so the first row owns bit 1
/// and bit 0 stays unused).
///
/// ScoreAward is the nested [ [G,D,P,L,Count], ... ] shape, so DlcRuntimeOverrides.ReadRewardRows
/// applies directly.
/// </summary>
[ResourceEntity("dlc/AlienDefense/alienaward.json")]
public class AlienDefenseAwardExcel : ExcelResource
{
    [JsonProperty("SeasonID")] public uint SeasonId { get; set; }
    [JsonProperty("Index")] public uint Index { get; set; }
    [JsonProperty("NeedScore")] public JToken? NeedScoreRaw { get; set; }
    [JsonProperty("ScoreAward")] public JToken? ScoreAward { get; set; }

    [JsonIgnore] public uint NeedScore => ReadUint(NeedScoreRaw);

    public override uint GetId() => SeasonId * 1000u + Index;

    public override void Loaded()
    {
        if (SeasonId == 0 || Index == 0)
            return;

        if (!GameData.AlienDefenseAwardData.TryGetValue(SeasonId, out var idxMap))
        {
            idxMap = [];
            GameData.AlienDefenseAwardData[SeasonId] = idxMap;
        }

        idxMap[Index] = this;
    }

    private static uint ReadUint(JToken? token)
    {
        var raw = token?.ToString().Trim().Trim('[', ']', '"') ?? string.Empty;
        return uint.TryParse(raw, out var value) ? value : 0;
    }
}
