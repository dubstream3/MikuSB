using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;

// 悖论迷宫积分模式 (Rogue3DScoreModel) 的两种战斗结算. Both arrive wrapped as
// Chapter_DealLevelSettlement:
//   sCmd = "Rogue3DScoreModel_BuffLevelSettlement"  (DLC/Rogue3D/Rogue3DScoreModel.lua:1169)
//   sCmd = "Rogue3DScoreModel_ScoreLevelSettlement" (:1303)
// and both used to fall through to the generic echo. The echo is not good enough for either: the
// settlement panels read fields that only this server can supply, and read two of them without a nil
// guard, so a raw echo makes the panel throw instead of showing a result:
//   UMG/Rogue25/umg_rogue25_settlement.lua:49  ipairs(tbData.tbMonsterDropRiki)   (no "or {}")
//   UMG/Rogue/umg_rogue_settlement.lua:87      ipairs(tbData.tbMonsterDropRiki)   (no "or {}")
//   UMG/Rogue/umg_rogue_settlement.lua:97      tbData.nNewScore > tbData.nOldScore (nil compare)
//
// Nothing writes the score-model banks by itself - the client only has getters for them
// (Rogue3DLogic.lua:77-84 LevelScoreStart=2401 / ScoreAwardStart=2421 / BuffRikiStart=2441, read back at
// :1151-1189) - so the server owns both the clear record and the permanent buff pool.
internal static class Rogue3DScoreSettlement
{
    private static readonly Logger Logger = new("Rogue3D");

    /// <summary>tbLog.LevelFinish column "Extend" (LaunchLog.lua:506): 1-based Lua index 35, 0-based 34.</summary>
    private const int LogExtendIndex = 34;

    public static ValueTask<(JsonNode Payload, NtfSyncPlayer? Sync)> HandleBuffLevelSettlementAsync(
        PlayerInstance player,
        JsonNode? tbParam)
    {
        var payload = tbParam?.DeepClone() as JsonObject ?? new JsonObject();
        payload["tbMonsterDropRiki"] = new JsonArray();
        payload["tbUavPart"] = new JsonArray();
        payload["tbAward"] = new JsonArray();

        var waves = ReadUint(tbParam, "nWaves");
        if (waves == 0)
        {
            // The buff mode parks the wave count in the log's Extend column (Rogue3DScoreModel.lua:1156).
            waves = ReadLogExtend(tbParam);
        }

        payload["nWaves"] = waves;

        // The permanent buff pool. The client decides what counts as "newly unlocked" from
        // tbSaveBuff alone - Rogue3DBuffBattleLogic.lua:46-53 walks BuffGrind_LevelEnd_GetAllCarriedBuffs()
        // and checks Rogue3DLogic:GetBuffRikiFlag(id) == 0 - so the carried list is exactly the set to
        // record here. tbSelectBuff (the buffs merely picked up during the run) is deliberately not
        // written: the client never treats those as unlocks, and writing them would silently grow the pool
        // past what its own "new buff" panel reports.
        var sync = new NtfSyncPlayer();
        uint unlocked = 0;
        foreach (var buffId in ReadUints(tbParam, "tbSaveBuff"))
        {
            var slot = buffId / AttrIds.Rogue3D.ScoreModelBuffBitPerSid;
            if (AttrIds.Rogue3D.ScoreModelBuffRikiStartSid + slot > AttrIds.Rogue3D.ScoreModelBuffRikiEndSid)
                continue;

            var attr = player.Attributes.GetOrCreate(
                AttrIds.Rogue3D.Gid, AttrIds.Rogue3D.ScoreModelBuffRikiStartSid + slot);
            var mask = 1u << (int)(buffId % AttrIds.Rogue3D.ScoreModelBuffBitPerSid);
            if ((attr.Val & mask) != 0)
                continue;

            attr.Val |= mask;
            player.Attributes.SyncTo(sync, attr);
            unlocked++;
        }

        if (unlocked > 0)
            DatabaseHelper.SaveDatabaseType(player.Data);

        Logger.Info(
            $"Rogue3D buff-mode settlement for uid={player.Uid}: waves={waves}, " +
            $"{unlocked} carried buff(s) added to the permanent pool" +
            (unlocked > 0 ? $"; stored at (gid={AttrIds.Rogue3D.Gid}, sid {AttrIds.Rogue3D.ScoreModelBuffRikiStartSid}.." +
                $"{AttrIds.Rogue3D.ScoreModelBuffRikiEndSid})." : "."));

        return ValueTask.FromResult<(JsonNode, NtfSyncPlayer?)>(
            (payload, unlocked > 0 ? sync : null));
    }

    public static ValueTask<(JsonNode Payload, NtfSyncPlayer? Sync)> HandleScoreLevelSettlementAsync(
        PlayerInstance player,
        JsonNode? tbParam)
    {
        var battleId = ReadUint(tbParam, "nBattleId");
        var payload = tbParam?.DeepClone() as JsonObject ?? new JsonObject();
        payload["tbMonsterDropRiki"] = new JsonArray();
        payload["tbAward"] = new JsonArray();

        // Rogue3DLogic.lua:1151-1160 GetLevelScore(battleId) reads (gid 124, 2401 + battleId - 1) and the
        // bank only spans battle ids 1..10; anything else is answered with zeros instead of a slot write.
        var slot = battleId - 1;
        if (battleId == 0 || slot >= AttrIds.Rogue3D.ScoreModelLevelScoreEndSid - AttrIds.Rogue3D.ScoreModelLevelScoreStartSid + 1)
        {
            payload["nOldScore"] = 0;
            payload["nNewScore"] = 0;
            Logger.Info(
                $"Rogue3D score-mode settlement for uid={player.Uid} carried battle id {battleId}, which has " +
                "no score slot; answered with zeros.");
            return ValueTask.FromResult<(JsonNode, NtfSyncPlayer?)>((payload, null));
        }

        var sid = AttrIds.Rogue3D.ScoreModelLevelScoreStartSid + slot;
        var oldScore = player.Attributes.GetValue(AttrIds.Rogue3D.Gid, sid);
        // The score is not a payload field: Rogue3DScoreModel.lua:1282 hands nScore to LaunchLog.LogLevel as
        // its "extend" column, which lands at tbLog.LevelFinish[35] (LaunchLog.lua:506 - the string "NULL"
        // when the caller passes nothing, hence the parse guard in ReadLogExtend).
        var score = ReadLogExtend(tbParam);
        var newScore = Math.Max(oldScore, score);

        payload["nOldScore"] = oldScore;
        payload["nNewScore"] = newScore;

        if (newScore <= oldScore)
        {
            Logger.Info(
                $"Rogue3D score battle {battleId} for uid={player.Uid} scored {score}, below the stored " +
                $"best {oldScore}; no record written.");
            return ValueTask.FromResult<(JsonNode, NtfSyncPlayer?)>((payload, null));
        }

        var attr = player.Attributes.GetOrCreate(AttrIds.Rogue3D.Gid, sid);
        attr.Val = newScore;
        var sync = new NtfSyncPlayer();
        player.Attributes.SyncTo(sync, attr);
        DatabaseHelper.SaveDatabaseType(player.Data);

        Logger.Info(
            $"Rogue3D score battle {battleId} for uid={player.Uid}: score {oldScore} -> {newScore} " +
            $"(gid={AttrIds.Rogue3D.Gid} sid={sid}).");

        return ValueTask.FromResult<(JsonNode, NtfSyncPlayer?)>((payload, sync));
    }

    private static uint ReadLogExtend(JsonNode? node)
    {
        if (node is not JsonObject obj ||
            obj["tbLog"] is not JsonObject log ||
            log["LevelFinish"] is not JsonArray finish ||
            finish.Count <= LogExtendIndex)
        {
            return 0;
        }

        var raw = finish[LogExtendIndex]?.ToString().Trim().Trim('[', ']', '"');
        return uint.TryParse(raw, out var value) ? value : 0;
    }

    private static IEnumerable<uint> ReadUints(JsonNode? node, string key)
    {
        if (node is not JsonObject obj || obj[key] is not JsonArray array)
            yield break;

        foreach (var item in array)
        {
            var raw = item?.ToString().Trim().Trim('[', ']', '"');
            if (uint.TryParse(raw, out var value))
                yield return value;
        }
    }

    private static uint ReadUint(JsonNode? node, string key)
    {
        if (node is not JsonObject obj)
            return 0;

        var raw = obj[key]?.ToString().Trim().Trim('[', ']', '"');
        return uint.TryParse(raw, out var value) ? value : 0;
    }
}
