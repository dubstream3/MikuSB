using Newtonsoft.Json;

namespace MikuSB.Data.Excel;

[ResourceEntity("item/support/fixed.json")]
public class SupportFixedExcel : ExcelResource
{
    [JsonProperty("ID")] public int Id { get; set; }
    public int Num { get; set; }
    public List<uint> Item { get; set; } = [];

    public override uint GetId() => (uint)Id;

    public override void Loaded()
    {
        GameData.SupportFixedData[Id] = this;
    }
}
