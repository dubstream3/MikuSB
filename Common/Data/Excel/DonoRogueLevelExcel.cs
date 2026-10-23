using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// Donoduel rogue exploration rooms, dlc/DonoDuel/DonoRogue/donorogue_level.json (68 rows).
///
/// LevelID is what the client settles with (Req_RoomSettlement nRoomId). CatchID joins into
/// donoduel_catch_battle.json for the room's Phase; RoomType joins into
/// donoduel_catch_room_heropool.json for the room's card pool. Both columns are "" on rows
/// that have none (the run's entry room), hence the tolerant reader.
/// </summary>
[ResourceEntity("dlc/DonoDuel/DonoRogue/donorogue_level.json")]
public class DonoRogueLevelExcel : ExcelResource
{
    [JsonProperty("LevelID")] public JToken? LevelIdRaw { get; set; }
    [JsonProperty("MapID")] public JToken? MapIdRaw { get; set; }
    [JsonProperty("Stage")] public JToken? StageRaw { get; set; }
    [JsonProperty("RoomID")] public JToken? RoomIdRaw { get; set; }
    [JsonProperty("CatchID")] public JToken? CatchIdRaw { get; set; }
    [JsonProperty("RoomType")] public JToken? RoomTypeRaw { get; set; }

    [JsonIgnore] public uint LevelId => ReadUint(LevelIdRaw);
    [JsonIgnore] public uint MapId => ReadUint(MapIdRaw);
    [JsonIgnore] public uint Stage => ReadUint(StageRaw);
    [JsonIgnore] public uint RoomId => ReadUint(RoomIdRaw);
    [JsonIgnore] public uint CatchId => ReadUint(CatchIdRaw);
    [JsonIgnore] public uint RoomType => ReadUint(RoomTypeRaw);

    public override uint GetId() => LevelId;

    public override void Loaded()
    {
        if (LevelId != 0)
            GameData.DonoRogueLevelData[LevelId] = this;
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
