using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// `dlc/vircapture/trial.json` - 试炼 level table (16 rows).
///
/// Implements <see cref="ILevelRewardConfig"/> so the shared RewardManager can resolve
/// BaseDropID / FirstDropID / RandomDropID through ExtraData exactly like the DLC chapters do.
///
/// `Score` is [[metric, [threshold, score], [threshold, score], ...]]: the leading number is the
/// metric id, every following pair is a tier. The client's GetMaxScore sums the last tier of every
/// group (VirCaptureTrial.lua:207-224), so the server uses the same tiers and reports the tier the
/// run reached; the metric id is logged, not silently guessed (see VirCaptureTrial_LevelSettlement).
/// </summary>
[ResourceEntity("dlc/vircapture/trial.json")]
public class VirCaptureTrialExcel : ExcelResource, ILevelRewardConfig
{
    public readonly record struct ScoreTier(uint Threshold, uint Score);

    [JsonProperty("ID")] public uint Id { get; set; }

    [JsonProperty("MapID")] public uint MapId { get; set; }

    [JsonProperty("FightID")] public uint FightId { get; set; }

    [JsonProperty("Monster")] public List<uint> Monsters { get; set; } = [];

    [JsonProperty("LevelStrength")] public uint LevelStrength { get; set; }

    [JsonProperty("LevelPower")] public uint LevelPower { get; set; }

    [JsonProperty("Condition")] public JToken? ConditionRaw { get; set; }

    [JsonProperty("Score")] public JToken? ScoreRaw { get; set; }

    [JsonProperty("TrialCard")] public List<uint> TrialCards { get; set; } = [];

    /// <summary>Drop / reward columns (BaseDropID, FirstDropID, RandomDropID, ...).</summary>
    [JsonExtensionData]
    public IDictionary<string, JToken> ExtraData { get; set; } = new Dictionary<string, JToken>();

    uint ILevelRewardConfig.ID => Id;

    /// <summary>Metric id of the first score group (2 in every shipped row).</summary>
    [JsonIgnore]
    public uint ScoreMetric { get; private set; }

    /// <summary>Score tiers sorted by threshold; the max score is the last tier of each group.</summary>
    [JsonIgnore]
    public List<ScoreTier> ScoreTiers { get; } = [];

    [JsonIgnore]
    public uint MaxScore { get; private set; }

    public override uint GetId() => Id;

    public override void Loaded()
    {
        ScoreTiers.Clear();
        MaxScore = 0;

        if (ScoreRaw is JArray groups)
        {
            foreach (var group in groups)
            {
                if (group is not JArray cells || cells.Count < 2)
                    continue;

                ScoreMetric = VirCapturePoolParse.ToInt(cells[0]);

                var tiers = new List<ScoreTier>();
                for (var i = 1; i < cells.Count; i++)
                {
                    if (cells[i] is not JArray pair || pair.Count < 2)
                        continue;

                    var threshold = VirCapturePoolParse.ToInt(pair[0]);
                    var score = VirCapturePoolParse.ToInt(pair[1]);
                    if (score == 0)
                        continue;

                    tiers.Add(new ScoreTier(threshold, score));
                }

                if (tiers.Count == 0)
                    continue;

                tiers.Sort((a, b) => a.Threshold.CompareTo(b.Threshold));
                ScoreTiers.AddRange(tiers);
                MaxScore += tiers[^1].Score;
            }
        }

        GameData.VirCaptureTrialData[Id] = this;
    }

    /// <summary>
    /// Score for one run: the highest tier whose threshold the metric reached. Tiers are cumulative
    /// per group in the shipped data (a single group), so a linear walk is enough.
    /// </summary>
    public uint ResolveScore(uint metricValue)
    {
        uint score = 0;
        foreach (var tier in ScoreTiers)
        {
            if (metricValue >= tier.Threshold)
                score = tier.Score;
        }

        return score == 0 ? 0u : Math.Min(score, MaxScore);
    }
}
