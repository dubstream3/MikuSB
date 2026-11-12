using MikuSB.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// dlc/vircapture/chippool.json - chip affix roll table referenced by ChipRandomId.
/// RandomId1..3 belong to the three chip slots and each column maps a chip id to its weight
/// (chip ids come from chiplist.json). A chip's displayed level is derived from how many copies the
/// formation owns (VirCaptureConfig:GetChipLevel), not from the card, so the affix level stays 1.
/// </summary>
[ResourceEntity("dlc/vircapture/chippool.json")]
public class VirCaptureChipPoolExcel : ExcelResource
{
    [JsonProperty("Id")] public uint Id { get; set; }
    [JsonProperty("RandomId1")] public JToken? RandomId1Raw { get; set; }
    [JsonProperty("RandomId2")] public JToken? RandomId2Raw { get; set; }
    [JsonProperty("RandomId3")] public JToken? RandomId3Raw { get; set; }

    [JsonIgnore] public (string Key, uint Weight)[] Slots1 { get; private set; } = [];
    [JsonIgnore] public (string Key, uint Weight)[] Slots2 { get; private set; } = [];
    [JsonIgnore] public (string Key, uint Weight)[] Slots3 { get; private set; } = [];

    public override uint GetId() => Id;

    public (string Key, uint Weight)[] Slot(int index) => index switch
    {
        1 => Slots1,
        2 => Slots2,
        3 => Slots3,
        _ => []
    };

    public override void Loaded()
    {
        Slots1 = VirCapturePoolParse.Weighted(RandomId1Raw);
        Slots2 = VirCapturePoolParse.Weighted(RandomId2Raw);
        Slots3 = VirCapturePoolParse.Weighted(RandomId3Raw);
        GameData.VirCaptureChipPoolData[Id] = this;
    }
}
