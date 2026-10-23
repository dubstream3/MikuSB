using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// Donoduel hero skills from dlc/DonoDuel/donoduel_herocard_skill.json (30 rows, SkillID 101..193).
///
/// The card block stores the raw <c>SkillID</c> in the +5/+6 slots - the client maps it to the
/// runtime SkillInfoId (3832001..) via DonoDuelConfig:GetHeroSkill (DonoDuelConfig.lua:671-675,
/// DonoDuelArena.lua:871-912), so the server only has to accept the ids that exist here. There is no
/// skill level: learning a skill is a flat slot write (see DonoDuel_UpgradeSkill).
/// </summary>
[ResourceEntity("dlc/DonoDuel/donoduel_herocard_skill.json")]
public class DonoDuelHeroCardSkillExcel : ExcelResource
{
    [JsonProperty("SkillID")] public JToken? SkillIdRaw { get; set; }
    [JsonProperty("SkillInfoId")] public JToken? SkillInfoIdRaw { get; set; }
    [JsonProperty("SkillCD")] public JToken? SkillCdRaw { get; set; }

    [JsonIgnore] public uint SkillId => ReadUint(SkillIdRaw);
    [JsonIgnore] public uint SkillInfoId => ReadUint(SkillInfoIdRaw);

    public override uint GetId() => SkillId;

    public override void Loaded()
    {
        if (SkillId != 0)
            GameData.DonoDuelHeroCardSkillData[SkillId] = this;
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
