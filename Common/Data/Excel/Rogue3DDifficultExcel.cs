using Newtonsoft.Json;

namespace MikuSB.Data.Excel;

[ResourceEntity("dlc/rogue3d/server_01_difficult.json")]
public class Rogue3DDifficultExcel : ExcelResource
{
    [JsonProperty("DifficultID")] public uint DifficultId { get; set; }
    [JsonProperty("GameplayGroup")] public List<uint> GameplayGroup { get; set; } = [];

    public override uint GetId() => DifficultId;

    public override void Loaded()
    {
        GameData.Rogue3DDifficultData[DifficultId] = this;
    }
}
