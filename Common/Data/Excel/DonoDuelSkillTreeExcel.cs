using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// Donoduel skill tree nodes from dlc/DonoDuel/donoduel_skilltree.json ("科技树"). Each studied node
/// sets one bit in the (185, 91 + (id-1)/30) bit array (AttrIds.DonoDuel.SkillTreeStartSid) and costs
/// season currency 63 (Consume = [63, price], 5000 for node 1 and +100 per node).
///
/// The shipped table has 26 rows, but three of them are mis-shifted (the ID landed in the name column
/// and Consume holds a texture id) - the client drops them via <c>tonumber(tbLine.ID) or 0</c>
/// (DonoDuelConfig.lua:368-392) and so does this loader.
/// </summary>
[ResourceEntity("dlc/DonoDuel/donoduel_skilltree.json")]
public class DonoDuelSkillTreeExcel : ExcelResource
{
    [JsonProperty("ID")] public JToken? IdRaw { get; set; }
    [JsonProperty("Prerequisite")] public JToken? PrerequisiteRaw { get; set; }
    [JsonProperty("Name")] public string? Name { get; set; }
    [JsonProperty("Consume")] public JToken? ConsumeRaw { get; set; }
    [JsonProperty("BuffID")] public JToken? BuffIdRaw { get; set; }
    [JsonProperty("Icon")] public JToken? IconRaw { get; set; }

    [JsonIgnore] public uint Id => ReadUint(IdRaw);
    [JsonIgnore] public uint Prerequisite => ReadUint(PrerequisiteRaw);
    [JsonIgnore] public uint BuffId => ReadUint(BuffIdRaw);

    /// <summary>Consume = [moneyType, amount]; node 1 costs 5000 season currency and +100 each node.</summary>
    [JsonIgnore]
    public (uint MoneyType, uint Amount) Consume
    {
        get
        {
            if (ConsumeRaw is not JArray { Count: >= 2 } values)
                return (0, 0);

            return (ReadUint(values[0]), ReadUint(values[1]));
        }
    }

    public override uint GetId() => Id;

    public override void Loaded()
    {
        // Id == 0 is the mis-shifted row the client drops as well.
        if (Id != 0)
            GameData.DonoDuelSkillTreeData[Id] = this;
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
                _ => uint.Parse(token.ToString().Trim().Trim('[', ']', '"'))
            };
        }
        catch
        {
            return 0;
        }
    }
}
