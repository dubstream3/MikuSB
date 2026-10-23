using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// Donoduel rogue catch-room card pools, dlc/DonoDuel/donoduel_catch_room_heropool.json (29 rows).
///
/// The client fills the room's draft from the row whose (Phase, RoomType) matches the room
/// (DLC/DonoDuel/DonoDuelRogue.lua:337-342 GetHeroPool). Number is how many cards the room
/// offers; StarWeight / QualityWeight pick the rolled star / quality by weight with the index
/// as the value (index + 1); Currency = [moneyType, amount] is the room's coin reward.
/// BondWeight only biases the draft's bonds, which are not card properties, so the server
/// ignores it when rolling the catch itself.
/// Rows with Number = 0 (RoomType 4) are non-catch rooms: currency only, no card.
/// </summary>
[ResourceEntity("dlc/DonoDuel/donoduel_catch_room_heropool.json")]
public class DonoDuelCatchRoomHeropoolExcel : ExcelResource
{
    [JsonProperty("PoolID")] public JToken? PoolIdRaw { get; set; }
    [JsonProperty("Phase")] public JToken? PhaseRaw { get; set; }
    [JsonProperty("RoomType")] public JToken? RoomTypeRaw { get; set; }
    [JsonProperty("Number")] public JToken? NumberRaw { get; set; }
    [JsonProperty("StarWeight")] public JToken? StarWeightRaw { get; set; }
    [JsonProperty("QualityWeight")] public JToken? QualityWeightRaw { get; set; }
    [JsonProperty("Currency")] public JToken? CurrencyRaw { get; set; }

    [JsonIgnore] public uint PoolId => ReadUint(PoolIdRaw);
    [JsonIgnore] public uint Phase => ReadUint(PhaseRaw);
    [JsonIgnore] public uint RoomType => ReadUint(RoomTypeRaw);
    [JsonIgnore] public uint Number => ReadUint(NumberRaw);

    [JsonIgnore] public IReadOnlyList<uint> StarWeight => ReadList(StarWeightRaw);
    [JsonIgnore] public IReadOnlyList<uint> QualityWeight => ReadList(QualityWeightRaw);
    [JsonIgnore] public IReadOnlyList<uint> Currency => ReadList(CurrencyRaw);

    public override uint GetId() => PoolId;

    public override void Loaded()
    {
        if (PoolId != 0)
            GameData.DonoDuelHeropoolData[PoolId] = this;
    }

    private static IReadOnlyList<uint> ReadList(JToken? token)
    {
        var values = new List<uint>();
        if (token is not JArray array)
            return values;

        foreach (var item in array)
            values.Add(ReadUint(item));

        return values;
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
