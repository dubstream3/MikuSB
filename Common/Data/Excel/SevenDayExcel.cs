using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

// The three tables behind the new-player "7 天乐" board (Activity/SevenDay.lua, ModeId 1043). The
// client loads them from activity/seven_day/*.txt at SevenDay.lua:18-56 and only uses them for
// display - every claim is decided server-side - so the server keeps its own decoded copy here.
//
//   seven_day_info.json         : one row (ActId 5, ActType 1) - only used to know the board exists
//   seven_day_achievements.json : (ActId, DayId) -> the 7 quest ids of that day (101..107, 201..207, ...)
//   seven_day_awards.json       : (ActId, AwardId) -> NeedPoint + ItemAward, AwardId 1..7 = 10/25/40/55/70/85/100

/// <summary>activity/seven_day/seven_day_info.json.</summary>
[ResourceEntity("activity/seven_day/seven_day_info.json")]
public class SevenDayInfoExcel : ExcelResource
{
    [JsonProperty("ActId")] public JToken? ActIdRaw { get; set; }
    [JsonProperty("ActType")] public JToken? ActTypeRaw { get; set; }

    [JsonIgnore] public uint ActId => ReadUint(ActIdRaw);
    [JsonIgnore] public uint ActType => ReadUint(ActTypeRaw);

    public override uint GetId() => ActId;

    public override void Loaded()
    {
        if (ActId != 0)
            GameData.SevenDayInfoData[ActId] = this;
    }

    private static uint ReadUint(JToken? token)
    {
        var raw = token?.ToString().Trim().Trim('[', ']', '"') ?? string.Empty;
        return uint.TryParse(raw, out var value) ? value : 0;
    }
}

/// <summary>activity/seven_day/seven_day_achievements.json - the 7 quests of one day.</summary>
[ResourceEntity("activity/seven_day/seven_day_achievements.json")]
public class SevenDayAchievementExcel : ExcelResource
{
    [JsonProperty("ActId")] public JToken? ActIdRaw { get; set; }
    [JsonProperty("DayId")] public JToken? DayIdRaw { get; set; }
    [JsonProperty("AchieveId")] public JToken? AchieveIdRaw { get; set; }

    [JsonIgnore] public uint ActId => ReadUint(ActIdRaw);
    [JsonIgnore] public uint DayId => ReadUint(DayIdRaw);
    [JsonIgnore] public List<uint> AchieveIds => ReadList(AchieveIdRaw);

    public override uint GetId() => ActId * 1000 + DayId;

    public override void Loaded()
    {
        if (ActId != 0 && DayId != 0)
            GameData.SevenDayAchievementData[(ActId, DayId)] = this;
    }

    private static uint ReadUint(JToken? token)
    {
        var raw = token?.ToString().Trim().Trim('[', ']', '"') ?? string.Empty;
        return uint.TryParse(raw, out var value) ? value : 0;
    }

    private static List<uint> ReadList(JToken? token)
    {
        var list = new List<uint>();
        if (token is not JArray array)
            return list;

        foreach (var item in array)
        {
            var value = ReadUint(item);
            if (value != 0)
                list.Add(value);
        }

        return list;
    }
}

/// <summary>
/// activity/seven_day/seven_day_awards.json - the 7 stage rewards of the point bar.
///
/// ItemAward is one [G, D, P, L, Count] row (e.g. [4,3,1,1,10000] at NeedPoint 10), which is exactly
/// what Item.Gain wants; the client displays it from its own copy at SevenDay.lua:298-301, so the
/// server has to grant the same row.
/// </summary>
[ResourceEntity("activity/seven_day/seven_day_awards.json")]
public class SevenDayAwardExcel : ExcelResource
{
    [JsonProperty("ActId")] public JToken? ActIdRaw { get; set; }
    [JsonProperty("AwardId")] public JToken? AwardIdRaw { get; set; }
    [JsonProperty("NeedPoint")] public JToken? NeedPointRaw { get; set; }
    [JsonProperty("ItemAward")] public JToken? ItemAward { get; set; }

    [JsonIgnore] public uint ActId => ReadUint(ActIdRaw);
    [JsonIgnore] public uint AwardId => ReadUint(AwardIdRaw);
    [JsonIgnore] public uint NeedPoint => ReadUint(NeedPointRaw);

    public override uint GetId() => ActId * 1000 + AwardId;

    public override void Loaded()
    {
        if (ActId != 0 && AwardId != 0)
            GameData.SevenDayAwardData[(ActId, AwardId)] = this;
    }

    private static uint ReadUint(JToken? token)
    {
        var raw = token?.ToString().Trim().Trim('[', ']', '"') ?? string.Empty;
        return uint.TryParse(raw, out var value) ? value : 0;
    }
}

/// <summary>activity/vigour_supply/vigour_supply.json - the two free vigour meals.</summary>
[ResourceEntity("activity/vigour_supply/vigour_supply.json")]
public class VigourSupplyExcel : ExcelResource
{
    [JsonProperty("Id")] public JToken? IdRaw { get; set; }
    [JsonProperty("TimeStart")] public JToken? TimeStartRaw { get; set; }
    [JsonProperty("TimeEnd")] public JToken? TimeEndRaw { get; set; }
    [JsonProperty("VigourNum")] public JToken? VigourNumRaw { get; set; }

    [JsonIgnore] public uint Id => ReadUint(IdRaw);
    [JsonIgnore] public uint VigourNum => ReadUint(VigourNumRaw);

    /// <summary>[HHmm] window start, e.g. 1100 = 11:00 local.</summary>
    [JsonIgnore] public uint StartHhmm => ReadUint(TimeStartRaw);

    /// <summary>[HHmm] window end, e.g. 2359 = 23:59 local.</summary>
    [JsonIgnore] public uint EndHhmm => ReadUint(TimeEndRaw);

    public override uint GetId() => Id;

    public override void Loaded()
    {
        if (Id != 0)
            GameData.VigourSupplyData[Id] = this;
    }

    private static uint ReadUint(JToken? token)
    {
        var raw = token?.ToString().Trim().Trim('[', ']', '"') ?? string.Empty;
        return uint.TryParse(raw, out var value) ? value : 0;
    }
}

/// <summary>
/// activity/player_charge/player_charge.json - 8 cumulative top-up tiers. NUM is the cumulative RMB
/// amount, ITEM the three reward rows of that tier.
/// </summary>
[ResourceEntity("activity/player_charge/player_charge.json")]
public class PlayerChargeExcel : ExcelResource
{
    [JsonProperty("INDEX")] public JToken? IndexRaw { get; set; }
    [JsonProperty("NUM")] public JToken? NumRaw { get; set; }
    [JsonProperty("ITEM")] public JToken? ItemRaw { get; set; }

    [JsonIgnore] public uint Index => ReadUint(IndexRaw);
    [JsonIgnore] public uint Num => ReadUint(NumRaw);
    [JsonIgnore] public JToken? Item => ItemRaw;

    public override uint GetId() => Index;

    public override void Loaded()
    {
        if (Index != 0)
            GameData.PlayerChargeData[Index] = this;
    }

    private static uint ReadUint(JToken? token)
    {
        var raw = token?.ToString().Trim().Trim('[', ']', '"') ?? string.Empty;
        return uint.TryParse(raw, out var value) ? value : 0;
    }
}
