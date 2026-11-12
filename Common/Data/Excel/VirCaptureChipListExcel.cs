using MikuSB.Data;
using Newtonsoft.Json;

namespace MikuSB.Data.Excel;

/// <summary>
/// dlc/vircapture/chiplist.json - the chip catalogue a monster card can roll.
/// NumLevel maps "copies owned in the formation" to the chip level the UI shows, so nothing here is
/// stored on the card itself.
/// </summary>
[ResourceEntity("dlc/vircapture/chiplist.json")]
public class VirCaptureChipListExcel : ExcelResource
{
    [JsonProperty("Id")] public uint Id { get; set; }
    [JsonProperty("BuffId")] public uint BuffId { get; set; }
    [JsonProperty("Type")] public uint Type { get; set; }
    [JsonProperty("Color")] public uint Color { get; set; }
    [JsonProperty("Group")] public uint Group { get; set; }

    public override uint GetId() => Id;

    public override void Loaded()
    {
        GameData.VirCaptureChipListData[Id] = this;
    }
}
