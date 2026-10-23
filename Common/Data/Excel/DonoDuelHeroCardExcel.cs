using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// 多诺-巅峰对决 (Donoduel) hero cards from dlc/DonoDuel/donoduel_herocard.json.
///
/// A card the player owns lives in attribute group 185 as a block of 7 slots (see DonoDuelBag):
/// +0 = (templateId &lt;&lt; 16) | star, +1 = quality &lt;&lt; 16, +2/+3/+4 = attack/defence/health and
/// +5/+6 = skills. The client copies +2/+3/+4 straight into FSpawnNpcOverrideParams.Attack/
/// Defence/Health (DLC/DonoDuel/DonoDuelArena.lua:838-841 SpawnPreDeployDono) without any fallback,
/// so a card stored with zeros fights at the model template's bare stats. AttackInitial /
/// DefenseInitial / HealthInitial are the values a fresh card is meant to start at - the per-quality
/// maps ("Attack"/"Defense"/"Health", quality 1..6) hold the upgrade ceilings the client displays.
///
/// SkillUnloked ([2,3,4]) is NOT a skill id list: element i is the star level slot i requires
/// (uw_donoduel_culture_skill.lua:288-321), and Stars is the card's maximum star.
/// </summary>
[ResourceEntity("dlc/DonoDuel/donoduel_herocard.json")]
public class DonoDuelHeroCardExcel : ExcelResource
{
    [JsonProperty("ID")] public JToken? IdRaw { get; set; }
    [JsonProperty("Name")] public string? Name { get; set; }
    [JsonProperty("Stars")] public JToken? StarsRaw { get; set; }
    [JsonProperty("AttackInitial")] public JToken? AttackInitialRaw { get; set; }
    [JsonProperty("DefenseInitial")] public JToken? DefenseInitialRaw { get; set; }
    [JsonProperty("HealthInitial")] public JToken? HealthInitialRaw { get; set; }
    [JsonProperty("Attack")] public JToken? AttackRaw { get; set; }
    [JsonProperty("Defense")] public JToken? DefenseRaw { get; set; }
    [JsonProperty("Health")] public JToken? HealthRaw { get; set; }
    [JsonProperty("SkillUnloked")] public JToken? SkillUnlokedRaw { get; set; }

    [JsonIgnore] public uint Id => ReadUint(IdRaw);
    [JsonIgnore] public uint Stars => ReadUint(StarsRaw);
    [JsonIgnore] public uint AttackInitial => ReadUint(AttackInitialRaw);
    [JsonIgnore] public uint DefenseInitial => ReadUint(DefenseInitialRaw);
    [JsonIgnore] public uint HealthInitial => ReadUint(HealthInitialRaw);

    /// <summary>Highest attack/defence/health a card may reach at <paramref name="quality"/>.</summary>
    public uint AttackCap(uint quality) => MapAt(AttackRaw, quality);
    public uint DefenseCap(uint quality) => MapAt(DefenseRaw, quality);
    public uint HealthCap(uint quality) => MapAt(HealthRaw, quality);

    /// <summary>Star level slot (1..3) requires: SkillUnloked = [2, 3, 4] -&gt; slot1 needs 2 stars.</summary>
    public uint SkillUnlockStar(int slot)
    {
        if (SkillUnlokedRaw is not JArray values || slot < 1 || slot > values.Count)
            return 0;

        return ReadUint(values[slot - 1]);
    }

    public override uint GetId() => Id;

    public override void Loaded()
    {
        if (Id != 0)
            GameData.DonoDuelHeroCardData[Id] = this;
    }

    private static uint MapAt(JToken? token, uint quality)
    {
        if (token is not JObject map)
            return 0;

        foreach (var property in map.Properties())
        {
            if (uint.TryParse(property.Name, out var key) && key == quality)
                return ReadUint(property.Value);
        }

        return 0;
    }

    private static uint ReadUint(JToken? token)
    {
        var raw = token?.ToString().Trim().Trim('[', ']', '"') ?? string.Empty;
        return uint.TryParse(raw, out var value) ? value : 0;
    }
}
