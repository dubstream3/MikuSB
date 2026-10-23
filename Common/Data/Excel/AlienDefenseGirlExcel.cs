using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// 异星守护 (alien defense / Mow) support-girl roster from dlc/AlienDefense/aliendefense_girl.json.
///
/// Like the pal roster, SeasonID is the act id and the client keys the table as tbGirlConfig[nActId][nId]
/// (AlienDefenseConfig.lua:200-242), so the id alone is not unique across acts.
///
/// Condition is the girl's unlock requirement (same reader/evaluator as the pal roster);
/// SkillCondition1 / SkillCondition2 gate her two skills, whose unlocked state lives in the bits
/// bit2/bit4 of the same (143, 551 + Id) slot that carries the unlock bit in bit0.
///
/// Numeric columns go through JToken for the same reason as the pal roster: this table mixes shapes
/// (empty strings / missing columns), and one bad row would abort the whole array load.
/// </summary>
[ResourceEntity("dlc/AlienDefense/aliendefense_girl.json")]
public class AlienDefenseGirlExcel : ExcelResource
{
    [JsonProperty("SeasonID")] public JToken? SeasonIdRaw { get; set; }
    [JsonProperty("Id")] public JToken? IdRaw { get; set; }
    [JsonProperty("TemplateId")] public JToken? TemplateIdRaw { get; set; }
    [JsonProperty("Skill")] public JToken? SkillRaw { get; set; }
    [JsonProperty("Condition")] public JToken? ConditionRaw { get; set; }
    [JsonProperty("SkillCondition1")] public JToken? SkillCondition1Raw { get; set; }
    [JsonProperty("SkillCondition2")] public JToken? SkillCondition2Raw { get; set; }
    [JsonProperty("GirlUnlockDes")] public string? UnlockDescription { get; set; }
    [JsonProperty("GirlName")] public string? GirlName { get; set; }
    [JsonProperty("Icon")] public JToken? IconRaw { get; set; }

    [JsonIgnore] public uint SeasonId => ReadUint(SeasonIdRaw);
    [JsonIgnore] public uint Id => ReadUint(IdRaw);
    [JsonIgnore] public uint TemplateId => ReadUint(TemplateIdRaw);
    [JsonIgnore] public uint Skill => ReadUint(SkillRaw);
    [JsonIgnore] public uint Icon => ReadUint(IconRaw);

    [JsonIgnore] public IReadOnlyList<uint> Condition => AlienDefenseConditionReader.ReadList(ConditionRaw);
    [JsonIgnore] public IReadOnlyList<uint> SkillCondition1 => AlienDefenseConditionReader.ReadList(SkillCondition1Raw);
    [JsonIgnore] public IReadOnlyList<uint> SkillCondition2 => AlienDefenseConditionReader.ReadList(SkillCondition2Raw);

    public override uint GetId() => Id;

    public override void Loaded()
    {
        if (SeasonId == 0 || Id == 0)
            return;

        if (!GameData.AlienDefenseGirlData.TryGetValue(SeasonId, out var roster))
        {
            roster = [];
            GameData.AlienDefenseGirlData[SeasonId] = roster;
        }

        roster[Id] = this;
    }

    private static uint ReadUint(JToken? token)
    {
        var raw = token?.ToString().Trim().Trim('[', ']', '"') ?? string.Empty;
        return uint.TryParse(raw, out var value) ? value : 0;
    }
}
