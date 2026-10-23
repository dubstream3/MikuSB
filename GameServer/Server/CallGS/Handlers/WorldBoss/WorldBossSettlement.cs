using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.WorldBoss;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.WorldBoss;

// 世界BOSS run settlement. Like every other module the client does not call its own settlement api:
// WorldBossLogic.Req_LevelSettlement (Challenge/WorldBoss/WorldBoss.lua:680-707) wraps
// { nID, nActID, nLevelID, nTeamID, nSeed, nDamageNum, tbLog, tbMonster } in
// Reconnect.Send_SettleInfo, which arrives as Chapter_DealLevelSettlement with
// sCmd = "WorldBossLogic_LevelSettlement" (and "WorldBossLogic_LevelFail" + nManualExit = 1 when the
// player leaves). The wrapper retries five times two seconds apart and force-disconnects after that,
// so this branch has to answer within ten seconds; both client callbacks read no field, an empty
// object is the whole contract.
//
// nDamageNum is the run's real damage. It feeds (179, base+2) for the score board and the three quest
// families in achievement/activity_quest.json 21001..21020:
//   Type 380 -> percent of the stage's BossMaxHp (checks 25/50/75/100)
//   Type 381 -> raw damage (checks 3e7/6e7/1e8)
//   Type 382 -> recorded attempts of this act (checks 1/2/3)
public static class WorldBossSettlement
{
    private static readonly Logger Log = new(nameof(WorldBossSettlement));

    public static (JsonNode Payload, NtfSyncPlayer? Sync) HandleSettlement(PlayerInstance player, JsonNode? tbParam, bool failed)
    {
        var activityId = ReadUint(tbParam, "nID");
        var actId = ReadUint(tbParam, "nActID");
        var stageId = ReadUint(tbParam, "nLevelID");
        var damage = failed ? 0u : ReadUint(tbParam, "nDamageNum");
        var manualExit = ReadUint(tbParam, "nManualExit");

        var act = WorldBossService.FindAct(activityId, actId) ?? WorldBossService.SelectAct(DateTime.Now);
        if (act == null)
        {
            Log.Warn($"WorldBoss settlement without a served act (nID={activityId}, nActID={actId}).");
            return (new JsonObject(), null);
        }

        var sync = new NtfSyncPlayer();
        WorldBossService.ApplyResult(player, act, stageId, damage, sync);

        Log.Info($"WorldBoss {(failed ? "fail" : "settlement")} act={act.ActivityId}/{act.ActId} " +
                 $"stage={stageId} damage={damage} manualExit={manualExit} for uid={player.Uid}.");

        return (new JsonObject(), sync);
    }

    private static uint ReadUint(JsonNode? node, string key)
    {
        var value = node?[key];
        if (value is JsonValue json && json.TryGetValue<uint>(out var parsed))
            return parsed;

        return uint.TryParse(value?.ToString(), out var text) ? text : 0;
    }
}
