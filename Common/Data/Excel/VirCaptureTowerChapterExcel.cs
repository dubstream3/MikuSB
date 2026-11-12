using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// `dlc/vircapture/tower_chapter.json` - one row per chapter of one difficulty.
///
/// `StarAward` ships as a dictionary keyed by the required star count
/// ({"5": [rows], "10": [rows], "20": [rows]}), while the client walks it with ipairs and reads
/// v[1] = required stars / v[2] = reward rows (VirCaptureTower.lua:321-410). The reward flag at
/// (128, 141 + chapterId - 1) stores one bit per *index*, so both sides have to agree on the
/// ordering: the loader sorts the tiers by required stars and the handlers use the same 1-based
/// index.
/// </summary>
[ResourceEntity("dlc/vircapture/tower_chapter.json")]
public class VirCaptureTowerChapterExcel : ExcelResource
{
    public readonly record struct StarTier(uint Need, List<IReadOnlyList<uint>> Rows);

    [JsonProperty("ID")] public uint Id { get; set; }

    [JsonProperty("Name")] public string Name { get; set; } = "";

    [JsonProperty("Difficult")] public uint Difficult { get; set; }

    [JsonProperty("Type")] public uint Type { get; set; }

    [JsonProperty("Condition")] public JToken? ConditionRaw { get; set; }

    [JsonProperty("Level")] public List<uint> Levels { get; set; } = [];

    [JsonProperty("StarAward")] public JToken? StarAwardRaw { get; set; }

    /// <summary>Star tiers ordered by the required star count; index n-1 is claim bit n-1.</summary>
    [JsonIgnore]
    public List<StarTier> StarAwards { get; } = [];

    public override uint GetId() => Id;

    public override void Loaded()
    {
        StarAwards.Clear();

        if (StarAwardRaw is JObject byStar)
        {
            foreach (var property in byStar.Properties())
            {
                if (!uint.TryParse(property.Name, out var need) || need == 0)
                    continue;

                StarAwards.Add(new StarTier(need, ReadRows(property.Value)));
            }
        }
        else if (StarAwardRaw is JArray pairs)
        {
            // Array form: [[need, [rows...]], ...]
            foreach (var pair in pairs)
            {
                if (pair is not JArray entry || entry.Count < 2)
                    continue;

                var need = VirCapturePoolParse.ToInt(entry[0]);
                if (need == 0)
                    continue;

                StarAwards.Add(new StarTier(need, ReadRows(entry[1])));
            }
        }

        StarAwards.Sort((a, b) => a.Need.CompareTo(b.Need));

        if (!GameData.VirCaptureTowerChapterData.TryGetValue(Difficult, out var chapters))
            GameData.VirCaptureTowerChapterData[Difficult] = chapters = [];

        chapters[Id] = this;
    }

    public static List<IReadOnlyList<uint>> ReadRows(JToken? raw)
    {
        var rows = new List<IReadOnlyList<uint>>();
        if (raw is not JArray array)
            return rows;

        foreach (var row in array)
        {
            if (row is not JArray cells || cells.Count < 5)
                continue;

            var parsed = new List<uint>(cells.Count);
            foreach (var cell in cells)
                parsed.Add(VirCapturePoolParse.ToInt(cell));

            rows.Add(parsed);
        }

        return rows;
    }

    /// <summary>Claim flag attribute id for this chapter's star tiers.</summary>
    public uint ClaimFlagSid => AttrIds.VirCapture.TowerStarNormalFlagStartSid + Id - 1;
}
