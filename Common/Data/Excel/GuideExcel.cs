using MikuSB.Data.Config;
using Newtonsoft.Json;

namespace MikuSB.Data.Excel;

[ResourceEntity("guide/guide.json")]
public class GuideExcel : ExcelResource
{
    public uint ID { get; set; }
    [JsonConverter(typeof(StringToUIntConverter))] public uint Group { get; set; }

    public override uint GetId()
    {
        return (ID << 48) | (Group << 32);
    }

    public override void Loaded()
    {
        GameData.GuideData.TryAdd(GetId(), this);
    }
}
