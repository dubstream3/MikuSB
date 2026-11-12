using MikuSB.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// dlc/vircapture/skillcost.json - the price of upgrading a skill affix from level N to N+1.
/// The client reads Level{N} for a card whose affix level is N (uw_vircapture_skillitem.lua:225-237)
/// and shows nothing when the row is missing, which is exactly the "already max level" state.
/// Each row is [itemType, detail, particular, level, count].
/// </summary>
[ResourceEntity("dlc/vircapture/skillcost.json")]
public class VirCaptureSkillCostExcel : ExcelResource
{
    [JsonProperty("Id")] public uint Id { get; set; }
    [JsonProperty("Level1")] public JToken? Level1Raw { get; set; }
    [JsonProperty("Level2")] public JToken? Level2Raw { get; set; }
    [JsonProperty("Level3")] public JToken? Level3Raw { get; set; }
    [JsonProperty("Level4")] public JToken? Level4Raw { get; set; }
    [JsonProperty("Level5")] public JToken? Level5Raw { get; set; }

    [JsonIgnore] public List<uint[]> Level1 { get; private set; } = [];
    [JsonIgnore] public List<uint[]> Level2 { get; private set; } = [];
    [JsonIgnore] public List<uint[]> Level3 { get; private set; } = [];
    [JsonIgnore] public List<uint[]> Level4 { get; private set; } = [];
    [JsonIgnore] public List<uint[]> Level5 { get; private set; } = [];

    public override uint GetId() => Id;

    /// <summary>Cost rows for upgrading an affix currently at <paramref name="level"/>.</summary>
    public List<uint[]> CostFor(uint level) => level switch
    {
        1 => Level1,
        2 => Level2,
        3 => Level3,
        4 => Level4,
        5 => Level5,
        _ => []
    };

    public override void Loaded()
    {
        Level1 = Parse(Level1Raw);
        Level2 = Parse(Level2Raw);
        Level3 = Parse(Level3Raw);
        Level4 = Parse(Level4Raw);
        Level5 = Parse(Level5Raw);
        GameData.VirCaptureSkillCostData[Id] = this;
    }

    private static List<uint[]> Parse(JToken? token)
    {
        var rows = new List<uint[]>();
        if (token is not JArray array)
            return rows;

        foreach (var row in array)
        {
            if (row is not JArray cells || cells.Count < 5)
                continue;

            var values = new uint[5];
            for (var i = 0; i < 5; i++)
                values[i] = cells[i].Type == JTokenType.Integer ? cells[i].Value<uint>() : 0u;
            rows.Add(values);
        }

        return rows;
    }
}
