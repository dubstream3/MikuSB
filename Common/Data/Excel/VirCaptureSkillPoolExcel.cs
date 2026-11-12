using MikuSB.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// dlc/vircapture/skillpool.json - skill affix roll table referenced by SkillRandomId.
/// RandomId1..3 belong to the three skill slots; each column maps a skill level ("1" / "2") to its
/// weight and uses "0" for "no skill in this slot" (skillpool row 1 gives slot 2/3 a high 0 weight,
/// which is why most captured cards carry a single skill).
/// </summary>
[ResourceEntity("dlc/vircapture/skillpool.json")]
public class VirCaptureSkillPoolExcel : ExcelResource
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
        GameData.VirCaptureSkillPoolData[Id] = this;
    }
}
