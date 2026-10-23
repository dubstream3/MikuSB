using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

[ResourceEntity("player/levels.json")]
public class PlayerLevelExcel : ExcelResource
{
    public uint Level { get; set; }
    [JsonProperty("MaxExp")] public JToken? MaxExpRaw { get; set; }
    [JsonProperty("MaxVigor")] public JToken? MaxVigorRaw { get; set; }
    [JsonProperty("GiftItems")] public JToken? GiftItemsRaw { get; set; }

    [JsonIgnore]
    public uint MaxExp => MaxExpRaw?.Type switch
    {
        JTokenType.Integer => MaxExpRaw.Value<uint>(),
        JTokenType.Float => (uint)Math.Max(0, MaxExpRaw.Value<decimal>()),
        JTokenType.String when uint.TryParse(MaxExpRaw.Value<string>(), out var value) => value,
        _ => 0
    };

    /// <summary>
    /// Stamina cap for this level. The client reads the same column through
    /// Player.GetMaxVigor (Script/Player/player.lua:57-58), so the server has to agree with it or the
    /// UI would show a different ceiling than the one enforced here.
    /// </summary>
    [JsonIgnore]
    public uint MaxVigor => MaxVigorRaw?.Type switch
    {
        JTokenType.Integer => MaxVigorRaw.Value<uint>(),
        JTokenType.Float => (uint)Math.Max(0, MaxVigorRaw.Value<decimal>()),
        JTokenType.String when uint.TryParse(MaxVigorRaw.Value<string>(), out var value) => value,
        _ => 0
    };

    [JsonIgnore]
    public IReadOnlyList<IReadOnlyList<uint>> GiftItems => ReadRewardRows(GiftItemsRaw);

    public override uint GetId() => Level;

    public override void Loaded()
    {
        GameData.PlayerLevelData[Level] = this;
    }

    private static IReadOnlyList<IReadOnlyList<uint>> ReadRewardRows(JToken? token)
    {
        if (token is not JArray array)
            return [];

        return array
            .OfType<JArray>()
            .Select(row => (IReadOnlyList<uint>)row.Select(ReadUInt).ToArray())
            .Where(row => row.Count >= 5)
            .ToArray();
    }

    private static uint ReadUInt(JToken token)
    {
        return token.Type switch
        {
            JTokenType.Integer => token.Value<uint>(),
            JTokenType.Float => (uint)Math.Max(0, token.Value<decimal>()),
            JTokenType.String when uint.TryParse(token.Value<string>(), out var value) => value,
            _ => 0
        };
    }
}
