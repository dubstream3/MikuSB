using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

[ResourceEntity("dlc/rogue3d/server_02_science.json")]
public class Rogue3DScienceExcel : ExcelResource
{
    [JsonProperty("ID")] public uint Id { get; set; }
    [JsonProperty("UnlockCondition")] private JToken? UnlockConditionRaw { get; set; }
    [JsonProperty("MaxLevel")] public uint MaxLevel { get; set; }
    [JsonProperty("LevelList")] public List<uint> LevelList { get; set; } = [];

    [JsonIgnore]
    public IReadOnlyList<uint> UnlockCondition => ParseUnlockCondition(UnlockConditionRaw);

    public override uint GetId() => Id;

    public override void Loaded()
    {
        GameData.Rogue3DScienceData[Id] = this;
    }

    private static IReadOnlyList<uint> ParseUnlockCondition(JToken? raw)
    {
        if (raw is not JArray array)
            return [];

        return array
            .Select(value => value.Value<uint>())
            .Where(value => value > 0)
            .ToList();
    }
}
