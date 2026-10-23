using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// The Donoduel rogue turntable ("小游戏" / 转盘) reward pool,
/// dlc/DonoDuel/donoduel_catch_room_minigame.json (18 rows).
///
/// The client loads the same table (DLC/DonoDuel/DonoDuelRogue.lua:131-152) and lays it out as an
/// 18-slot wheel whose slot index is <see cref="Sort"/>: UMG/DonoDuel/umg_donoduel_turntable.lua:20-30
/// scatters the rows into the TurntableItem{Sort} widgets and :280 wraps the spin at
/// <c>CurrentIndex % 18 + 1</c>. The settlement reply's nAwardId is that Sort (:41 TurntableStart),
/// which is why the server rolls over this table instead of picking a card on its own.
///   RewardType 1 -&gt; Reward1 = [g, d, p, l, count]: a normal item grant (the shipped rows use
///   genre 4 = TYPE_USEABLE), displayed by the client through UItem.FindTemplate (:107-112).
///   RewardType 2 -&gt; Reward2 = [heroCardId, star, quality]: a Donoduel hero card, resolved through
///   DonoDuelConfig.tbHeroCardList (:114-121 display, :264-268 award).
/// Both payload columns are "" on the row that does not use them, hence the tolerant reader.
/// </summary>
[ResourceEntity("dlc/DonoDuel/donoduel_catch_room_minigame.json")]
public class DonoDuelCatchRoomMiniGameExcel : ExcelResource
{
    [JsonProperty("ID")] public JToken? IdRaw { get; set; }
    [JsonProperty("Sort")] public JToken? SortRaw { get; set; }
    [JsonProperty("Quality")] public JToken? QualityRaw { get; set; }
    [JsonProperty("Weight")] public JToken? WeightRaw { get; set; }
    [JsonProperty("RewardType")] public JToken? RewardTypeRaw { get; set; }
    [JsonProperty("Reward1")] public JToken? Reward1Raw { get; set; }
    [JsonProperty("Reward2")] public JToken? Reward2Raw { get; set; }

    [JsonIgnore] public uint Id => ReadUint(IdRaw);
    [JsonIgnore] public uint Sort => ReadUint(SortRaw);
    [JsonIgnore] public uint Quality => ReadUint(QualityRaw);
    [JsonIgnore] public uint Weight => ReadUint(WeightRaw);

    /// <summary>1 = item grant (Reward1), 2 = hero card (Reward2).</summary>
    [JsonIgnore] public uint RewardType => ReadUint(RewardTypeRaw);

    [JsonIgnore] public IReadOnlyList<uint> Reward1 => ReadList(Reward1Raw);
    [JsonIgnore] public IReadOnlyList<uint> Reward2 => ReadList(Reward2Raw);

    public override uint GetId() => Id;

    public override void Loaded()
    {
        if (Id != 0)
            GameData.DonoDuelMiniGameData[Id] = this;
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
