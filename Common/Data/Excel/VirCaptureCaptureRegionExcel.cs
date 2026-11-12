using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

[ResourceEntity("dlc/vircapture/captureregion.json")]
public class VirCaptureCaptureRegionExcel : ExcelResource
{
    [JsonProperty("Id")] public uint Id { get; set; }
    [JsonProperty("StartTime")] public string StartTime { get; set; } = "";
    [JsonProperty("EndTime")] public string EndTime { get; set; } = "";
    [JsonProperty("MapId")] public uint MapId { get; set; }
    [JsonProperty("LevelRegionName")] public string LevelRegionName { get; set; } = "";
    /// <summary>Region unlock conditions, either {"20":15} or [[20,15],[23,5810,1102,1104]].</summary>
    [JsonProperty("Condition")] public JToken? Condition { get; set; }
    /// <summary>Day/night switch conditions (empty on maps without a light switch).</summary>
    [JsonProperty("LightSwitchCondition")] public JToken? LightSwitchCondition { get; set; }
    /// <summary>[questPoolId, maxRandomQuest, ...] for maps that host build-island missions.</summary>
    [JsonProperty("RandomQuestMax")] public JToken? RandomQuestMax { get; set; }
    /// <summary>Maximum island level (only present on map 326).</summary>
    [JsonProperty("Islandlevel")] public uint IslandLevel { get; set; }

    /// <summary>Quest pool id (RandomQuestMax[1]), 0 when the map has no random missions.</summary>
    public uint QuestPoolId => ReadInt(RandomQuestMax, 0);

    /// <summary>Client uses RandomQuestMax[2] + 1 as its slot count, the first slot is reserved for cost missions.</summary>
    public uint MaxRandomQuest => ReadInt(RandomQuestMax, 1) + 1;

    private static uint ReadInt(JToken? token, int index)
    {
        if (token is not JArray array || array.Count <= index)
            return 0;

        var value = array[index];
        return value.Type == JTokenType.Integer || value.Type == JTokenType.Float
            ? (uint)Math.Max(0, value.Value<long>())
            : 0;
    }

    public override uint GetId() => Id;

    public override void Loaded()
    {
        GameData.VirCaptureCaptureRegionData[Id] = this;
    }
}
