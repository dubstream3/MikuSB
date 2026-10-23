using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

[ResourceEntity("item/templates/suplies.json")]
public class SuppliesExcel : ExcelResource
{
    public uint Genre { get; set; }
    public uint Detail { get; set; }
    public uint Particular { get; set; }
    public uint Level { get; set; }
    [JsonProperty("Color")] public JToken? ColorRaw { get; set; }
    [JsonProperty("ProvideExp")] public JToken? ProvideExpRaw { get; set; }
    [JsonProperty("ConsumeGold")] public JToken? ConsumeGoldRaw { get; set; }
    [JsonProperty("GMnum")] public JToken? GMnumRaw { get; set; }
    [JsonProperty("UseMode")] public JToken? UseModeRaw { get; set; }
    [JsonProperty("Param1")] public JToken? Param1Raw { get; set; }
    [JsonProperty("LuaType")] public string LuaType { get; set; } = "";

    [JsonIgnore] public int Color => ReadInt(ColorRaw);
    [JsonIgnore] public uint ProvideExp => ReadUInt(ProvideExpRaw);
    [JsonIgnore] public uint ConsumeGold => ReadUInt(ConsumeGoldRaw);
    [JsonIgnore] public uint GMnum => ReadUInt(GMnumRaw);
    [JsonIgnore] public uint UseMode => ReadUInt(UseModeRaw);
    [JsonIgnore] public uint Param1 => ReadUInt(Param1Raw);

    [JsonIgnore]
    public ulong TemplateId => GameResourceTemplateId.FromGdpl(Genre, Detail, Particular, Level);

    /// <summary>
    /// Only feeds the resource manager's "id == 0 means a nested dictionary" probe. It is
    /// <b>not</b> a usable key: the cast drops Particular and Level, so the four card/weapon exp
    /// tiers of one Detail all collapse onto the same value. <see cref="GameData.SuppliesData"/>
    /// is keyed by <see cref="TemplateId"/> instead.
    /// </summary>
    public override uint GetId()
    {
        return (uint)TemplateId;
    }

    public override void Loaded()
    {
        GameData.SuppliesData[TemplateId] = this;
        GameData.AllSuppliesData.Add(this);
    }

    private static int ReadInt(JToken? token)
    {
        if (token == null)
        {
            return 0;
        }

        return token.Type switch
        {
            JTokenType.Integer => token.Value<int>(),
            JTokenType.Float => (int)token.Value<decimal>(),
            JTokenType.String when int.TryParse(token.Value<string>(), out var value) => value,
            _ => 0
        };
    }

    private static uint ReadUInt(JToken? token)
    {
        var value = ReadInt(token);
        return value > 0 ? (uint)value : 0;
    }
}
