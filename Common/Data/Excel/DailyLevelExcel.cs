namespace MikuSB.Data.Excel;

[ResourceEntity("daily/level.json")]
public class DailyLevelExcel : ExcelResource, ILevelRewardConfig
{
    public uint ID { get; set; }
    [Newtonsoft.Json.JsonExtensionData] public IDictionary<string, Newtonsoft.Json.Linq.JToken> ExtraData { get; set; } = new Dictionary<string, Newtonsoft.Json.Linq.JToken>();

    public override uint GetId() => ID;

    public override void Loaded()
    {
        GameData.DailyLevelData.Add(ID, this);
    }
}
