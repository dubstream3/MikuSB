using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// `dlc/vircapture/tower.json` - 爬塔 level table (70 rows).
///
/// Implements <see cref="ILevelRewardConfig"/> so the shared RewardManager can resolve
/// FirstDropID / BaseDropID / RandomDropID through ExtraData (same contract the DLC chapters use),
/// which is what lets the tower settlement pay its first-clear and per-clear drops.
/// </summary>
[ResourceEntity("dlc/vircapture/tower.json")]
public class VirCaptureTowerExcel : ExcelResource, ILevelRewardConfig
{
    [JsonProperty("ID")] public uint Id { get; set; }

    [JsonProperty("Condition")] public JToken? ConditionRaw { get; set; }

    [JsonProperty("MapID")] public uint MapId { get; set; }

    [JsonProperty("TrialCard")] public List<uint> TrialCard { get; set; } = [];

    [JsonProperty("TaskPath")] public string TaskPath { get; set; } = "";

    /// <summary>Drop / reward columns (FirstDropID, BaseDropID, RandomDropID, ShowAward, ...).</summary>
    [JsonExtensionData]
    public IDictionary<string, JToken> ExtraData { get; set; } = new Dictionary<string, JToken>();

    uint ILevelRewardConfig.ID => Id;

    [JsonIgnore]
    public Dictionary<int, uint> Condition { get; } = [];

    public override uint GetId() => Id;

    public override void Loaded()
    {
        Condition.Clear();
        if (ConditionRaw is JObject obj)
        {
            foreach (var property in obj.Properties())
            {
                if (!int.TryParse(property.Name, out var key))
                    continue;

                uint value = 0;
                if (property.Value.Type == JTokenType.Integer)
                    value = property.Value.Value<uint>();
                else if (property.Value.Type == JTokenType.String &&
                         uint.TryParse(property.Value.Value<string>(), out var parsed))
                    value = parsed;

                if (value > 0)
                    Condition[key] = value;
            }
        }

        GameData.VirCaptureTowerData[Id] = this;
    }
}
