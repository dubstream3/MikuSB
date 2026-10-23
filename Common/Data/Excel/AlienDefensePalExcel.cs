using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// 异星守护 (alien defense / Mow) pal roster from dlc/AlienDefense/aliendefense_pal_main.json.
///
/// SeasonID IS the act id (AlienDefenseConfig.lua:78 reads `local nActId = tonumber(tbLine.SeasonID)`) and
/// the client keys the roster tbPalMainInfo[nActId][nIdx], so the id alone is not unique — the dictionary
/// is nested per season.
///
/// Pal_Type 1 = an ordinary pal (the handbook lists only those, uw_mow_team.lua:34-40), 2 = an alternate
/// row of the same pal (skipped by the red-dot scan, AlienDefense.CheckPalRed), 3 = a combined pal whose
/// unlocked state the client derives from its member pals (AlienDefense.GetPalUnlockState) — the server
/// side json has no CombineID column, so combined pals are left alone: the client never reads their own
/// unlock bit anyway.
///
/// Condition is the unlock requirement evaluated by AlienDefense.CheckPalCondition:
///   empty            -> no requirement at all (pal 1 / 3), i.e. unlocked
///   [1, level, n, e] -> clear `level` n times; e == 1 counts this pal's own clears (the (143, 111..250)
///                       block), otherwise the level's own clear count (attribute (22, level))
///   [2, type, n]     -> kill n monsters of level type `type` with this pal (0 = every type)
///   [3, type, n]     -> accumulate n clears of this pal across the levels of level type `type`
///   [4, n]           -> (girls only) use the girl skill n times
///
/// The numeric columns are read through JToken because this table mixes shapes: a few rows carry an
/// empty string (or omit the column entirely) in Skill/Icon/Pal_Type, and a strictly typed property would
/// abort the whole array load at that row (ResourceManager deserializes row by row and stops on the first
/// throw — the roster then silently loses every later act).
/// </summary>
[ResourceEntity("dlc/AlienDefense/aliendefense_pal_main.json")]
public class AlienDefensePalExcel : ExcelResource
{
    [JsonProperty("SeasonID")] public JToken? SeasonIdRaw { get; set; }
    [JsonProperty("Pal_Type")] public JToken? PalTypeRaw { get; set; }
    [JsonProperty("Id")] public JToken? IdRaw { get; set; }
    [JsonProperty("Condition")] public JToken? ConditionRaw { get; set; }
    [JsonProperty("PalUnlockDes")] public string? UnlockDescription { get; set; }
    [JsonProperty("Name")] public string? Name { get; set; }
    [JsonProperty("Icon")] public JToken? IconRaw { get; set; }
    [JsonProperty("Skill")] public JToken? SkillRaw { get; set; }
    [JsonProperty("ExtraUpgrade01")] public JToken? ExtraUpgradeRaw { get; set; }
    [JsonProperty("ExtraUpgrade01Condition")] public JToken? ExtraUpgradeConditionRaw { get; set; }
    [JsonProperty("ExtraPassive01")] public JToken? ExtraPassiveRaw { get; set; }
    [JsonProperty("ExtraPassive01Condition")] public JToken? ExtraPassiveConditionRaw { get; set; }

    [JsonIgnore] public uint SeasonId => ReadUint(SeasonIdRaw);
    [JsonIgnore] public uint PalType => ReadUint(PalTypeRaw);
    [JsonIgnore] public uint Id => ReadUint(IdRaw);
    [JsonIgnore] public uint Icon => ReadUint(IconRaw);
    [JsonIgnore] public uint Skill => ReadUint(SkillRaw);

    [JsonIgnore] public IReadOnlyList<uint> Condition => AlienDefenseConditionReader.ReadList(ConditionRaw);
    [JsonIgnore] public IReadOnlyList<uint> ExtraUpgradeCondition => AlienDefenseConditionReader.ReadList(ExtraUpgradeConditionRaw);
    [JsonIgnore] public IReadOnlyList<uint> ExtraPassiveCondition => AlienDefenseConditionReader.ReadList(ExtraPassiveConditionRaw);

    public override uint GetId() => Id;

    public override void Loaded()
    {
        if (SeasonId == 0 || Id == 0)
            return;

        if (!GameData.AlienDefensePalData.TryGetValue(SeasonId, out var roster))
        {
            roster = [];
            GameData.AlienDefensePalData[SeasonId] = roster;
        }

        roster[Id] = this;
    }

    private static uint ReadUint(JToken? token)
    {
        var raw = token?.ToString().Trim().Trim('[', ']', '"') ?? string.Empty;
        return uint.TryParse(raw, out var value) ? value : 0;
    }
}
