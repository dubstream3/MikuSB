using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.SeasonBoss;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.SeasonBoss;

// 弑神斗场 (season boss) clear settlement. SeasonBossLogic.Req_LevelSettlement
// (Challenge/SeasonBoss/SeasonBoss.lua:1387-1406) is sent through Reconnect.Send_SettleInfo, so it may
// be replayed once after a reconnect; the callback (:1411-1419) only needs the absence of an "err" key
// to open the Success UI.
//
//     { nID, nSeed, PartNum, nTime, nDiff, nZoneID, tbLog, tbKill, tbMonster, tbEntrie,
//       nSkillKill, tbBeSkillKill, nNoDamaged, nExitNum, nDamage }
//
// What the client expects the server to have advanced (all read back from attribute group 18):
//   * (18,3) LevelIndex      -- unlocked difficulty, packed zone1 in the hundreds / zone2 in the units
//                               (SeasonBoss.lua:539-557 Get/SetUnlockMaxLevelIndex)
//   * (18,6) / (18,7)        -- historical max difficulty of zone 1 / zone 2, bits 0..15 for term 1 and
//                               bits 16..31 for term 2 (SeasonBoss.lua:836-845 GetHistoryMaxLevelIndex)
//   * (26, 10*nID + 100 + k) -- per level string stats: +1..+3 the best run's role snapshots, +4 best
//                               score, +5 clear time, +6 clear count, +7 challenge count
//                               (SeasonBoss.lua:560-586, SubNum=10 StartSID=100 SGID=26).
//                               +1..+3 and +7 are maintained by SeasonBossRunService; +7 is bumped on
//                               entry rather than here.
// The quest row behind the level (seasonleveldes ReferAward) is finished on (7, questId), which is what
// makes the reward claimable in the mode's own reward panel.
//
// Request:  { nID, nSeed, nDiff, nZoneID, nDamage, nTime, ... }
// Response: {} (any payload without "err" opens the Success UI)
[CallGSApi("SeasonBoss_LevelSettlement")]
public class SeasonBoss_LevelSettlement : CallGSHandler<SeasonBossLevelSettlementParam>
{
    private static readonly Logger Log = new(nameof(SeasonBoss_LevelSettlement));

    private const uint MaxDifficulty = 5;

    protected override Task<CallGSResult> HandleAsync(
        CallGSContext context,
        SeasonBossLevelSettlementParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var (payload, sync) = Apply(player, request);
        return Task.FromResult(CallGSResult.Ok(payload, sync));
    }

    /// <summary>
    /// Entry point for the wrapped settlement channel. SeasonBossLogic.Req_LevelSettlement routes through
    /// Reconnect.Send_SettleInfo, so the request actually arrives as Chapter_DealLevelSettlement with
    /// sCmd = "SeasonBoss_LevelSettlement" -- the direct CallGS API registered above is never hit by the
    /// live client. Chapter_DealLevelSettlement calls this to share one implementation.
    /// </summary>
    internal static (JsonNode Payload, NtfSyncPlayer? Sync) HandleSettlement(
        PlayerInstance player,
        JsonNode? tbParam)
    {
        var request = tbParam?.Deserialize<SeasonBossLevelSettlementParam>(CallGSJson.Options);
        if (request == null || request.LevelId == 0)
            return (new JsonObject { ["sErr"] = "error.BadParam" }, null);

        return Apply(player, request);
    }

    private static (JsonObject Payload, NtfSyncPlayer Sync) Apply(
        PlayerInstance player,
        SeasonBossLevelSettlementParam request)
    {
        var uid = player.Uid;
        var sync = new NtfSyncPlayer();

        var questFinished = SeasonBossQuestService.MarkLevelCleared(player, request.LevelId, sync);

        // seasonlist is authoritative for which term/zone a level belongs to; the payload's own
        // nZoneID is only trusted when the level is not listed at all.
        var row = SeasonBossQuestService.ResolveRow(request.LevelId);
        var zone = row != null && row.ZoneId != 0 ? row.ZoneId : request.ZoneId == 2 ? 2u : 1u;
        var termSynced = row != null && SyncTerm(player, row.TermId, sync);

        var difficulty = Math.Clamp(request.Difficulty, 1u, MaxDifficulty);
        var unlocked = AdvanceUnlock(player, zone, difficulty, sync);
        var recorded = AdvanceHistoryMax(player, zone, difficulty, sync);
        RecordLevelStats(player, request, sync);

        DatabaseHelper.SaveDatabaseType(player.Data);

        Log.Info($"SeasonBoss level {request.LevelId} settled by uid={uid} " +
                 $"(zone={zone}, diff={difficulty}, damage={request.Damage}, time={request.Time}, " +
                 $"seed={request.Seed}, questFinished={questFinished}, termSynced={termSynced}, " +
                 $"unlocked={unlocked}, recorded={recorded}).");

        return (new JsonObject(), sync);
    }

    /// <summary>
    /// Keeps (18,2) pointing at the term the cleared level belongs to. The client reads it back through
    /// GetCurTermID to pick the visible zone list and to choose the 16 bit half of the historical max
    /// attributes (SeasonBoss.lua:836-845), so clearing a 冥域 level has to move it to 2 or the second
    /// half of the season can never report progress.
    /// </summary>
    private static bool SyncTerm(PlayerInstance player, uint term, NtfSyncPlayer sync)
    {
        if (term == 0)
            return false;

        var attr = player.Attributes.GetOrCreate(AttrIds.SeasonBoss.Gid, AttrIds.SeasonBoss.TermSid);
        if (attr.Val == term)
            return false;

        attr.Val = term;
        player.Attributes.SyncTo(sync, attr);
        return true;
    }

    /// <summary>
    /// Raises (18,3) so the next difficulty of the zone becomes selectable. The client's
    /// CheckLevelUnlock (SeasonBoss.lua:847-864) treats index n as open while
    /// GetUnlockMaxLevelIndex(zone) + 1 >= n, so clearing index d has to store d.
    /// </summary>
    private static bool AdvanceUnlock(PlayerInstance player, uint zone, uint difficulty, NtfSyncPlayer sync)
    {
        var attr = player.Attributes.GetOrCreate(AttrIds.SeasonBoss.Gid, AttrIds.SeasonBoss.LevelIndexSid);
        var value = attr.Val;

        // Mirrors SetUnlockMaxLevelIndex (SeasonBoss.lua:548-557): zone 1 lives in the hundreds,
        // zone 2 in the units.
        if (zone == 1)
        {
            if (difficulty <= value / 100)
                return false;

            attr.Val = value % 100 + difficulty * 100;
        }
        else
        {
            if (difficulty <= value % 100)
                return false;

            attr.Val = value / 100 * 100 + difficulty;
        }

        player.Attributes.SyncTo(sync, attr);
        return true;
    }

    /// <summary>
    /// Raises the zone's historical max difficulty for the current term. Term 1 owns bits 0..15 and
    /// term 2 bits 16..31 of (18,6) / (18,7); the old always-on (18,4) / (18,5) pair is explicitly
    /// abandoned by the client (SeasonBoss.lua:15-19), so it is not touched.
    /// </summary>
    private static bool AdvanceHistoryMax(PlayerInstance player, uint zone, uint difficulty, NtfSyncPlayer sync)
    {
        var term = player.Attributes.GetOrCreate(AttrIds.SeasonBoss.Gid, AttrIds.SeasonBoss.TermSid).Val;
        var sid = zone == 2
            ? AttrIds.SeasonBoss.HistoryMaxZone2DifficultySid
            : AttrIds.SeasonBoss.HistoryMaxZone1DifficultySid;

        var attr = player.Attributes.GetOrCreate(AttrIds.SeasonBoss.Gid, sid);
        var shift = term == 2 ? 16 : 0;
        var current = (attr.Val >> shift) & 0xFFFFu;
        if (difficulty <= current)
            return false;

        attr.Val = (attr.Val & ~(0xFFFFu << shift)) | (difficulty << shift);
        player.Attributes.SyncTo(sync, attr);
        return true;
    }

    /// <summary>
    /// Writes the level's per run stats into its string attribute block. The best score (and the lineup
    /// that produced it) is handled by SeasonBossRunService so the settlement and the player's later
    /// "keep this run" confirmation share one implementation; this method covers the best clear time and
    /// the clear count.
    /// </summary>
    private static void RecordLevelStats(
        PlayerInstance player,
        SeasonBossLevelSettlementParam request,
        NtfSyncPlayer sync)
    {
        if (request.LevelId == 0)
            return;

        SeasonBossRunService.RecordBestRun(player, request.LevelId, request.Damage, sync);

        var timeSid = SeasonBossRunService.StatSid(request.LevelId, AttrIds.SeasonBoss.ClearTimeOffset);
        var countSid = SeasonBossRunService.StatSid(request.LevelId, AttrIds.SeasonBoss.ClearCountOffset);

        // nTime is the clear duration as a Lua float, so the smaller value is the better run. 0 means
        // the client did not report one and must not wipe a recorded time.
        var clearTime = (uint)MathF.Round(request.Time);
        if (clearTime > 0)
        {
            var best = SeasonBossRunService.ReadStat(player, timeSid);
            if (best == 0 || clearTime < best)
                SeasonBossRunService.SetStat(player, timeSid, clearTime, sync);
        }

        SeasonBossRunService.SetStat(
            player,
            countSid,
            SeasonBossRunService.ReadStat(player, countSid) + 1,
            sync);
    }
}

public sealed class SeasonBossLevelSettlementParam
{
    [JsonPropertyName("nID")] public uint LevelId { get; set; }
    [JsonPropertyName("nSeed")] public uint Seed { get; set; }
    [JsonPropertyName("nDiff")] public uint Difficulty { get; set; }
    [JsonPropertyName("nZoneID")] public uint ZoneId { get; set; }
    [JsonPropertyName("nDamage")] public uint Damage { get; set; }
    // nTime is SeasonBossLogic.FinishTime, a Lua float (e.g. 29.8). Declaring it as an integer makes
    // System.Text.Json reject the entire request with "The JSON value could not be converted to
    // System.UInt32. Path: $.nTime".
    [JsonPropertyName("nTime")] public float Time { get; set; }
    [JsonPropertyName("PartNum")] public uint PartNum { get; set; }
    [JsonPropertyName("nSkillKill")] public uint SkillKill { get; set; }
    [JsonPropertyName("nNoDamaged")] public uint NoDamaged { get; set; }
    [JsonPropertyName("nExitNum")] public uint ExitNum { get; set; }
    [JsonPropertyName("tbKill")] public JsonNode? Kill { get; set; }
    [JsonPropertyName("tbMonster")] public JsonNode? Monster { get; set; }
    [JsonPropertyName("tbBeSkillKill")] public JsonNode? BeSkillKill { get; set; }
    [JsonPropertyName("tbEntrie")] public JsonNode? Entries { get; set; }
    [JsonPropertyName("tbLog")] public JsonNode? Log { get; set; }
}
