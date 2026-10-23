using Newtonsoft.Json;

namespace MikuSB.Data.Excel;

// prolevel.json: one row per character card GDPL holding the pro-level (职级) table:
// SkillID0..3 (team skill per stage), Condition1..3 (promotion requirements) and Award1..3
// (the claimable reward of each stage, [G,D,P,L,Count] rows).
// The client reads the same table as item/card/prolevel.txt and keys it by "g-d-p-l"
// (UMG/Role/RoleCard.lua:1241 LoadProLevelData), so TemplateId is the packed GDPL.
[ResourceEntity("item/card/prolevel.json")]
public class ProLevelExcel : ExcelResource
{
    [JsonProperty("GDPL")] public List<int> Gdpl { get; set; } = [];

    [JsonProperty("SkillID0")] public List<int> SkillID0 { get; set; } = [];
    [JsonProperty("SkillID1")] public List<int> SkillID1 { get; set; } = [];
    [JsonProperty("SkillID2")] public List<int> SkillID2 { get; set; } = [];
    [JsonProperty("SkillID3")] public List<int> SkillID3 { get; set; } = [];

    [JsonProperty("Condition1")] public List<List<int>> Condition1 { get; set; } = [];
    [JsonProperty("Condition2")] public List<List<int>> Condition2 { get; set; } = [];
    [JsonProperty("Condition3")] public List<List<int>> Condition3 { get; set; } = [];

    [JsonProperty("Award1")] public List<List<int>> Award1 { get; set; } = [];
    [JsonProperty("Award2")] public List<List<int>> Award2 { get; set; } = [];
    [JsonProperty("Award3")] public List<List<int>> Award3 { get; set; } = [];

    [JsonIgnore] public ulong TemplateId => Gdpl.Count >= 4
        ? GameResourceTemplateId.FromGdpl((uint)Gdpl[0], (uint)Gdpl[1], (uint)Gdpl[2], (uint)Gdpl[3])
        : 0;

    // The key is also stored as a GameData dictionary key, so it only has to be non-zero for the
    // resource manager's nested-folder probe; every lookup goes through TemplateId instead.
    public override uint GetId() => (uint)TemplateId;

    public List<int> GetSkillIds(int proLevel) => proLevel switch
    {
        1 => SkillID1,
        2 => SkillID2,
        3 => SkillID3,
        _ => SkillID0
    };

    public List<List<int>> GetCondition(int proLevel) => proLevel switch
    {
        1 => Condition1,
        2 => Condition2,
        3 => Condition3,
        _ => []
    };

    public List<List<int>> GetAward(int proLevel) => proLevel switch
    {
        1 => Award1,
        2 => Award2,
        3 => Award3,
        _ => []
    };

    public override void Loaded()
    {
        if (TemplateId != 0)
            GameData.ProLevelData[TemplateId] = this;
    }
}
