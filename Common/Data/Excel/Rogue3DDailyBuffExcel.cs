using Newtonsoft.Json;

namespace MikuSB.Data.Excel;

[ResourceEntity("dlc/rogue3d/dailybuff.json")]
public class Rogue3DDailyBuffExcel : ExcelResource
{
    [JsonProperty("ID")] public uint Id { get; set; }
    [JsonProperty("GroupID")] public uint GroupId { get; set; }
    [JsonProperty("ScoreBuffID")] public uint ScoreBuffId { get; set; }

    public override uint GetId() => Id;

    public override void Loaded()
    {
        GameData.Rogue3DDailyBuffData[Id] = this;
    }
}
