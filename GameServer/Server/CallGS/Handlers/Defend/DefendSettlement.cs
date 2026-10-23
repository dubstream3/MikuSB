using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Defend;

/// <summary>
/// 防御活动 (defend) level settlements. They go out over the shared reconnect channel
/// (Misc/Reconnect.lua Reconnect.Send_SettleInfo) as Chapter_DealLevelSettlement with
/// sCmd = "DefendLogic_LevelSettlement" (Challenge/Defend/Defend.lua:1020), so they are handled from
/// Chapter_DealLevelSettlement.BuildSettlementPayloadAsync instead of as their own CallGS API.
///
/// Client expectations (Launch/Classes/DefendLogic.lua:27-56): the registered callback throws the
/// payload away and only opens Success/Fail, so the answer just has to arrive without sErr -
/// Reconnect.lua:160-166 force-disconnects the client after five unanswered retries.
///
/// Difficulty progression lives in the player attributes (GroupId=10):
///   DefendLogic.GetLevelPassNum(nDiff)       = GetAttribute(10, 40 + nDiff)   (Defend.lua:349-353,
///   LevelPassStart = 40) - the all-time clear counter the difficulty list locks on
///   (UMG/DungeonsDefend/Widgets/uw_defense_difficulty.lua:71-81).
///   DefendLogic.GetCurActLevelPassNum(nDiff) = GetAttribute(10, 140 + nDiff)  (Defend.lua:356-360,
///   CurActLevelPassStart = 140) - the same counter for the running activity, drawn as the "cleared"
///   badge (uw_defense_difficulty.lua:58-64).
/// challenge/defend/difficult.json gates difficulty 3 on UnlockCondition 2 and 4 on 3 (1 and 2 are
/// free), so a successful run has to bump both slots of its nDiff - that write was missing, which is
/// why every later difficulty stayed locked. Only clears count: Defend.lua:998 sends
/// bSuccess = (nResult == Success and nReason ~= ManualExit), and a failure or a manual exit sends the
/// same settlement with bSuccess = false.
/// </summary>
internal static class DefendSettlement
{
    private static readonly Logger Logger = new("Defend");

    /// <summary>DefendLogic.LevelPassStart: difficulty n is counted at sid (40 + n).</summary>
    private const uint LevelPassStartSid = AttrIds.Defend.LevelPassStartSid;

    /// <summary>Difficulty counter slots the client reserves (LevelPassStart .. LevelPassEnd).</summary>
    private const uint LevelPassSlots = AttrIds.Defend.LevelPassEndSid - AttrIds.Defend.LevelPassStartSid;

    /// <summary>DefendLogic.CurActLevelPassStart: the same counter for the running activity.</summary>
    private const uint CurrentActivityLevelPassStartSid = AttrIds.Defend.CurrentActivityLevelPassStartSid;

    private const uint CurrentActivityLevelPassSlots =
        AttrIds.Defend.CurrentActivityLevelPassEndSid - AttrIds.Defend.CurrentActivityLevelPassStartSid;

    /// <summary>DefendLogic.FightData (Defend.lua:34): the string slot DefendFightSave checkpoints into.</summary>
    private const uint FightDataSid = 1;

    /// <summary>
    /// DefendLogic.MaxLevelScore (Defend.lua:18 = 7): "本期最高御敌积分". The client reads it through
    /// DefendLogic.GetCurActTopScore (Defend.lua:1814-1816) and draws it in umg_defensemain.lua:144.
    /// </summary>
    private const uint MaxLevelScoreSid = AttrIds.Defend.MaxLevelScoreSid;

    /// <summary>
    /// DefendLogic.PassWave (Defend.lua:12 = 2): "最大通过波数", the highest wave the player has ever
    /// passed. DefendLogic.GetMaxWave (Defend.lua:431-432) draws it in umg_defensemain.lua:142 and
    /// uw_defense_action.lua:29, and GetTargetState (Defend.lua:1403-1405) refuses every type 1 target
    /// reward whose Param - the client's nWave (Defend.lua:207) - is above it. Nothing else writes the
    /// slot, so before this the whole wave driven target list stayed locked.
    /// </summary>
    private const uint PassWaveSid = AttrIds.Defend.PassWaveSid;

    public static ValueTask<(JsonNode Payload, NtfSyncPlayer? Sync)> HandleLevelSettlementAsync(
        PlayerInstance player,
        JsonNode? tbParam)
    {
        var levelId = ReadUint(tbParam, "nID");
        var diffId = ReadUint(tbParam, "nDiff");
        var success = ReadBool(tbParam, "bSuccess");
        var sync = new NtfSyncPlayer();
        // The payload always carries the run's score (Defend.lua:1008 nScore = valid kills + full-combo
        // bonus) and the client's settlement callback throws the payload away (Defend.lua:1025-1030),
        // so the only way the panel ever shows 本期最高御敌积分 is this attribute sync.
        var scoreChanged = ApplyMaxScore(player, ReadUint(tbParam, "nScore"), sync);
        // Both a clear and a defeat report the wave the run got through (Defend.lua:995-996 sends
        // GetCurWave() and drops one wave when the run was lost), and PassWave is a "best ever" value,
        // so it is raised on either outcome.
        var waveChanged = ApplyMaxPassWave(player, ReadUint(tbParam, "nWave"), sync);

        if (!success)
        {
            Logger.Info(
                $"Defend level {levelId} diff {diffId} settled for uid={player.Uid} without a clear; " +
                "no difficulty counter written.");
            ClearCheckpoint(player, sync);
            if (scoreChanged || waveChanged)
                DatabaseHelper.SaveDatabaseType(player.Data);
            return ValueTask.FromResult<(JsonNode, NtfSyncPlayer?)>((new JsonObject(), sync));
        }

        if (diffId == 0 || diffId > LevelPassSlots || diffId > CurrentActivityLevelPassSlots)
        {
            Logger.Info(
                $"Defend level {levelId} cleared for uid={player.Uid}, but diff {diffId} is outside the " +
                $"difficulty counter window (1..{LevelPassSlots}); nothing written.");
            ClearCheckpoint(player, sync);
            if (scoreChanged || waveChanged)
                DatabaseHelper.SaveDatabaseType(player.Data);
            return ValueTask.FromResult<(JsonNode, NtfSyncPlayer?)>((new JsonObject(), sync));
        }

        var passAttr = player.Attributes.Add(AttrIds.Defend.Gid, LevelPassStartSid + diffId, 1);
        var activityAttr = player.Attributes.Add(AttrIds.Defend.Gid, CurrentActivityLevelPassStartSid + diffId, 1);
        player.Attributes.SyncTo(sync, passAttr);
        player.Attributes.SyncTo(sync, activityAttr);
        ClearCheckpoint(player, sync);
        DatabaseHelper.SaveDatabaseType(player.Data);

        Logger.Info(
            $"Defend difficulty {diffId} cleared for uid={player.Uid} (level={levelId}); clear counter " +
            $"(gid={AttrIds.Defend.Gid} sid={LevelPassStartSid + diffId}) is now {passAttr.Val}, activity " +
            $"counter (sid={CurrentActivityLevelPassStartSid + diffId}) is now {activityAttr.Val}.");

        return ValueTask.FromResult<(JsonNode, NtfSyncPlayer?)>((new JsonObject(), sync));
    }

    /// <summary>
    /// Drops the mid-run checkpoint DefendFightSave wrote at (gid 10, sid 1). The run is over either way -
    /// cleared or failed - and the client's own reset (Defend.lua:578-592 DefendLogic.ClearFightData ->
    /// DefendLogic_ClearFightData) is commented out in the shipped scripts, so nothing else would ever
    /// remove it; a stale checkpoint would make the next entry resume the finished wave.
    /// </summary>
    private static void ClearCheckpoint(PlayerInstance player, NtfSyncPlayer sync)
    {
        var attr = player.Attributes.SetString(AttrIds.Defend.Gid, FightDataSid, string.Empty);
        player.Attributes.SyncTo(sync, attr);
    }

    /// <summary>
    /// Keeps the highest single-run score of the running period (Defend.lua:18 MaxLevelScore). Every
    /// settlement carries nScore - the run's own score, partial runs included, which is what the client
    /// already credits for a mid-run exit (ui.json:6313) - so the value only ever grows. Returns true
    /// when the stored slot changed, so the caller can persist it.
    /// </summary>
    private static bool ApplyMaxScore(PlayerInstance player, uint score, NtfSyncPlayer sync)
    {
        if (score == 0)
            return false;

        if (score <= player.Attributes.GetValue(AttrIds.Defend.Gid, MaxLevelScoreSid))
            return false;

        var attr = player.Attributes.Set(AttrIds.Defend.Gid, MaxLevelScoreSid, score);
        player.Attributes.SyncTo(sync, attr);

        Logger.Info(
            $"Defend best single-run score for uid={player.Uid} is now {score} " +
            $"(gid={AttrIds.Defend.Gid} sid={MaxLevelScoreSid}).");
        return true;
    }

    /// <summary>
    /// Keeps the highest wave the player ever got through (Defend.lua:12 PassWave). The settlement's
    /// nWave is the run's own wave count, so the slot only ever grows - the same "raise only" shape as
    /// <see cref="ApplyMaxScore"/>. Returns true when the stored slot changed, so the caller can
    /// persist it.
    /// </summary>
    private static bool ApplyMaxPassWave(PlayerInstance player, uint wave, NtfSyncPlayer sync)
    {
        if (wave == 0)
            return false;

        if (wave <= player.Attributes.GetValue(AttrIds.Defend.Gid, PassWaveSid))
            return false;

        var attr = player.Attributes.Set(AttrIds.Defend.Gid, PassWaveSid, wave);
        player.Attributes.SyncTo(sync, attr);

        Logger.Info(
            $"Defend best passed wave for uid={player.Uid} is now {wave} " +
            $"(gid={AttrIds.Defend.Gid} sid={PassWaveSid}).");
        return true;
    }

    private static uint ReadUint(JsonNode? node, string key)
    {
        if (node is not JsonObject obj)
            return 0;

        var raw = obj[key]?.ToString().Trim().Trim('[', ']', '"');
        return uint.TryParse(raw, out var value) ? value : 0;
    }

    private static bool ReadBool(JsonNode? node, string key)
    {
        if (node is not JsonObject obj)
            return false;

        var raw = obj[key]?.ToString().Trim();
        return raw is "true" or "True" or "1";
    }
}
