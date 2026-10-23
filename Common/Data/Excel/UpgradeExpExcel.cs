using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

[ResourceEntity("item/upgrade_exp.json")]
public class UpgradeExpExcel : ExcelResource
{
    public int Lv { get; set; }
    [JsonProperty("CardNeedExp")] public JToken? CardNeedExpRaw { get; set; }
    [JsonProperty("SSRCardNeedExp")] public JToken? SsrCardNeedExpRaw { get; set; }
    [JsonProperty("SusNeedExp")] public JToken? SusNeedExpRaw { get; set; }
    [JsonProperty("SSRSusNeedExp")] public JToken? SsrSusNeedExpRaw { get; set; }
    [JsonProperty("WeaponNeedExp")] public JToken? WeaponNeedExpRaw { get; set; }
    [JsonProperty("SSRWeaponNeedExp")] public JToken? SsrWeaponNeedExpRaw { get; set; }

    [JsonIgnore] public uint CardNeedExp => ReadUInt(CardNeedExpRaw);
    [JsonIgnore] public uint SSRCardNeedExp => ReadUInt(SsrCardNeedExpRaw);
    [JsonIgnore] public uint SusNeedExp => ReadUInt(SusNeedExpRaw);
    [JsonIgnore] public uint SSRSusNeedExp => ReadUInt(SsrSusNeedExpRaw);
    [JsonIgnore] public uint WeaponNeedExp => ReadUInt(WeaponNeedExpRaw);
    [JsonIgnore] public uint SSRWeaponNeedExp => ReadUInt(SsrWeaponNeedExpRaw);

    public override uint GetId()
    {
        return (uint)Lv;
    }

    public override void Loaded()
    {
        GameData.UpgradeExpData[Lv] = this;
    }

    private static uint ReadUInt(JToken? token)
    {
        if (token == null)
        {
            return 0;
        }

        return token.Type switch
        {
            JTokenType.Integer => token.Value<uint>(),
            JTokenType.Float => (uint)Math.Max(0, token.Value<decimal>()),
            JTokenType.String when uint.TryParse(token.Value<string>(), out var value) => value,
            _ => 0
        };
    }
}
