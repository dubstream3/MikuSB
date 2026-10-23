using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.WorldBoss;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.WorldBoss;

// 世界BOSS level enter (Challenge/WorldBoss/WorldBoss.lua:635-659). The client sends
// { nID, nActID, nLevelID (= the *stage* id from LevelList: 2501 / 2511, not the battle level
// 3401 / 3402), nTeamID, tbLog } with a depend-param global counter and shows a connection overlay,
// so the reply must come.
//
// Reply contract: nSeed for the settlement, and `levelId` + `num` for the shared HP bar - the client
// writes SetGlobalNum("WorldBoss_Global_" .. levelId, num), so levelId has to be the requested stage
// id (echoing the battle level id would aim the counter at another key) and num is the remaining HP
// in millions.
[CallGSApi("WorldBossLogic_EnterLevel")]
public class WorldBossLogic_EnterLevel : CallGSHandler<WorldBossEnterLevelParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, WorldBossEnterLevelParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var act = WorldBossService.FindAct(request.ActivityId, request.ActId)
                  ?? WorldBossService.SelectAct(DateTime.Now);

        if (act == null)
            return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));

        var sync = new NtfSyncPlayer();
        var block = WorldBossService.BeginAttempt(player, act, request.LevelId, sync, null);
        if (block < 0)
            return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));

        var seed = (uint)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() & 0x7FFFFFFF);
        if (seed == 0)
            seed = 1;

        var maxHp = WorldBossService.BossMaxHp(request.LevelId);
        var num = maxHp / 1000000;

        Logger.GetByClassName().Info(
            $"WorldBoss EnterLevel act={act.ActivityId}/{act.ActId} stage={request.LevelId} " +
            $"block={block} num={num} for uid={player.Uid}.");

        return Task.FromResult(CallGSResult.Ok(new JsonObject
        {
            ["nSeed"] = seed,
            ["levelId"] = request.LevelId,
            ["num"] = num
        }, sync, "WorldBossLogic_EnterLevel"));
    }
}

public sealed class WorldBossEnterLevelParam
{
    [JsonPropertyName("nID")] public uint ActivityId { get; set; }
    [JsonPropertyName("nActID")] public uint ActId { get; set; }
    [JsonPropertyName("nLevelID")] public uint LevelId { get; set; }
    [JsonPropertyName("nTeamID")] public uint TeamId { get; set; }
    [JsonPropertyName("tbLog")] public JsonObject? Log { get; set; }
}
