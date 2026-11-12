using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// Island staff (dlc/vircapture/build/vircapture_staff.json).
/// <c>Property01..03</c> are "level,value,level,value" pairs, <c>Salary</c> is the hold cost per
/// staff level and <c>Consume</c> is the coin price of reaching level 2..MaxLevel.
/// </summary>
[ResourceEntity("dlc/vircapture/build/vircapture_staff.json")]
public class VirCaptureStaffExcel : ExcelResource
{
    [JsonProperty("Id")] public uint Id { get; set; }
    [JsonProperty("Type")] public JToken? TypeRaw { get; set; }

    /// <summary>Some rows carry an empty string in <c>Type</c>; treat that as 0.</summary>
    [JsonIgnore]
    public uint Type => TypeRaw is null
        ? 0
        : TypeRaw.Type == JTokenType.Integer ? TypeRaw.Value<uint>()
        : uint.TryParse(TypeRaw.ToString(), out var parsed) ? parsed : 0;
    [JsonProperty("Quality")] public uint Quality { get; set; }
    [JsonProperty("Level")] public uint Level { get; set; }
    [JsonProperty("MaxLevel")] public uint MaxLevel { get; set; }
    [JsonProperty("Property01")] public string Property01 { get; set; } = string.Empty;
    [JsonProperty("Property02")] public string Property02 { get; set; } = string.Empty;
    [JsonProperty("Property03")] public string Property03 { get; set; } = string.Empty;
    [JsonProperty("Salary")] public List<long> Salary { get; set; } = [];
    [JsonProperty("Consume")] public List<long> Consume { get; set; } = [];

    public override uint GetId() => Id;

    public long SalaryAt(uint level) => level >= 1 && level <= Salary.Count ? Salary[(int)(level - 1)] : 0;

    /// <summary>Coin price of moving from <paramref name="level"/> to the next level.</summary>
    public long UpgradeCost(uint level) => level >= 1 && level <= Consume.Count ? Consume[(int)(level - 1)] : 0;

    public uint PropertyValue(uint propertyIndex, uint level)
    {
        var raw = propertyIndex switch
        {
            1 => Property01,
            2 => Property02,
            3 => Property03,
            _ => string.Empty
        };
        if (string.IsNullOrWhiteSpace(raw))
            return 0;

        var cells = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 0; i + 1 < cells.Length; i += 2)
        {
            if (uint.TryParse(cells[i], out var lv) && lv == level && uint.TryParse(cells[i + 1], out var value))
                return value;
        }

        return 0;
    }

    public override void Loaded()
    {
        GameData.VirCaptureStaffData[Id] = this;
    }
}
