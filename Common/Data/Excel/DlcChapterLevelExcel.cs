using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

// The DLC story chapters (the "自往昔归还" story line reached from Dlc35Main) keep their own level
// table in dlc/dlc1/chapter/level.json, which the generic chapter loader does not read. Implementing
// ILevelRewardConfig lets RewardManager.GrantLevelRewardsAsync resolve the FirstDropID/BaseDropID
// drop tables plus the player/role experience for these levels without any extra plumbing.
[ResourceEntity("dlc/dlc1/chapter/level.json")]
public class DlcChapterLevelExcel : ExcelResource, ILevelRewardConfig
{
    public uint ID { get; set; }
    [JsonExtensionData] public IDictionary<string, JToken> ExtraData { get; set; } = new Dictionary<string, JToken>();

    public override uint GetId() => ID;

    public override void Loaded()
    {
        GameData.DlcChapterLevelData[ID] = this;
    }
}

// dlc/dlc1/chapter/chapter.json carries the per chapter star award table consumed by
// DLC_Chapter_STAR_AWARD. StarAward is either an empty string or a list of [G,D,P,L,Count] rows.
[ResourceEntity("dlc/dlc1/chapter/chapter.json")]
public class DlcChapterExcel : ExcelResource
{
    public uint ID { get; set; }
    [JsonProperty("StarAward")] public JToken? StarAwardRaw { get; set; }

    public override uint GetId() => ID;

    public override void Loaded()
    {
        GameData.DlcChapterData[ID] = this;
    }
}
