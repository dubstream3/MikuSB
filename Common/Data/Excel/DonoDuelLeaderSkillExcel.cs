using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// Donoduel leader skills from dlc/DonoDuel/donoduel_leaderskill.json (6 rows). Equipping one is a
/// single attribute write - (185, 4) = LeaderskillID (DonoDuel.lua:223-225 GetLeaderSkill,
/// AttrIds.DonoDuel.LeaderSkillSid) - and the client highlights whichever row matches that value
/// (uw_donoduel_arena_main_skill.lua:86-109), so the server must write and sync it.
/// </summary>
[ResourceEntity("dlc/DonoDuel/donoduel_leaderskill.json")]
public class DonoDuelLeaderSkillExcel : ExcelResource
{
    [JsonProperty("LeaderskillID")] public JToken? IdRaw { get; set; }
    [JsonProperty("SkillInfoId")] public JToken? SkillInfoIdRaw { get; set; }
    [JsonProperty("Condition")] public JToken? ConditionRaw { get; set; }

    [JsonIgnore] public uint Id => ReadUint(IdRaw);
    [JsonIgnore] public uint SkillInfoId => ReadUint(SkillInfoIdRaw);

    public override uint GetId() => Id;

    public override void Loaded()
    {
        if (Id != 0)
            GameData.DonoDuelLeaderSkillData[Id] = this;
    }

    /// <summary>
    /// Condition rows as flat [kind, id] pairs: {"36": 104} -&gt; [36, 104] (36 = that Donoduel
    /// story mission was claimed, 37 = that arena level was cleared; see DonoDuelGrowth.ConditionMet).
    /// </summary>
    public IReadOnlyList<uint> ConditionPairs()
    {
        var pairs = new List<uint>();
        if (ConditionRaw is not JObject map)
            return pairs;

        foreach (var property in map.Properties())
        {
            if (!uint.TryParse(property.Name, out var kind) || kind == 0)
                continue;

            pairs.Add(kind);
            pairs.Add(ReadUint(property.Value));
        }

        return pairs;
    }

    private static uint ReadUint(JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null)
            return 0;

        try
        {
            return token.Type switch
            {
                JTokenType.Integer => token.Value<uint>(),
                JTokenType.Float => (uint)token.Value<double>(),
                JTokenType.Array or JTokenType.Object => 0,
                _ => uint.TryParse(token.ToString().Trim().Trim('[', ']', '"'), out var parsed) ? parsed : 0
            };
        }
        catch
        {
            return 0;
        }
    }
}
