using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

[ResourceEntity("chapter/level.json")]
public class ChapterLevelExcel : ExcelResource, ILevelRewardConfig
{
    public uint ID { get; set; }
    [JsonExtensionData] public IDictionary<string, JToken> ExtraData { get; set; } = new Dictionary<string, JToken>();

    public override uint GetId() => ID;

    public override void Loaded()
    {
        GameData.ChapterLevelData.Add(ID, this);
    }
}
