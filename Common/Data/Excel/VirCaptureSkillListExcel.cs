using MikuSB.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// dlc/vircapture/skilllist.json - the skill catalogue a monster card can roll.
/// Group lists which skill slots the skill may occupy (skillpool RandomIdN), MutexID keeps two
/// variants of the same skill off one card, MaxLevel caps the upgrade (skillcost Level1..Level5)
/// and CostId selects the skillcost row.
/// </summary>
[ResourceEntity("dlc/vircapture/skilllist.json")]
public class VirCaptureSkillListExcel : ExcelResource
{
    [JsonProperty("Id")] public uint Id { get; set; }
    [JsonProperty("BuffId")] public uint BuffId { get; set; }
    [JsonProperty("Type")] public uint Type { get; set; }
    [JsonProperty("Color")] public uint Color { get; set; }
    [JsonProperty("CostId")] public uint CostId { get; set; }
    [JsonProperty("MaxLevel")] public uint MaxLevel { get; set; }
    [JsonProperty("MutexID")] public uint MutexId { get; set; }
    [JsonProperty("Group")] public List<uint> Group { get; set; } = [];
    [JsonProperty("DescValue")] public JToken? DescValueRaw { get; set; }

    public override uint GetId() => Id;

    public bool FitsSlot(int slot) => Group.Count == 0 || Group.Contains((uint)slot);

    public override void Loaded()
    {
        GameData.VirCaptureSkillListData[Id] = this;
    }
}
