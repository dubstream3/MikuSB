using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

[ResourceEntity("challenge/role/level.json")]
public class RoleLevelExcel : ExcelResource, ILevelRewardConfig
{
    public uint ID { get; set; }
    [JsonExtensionData] public IDictionary<string, JToken> ExtraData { get; set; } = new Dictionary<string, JToken>();

    public override uint GetId() => ID;

    public override void Loaded()
    {
        GameData.RoleLevelData[ID] = this;
    }
}
