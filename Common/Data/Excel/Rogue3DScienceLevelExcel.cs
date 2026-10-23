using Newtonsoft.Json;

namespace MikuSB.Data.Excel;

[ResourceEntity("dlc/rogue3d/server_06_sciencelevel.json")]
public class Rogue3DScienceLevelExcel : ExcelResource
{
    [JsonProperty("ID")] public uint Id { get; set; }
    [JsonProperty("Level")] public uint Level { get; set; }
    [JsonProperty("Cost")] public List<uint> Cost { get; set; } = [];

    public override uint GetId() => Id;

    public override void Loaded()
    {
        GameData.Rogue3DScienceLevelData[Id] = this;
    }
}
