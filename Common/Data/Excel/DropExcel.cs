using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

[ResourceEntity("drop/drop.json")]
public class DropExcel : ExcelResource
{
    public uint ID { get; set; }
    public List<List<uint>> Drop { get; set; } = [];

    public override uint GetId() => ID;

    public override void Loaded()
    {
        GameData.DropData[ID] = this;
    }
}

[ResourceEntity("drop/drop_grop.json")]
public class DropGroupExcel : ExcelResource
{
    public uint ID { get; set; }
    [JsonProperty("RandType")] public JToken? RandTypeRaw { get; set; }
    [JsonProperty("Grop")] public JToken? GroupRaw { get; set; }

    [JsonIgnore]
    public int RandType => RandTypeRaw?.Type switch
    {
        JTokenType.Integer => RandTypeRaw.Value<int>(),
        JTokenType.String when int.TryParse(RandTypeRaw.Value<string>(), out var value) => value,
        _ => 0
    };

    public override uint GetId() => ID;

    public override void Loaded()
    {
        GameData.DropGroupData[ID] = this;
    }
}
