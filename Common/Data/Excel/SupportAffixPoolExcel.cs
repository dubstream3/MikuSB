using Newtonsoft.Json;

namespace MikuSB.Data.Excel;

[ResourceEntity("item/support/affix_pool.json")]
public class SupportAffixPoolExcel : ExcelResource
{
    [JsonProperty("ID")] public int Id { get; set; }
    public List<int> AffixGroup1 { get; set; } = [];
    public int Weight1 { get; set; }
    public List<int> AffixGroup2 { get; set; } = [];
    public int Weight2 { get; set; }
    public List<int> AffixGroup3 { get; set; } = [];
    public int Weight3 { get; set; }
    public List<int> AffixGroup4 { get; set; } = [];
    public int Weight4 { get; set; }

    public IEnumerable<(IReadOnlyList<int> Affixs, int Weight)> Groups
    {
        get
        {
            if (AffixGroup1.Count > 0 && Weight1 > 0) yield return (AffixGroup1, Weight1);
            if (AffixGroup2.Count > 0 && Weight2 > 0) yield return (AffixGroup2, Weight2);
            if (AffixGroup3.Count > 0 && Weight3 > 0) yield return (AffixGroup3, Weight3);
            if (AffixGroup4.Count > 0 && Weight4 > 0) yield return (AffixGroup4, Weight4);
        }
    }

    public override uint GetId() => (uint)Id;

    public override void Loaded()
    {
        GameData.SupportAffixPoolData[Id] = this;
    }
}
