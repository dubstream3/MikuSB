using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

[ResourceEntity("item/support/affix.json")]
public class SupportAffixExcel : ExcelResource
{
    [JsonProperty("ID")] public int Id { get; set; }
    [JsonExtensionData] public IDictionary<string, JToken> ExtraData { get; set; } = new Dictionary<string, JToken>();

    public int TierCount =>
        ExtraData
            .Where(x => x.Key != "ID" && x.Key != "Sift" && x.Key != "Comment")
            .Select(x => x.Value)
            .OfType<JObject>()
            .Select(x => x.Count)
            .DefaultIfEmpty(0)
            .Max();

    public override uint GetId() => (uint)Id;

    public override void Loaded()
    {
        GameData.SupportAffixData[Id] = this;
    }
}
