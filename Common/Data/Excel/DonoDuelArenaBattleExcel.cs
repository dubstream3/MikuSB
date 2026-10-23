using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

/// <summary>
/// 多诺-巅峰对决 (Donoduel) arena challenge rows from dlc/DonoDuel/donoduel_arena_battle.json.
///
/// The arena levels themselves (dlc/DonoDuel/donoduel_arena_level.json, all MapID 5505) are loaded
/// locally by the client: Launch/Classes/DonoDuelArenaLogic.lua:OnStart opens the map as soon as
/// DonoDuelArena_EnterLevel answers with an nSeed, so this side only has to hand out the seed and
/// settle the run. The reward rows live here: DonoDuelArenaLogic:OnSettlement reads
/// DonoDuelArena:GetArenaBattleConfig(levelId).tbReward into Launch.tbAward for the
/// DonoArenaSettlement panel, so the server has to grant exactly the same rows.
/// </summary>
[ResourceEntity("dlc/DonoDuel/donoduel_arena_battle.json")]
public class DonoDuelArenaBattleExcel : ExcelResource
{
    [JsonProperty("LevelID")] public JToken? LevelIdRaw { get; set; }
    [JsonProperty("ArenaType")] public JToken? ArenaTypeRaw { get; set; }
    [JsonProperty("Prerequisite")] public JToken? PrerequisiteRaw { get; set; }
    [JsonProperty("Condition")] public JToken? Condition { get; set; }
    [JsonProperty("Name")] public string? Name { get; set; }
    [JsonProperty("Desc")] public string? Desc { get; set; }
    [JsonProperty("BuffID")] public JToken? BuffId { get; set; }
    [JsonProperty("OpenTime")] public JToken? OpenTime { get; set; }
    [JsonProperty("CloseTime")] public JToken? CloseTime { get; set; }
    [JsonProperty("Reward")] public JToken? Reward { get; set; }

    [JsonIgnore] public uint LevelId => ReadUint(LevelIdRaw);
    [JsonIgnore] public uint ArenaType => ReadUint(ArenaTypeRaw);

    public override uint GetId() => LevelId;

    public override void Loaded()
    {
        if (LevelId != 0)
            GameData.DonoDuelArenaBattleData[LevelId] = this;
    }

    private static uint ReadUint(JToken? token)
    {
        var raw = token?.ToString().Trim().Trim('[', ']', '"') ?? string.Empty;
        return uint.TryParse(raw, out var value) ? value : 0;
    }
}
