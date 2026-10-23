using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

[ResourceEntity("item/templates/weapon.json")]
public class WeaponExcel : ExcelResource
{
    public uint Genre { get; set; }
    public uint Detail { get; set; }
    public uint Particular { get; set; }
    public uint Level { get; set; }
    [JsonProperty("Color")] public JToken? ColorRaw { get; set; }
    [JsonProperty("InitBreak")] public JToken? InitBreakRaw { get; set; }
    public int Class { get; set; }
    public uint Icon { get; set; }
    [JsonProperty("ProvideExp")] public JToken? ProvideExpRaw { get; set; }
    [JsonProperty("ConsumeGold")] public JToken? ConsumeGoldRaw { get; set; }
    [JsonProperty("RecycleID")] public JToken? RecycleIdRaw { get; set; }
    public int EvolutionMatID { get; set; }
    public int BreakMatID { get; set; }
    public int LevelLimitID { get; set; }
    public int BreakLimitID { get; set; }
    public int AppearID { get; set; }
    public List<int> DefaultSkillID { get; set; } = [];
    public List<int> WeaponPartsLimit { get; set; } = [];
    public string I18n { get; set; } = "";

    // 41 of the 189 rows carry an empty string here, so the column is read through a token
    // instead of binding a list straight onto it (same reason as Color/ProvideExp above).
    [JsonProperty("WeaponPartsAward")] public JToken? WeaponPartsAwardRaw { get; set; }

    [JsonIgnore] public int Color => ReadInt(ColorRaw);
    [JsonIgnore] public List<int> WeaponPartsAward => ReadIntList(WeaponPartsAwardRaw);
    [JsonIgnore] public uint InitBreak => ReadUInt(InitBreakRaw);
    [JsonIgnore] public uint ProvideExp => ReadUInt(ProvideExpRaw);
    [JsonIgnore] public uint ConsumeGold => ReadUInt(ConsumeGoldRaw);
    [JsonIgnore] public int RecycleID => ReadInt(RecycleIdRaw);

    public override uint GetId()
    {
        return (uint)I18n.GetHashCode();
    }

    public override void Loaded()
    {
        GameData.WeaponData.Add(GetId(), this);
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

    private static List<int> ReadIntList(JToken? token)
    {
        var result = new List<int>();
        if (token is not JArray array)
        {
            return result;
        }

        foreach (var entry in array)
        {
            result.Add(ReadInt(entry));
        }

        return result;
    }
}
