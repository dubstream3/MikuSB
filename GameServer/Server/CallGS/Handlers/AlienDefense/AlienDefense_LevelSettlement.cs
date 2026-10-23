using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.AlienDefense;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.AlienDefense;

// 异星守护 (alien defense / Mow) clear settlement. AlienDefense.LevelSettlement
// (DLC/AlienDefense/AlienDefense.lua:942-979) is sent through Reconnect.Send_SettleInfo, so it may
// be replayed once after a reconnect; the callback (:981-993) keeps tbUnlockPal/tbUnlockGirl only
// when they are non-empty and always Launch.Responses into the settlement UI.
//
//     { nID, nSeed, tbKillInfo, nScore, nWave, nGirlKillNum, nGirlSkillNum, tbLog }
//
// nScore/nWave are non-zero only for endless (type 3) runs (mgr:GetTotalScore/GetCurWaveNumber).
// The client never writes its own score attributes, so the server is authoritative for:
//
//   * (143,2) Score        -- cumulative endless score; the client predicts the new total as
//                             GetScore() + nScore in its fight log (AlienDefense.lua:899)
//   * (143,6) MaxScore     -- best single-run score (bits 0..15) and max wave (bits 16..31),
//                             mirroring GetMaxScoreAndWave; wave >= 25 unlocks endless mode
//   * (143,10..110)        -- per-pal kill counts: subId = palIdx + 10 + (levelType-1) * 20,
//                             mirroring GetKillNum (tbKillInfo is keyed by pal index)
//   * (143,351..450)       -- the fielded girl's kill count for this level type
//   * (143,601..610)       -- the fielded girl's skill-cast count
//   * (143,111..250)       -- per-pal clear counts for the levels that pal fought
//   * (143,451..550)       -- the same for the fielded girl
//   * (22, nID)             -- per-level clear counter (Launch.GPASSID == 22); see AddPassTime
//   * (7, questId)          -- the Type-2 activity_quest row naming nID in Param, finished via
//                             AlienDefenseQuestService
//
// The per-roster pass-time blocks are NOT written by the client: the client's own AddPassTime has
// no call site anywhere under Game/Content/Script, so (143, 111..250) / (143, 451..550) stayed zero
// and every condition row carrying the e == 1 flag ([1, levelId, n, 1]) was permanently unmet.
//
// tbUnlockPal/tbUnlockGirl used to be returned empty on the assumption that the client evaluates
// CheckPalCondition itself. It does not: the handbook only ever reads bit0 of (143, 251 + Id) /
// (143, 551 + Id) (AlienDefense.GetPalUnlockState, AlienDefense.lua:329-359) and there was no writer
// anywhere, so every companion and support girl stayed locked. AlienDefenseUnlockService now
// evaluates the roster conditions, sets the bits and reports the ids that flipped; the client keeps
// the lists only when they are non-empty (:985-990) and opens the unlock popups from them.
//
// Request:  { nID, nSeed, tbKillInfo, nScore, nWave, ... }
// Response: { tbUnlockPal: [], tbUnlockGirl: [] }
[CallGSApi("AlienDefense_LevelSettlement")]
public class AlienDefense_LevelSettlement : CallGSHandler<AlienDefenseLevelSettlementParam>
{
    private static readonly Logger Log = new(nameof(AlienDefense_LevelSettlement));

    protected override Task<CallGSResult> HandleAsync(
        CallGSContext context,
        AlienDefenseLevelSettlementParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var (payload, sync) = Apply(player, request);
        return Task.FromResult(CallGSResult.Ok(payload, sync));
    }

    /// <summary>
    /// Entry point for the wrapped settlement channel. AlienDefense.LevelSettlement routes through
    /// Reconnect.Send_SettleInfo, so the request actually arrives as Chapter_DealLevelSettlement with
    /// sCmd = "AlienDefense_LevelSettlement" -- the direct CallGS API registered above is never hit
    /// by the live client. Chapter_DealLevelSettlement calls this to share one implementation.
    /// </summary>
    internal static (JsonNode Payload, NtfSyncPlayer? Sync) HandleSettlement(
        PlayerInstance player,
        JsonNode? tbParam)
    {
        var request = tbParam?.Deserialize<AlienDefenseLevelSettlementParam>(CallGSJson.Options);
        if (request == null || request.LevelId == 0)
            return (new JsonObject { ["sErr"] = "error.BadParam" }, null);

        return Apply(player, request);
    }

    private static (JsonObject Payload, NtfSyncPlayer Sync) Apply(
        PlayerInstance player,
        AlienDefenseLevelSettlementParam request)
    {
        var uid = player.Uid;
        var sync = new NtfSyncPlayer();

        var actId = player.Attributes.GetValue(AttrIds.AlienDefense.Gid, AttrIds.AlienDefense.ActivitySid);
        var row = Data.Excel.AlienDefenseExcel.FindByLevel(request.LevelId, actId);
        var levelType = row?.FindLevelType(request.LevelId) ?? 0;

        var questFinished = AlienDefenseQuestService.MarkLevelCleared(player, request.LevelId, sync);
        var passCounted = AddPassTime(player, request.LevelId, sync);
        var scoreAdded = AddScore(player, request.Score, sync);
        var bestUpdated = UpdateBest(player, request.Score, request.Wave, sync);
        var killsRecorded = RecordKills(player, levelType, request.KillInfo, sync);
        var girlCounters = RecordGirlCounters(player, levelType, request, sync);
        var rosterPassTimes = RecordPassTimes(player, row, request.LevelId, sync);
        var (unlockedPals, unlockedGirls, unlocksChanged) = AlienDefenseUnlockService.GrantUnlocks(player, sync);

        DatabaseHelper.SaveDatabaseType(player.Data);

        Log.Info($"Alien defense level {request.LevelId} settled by uid={uid} " +
                 $"(act={actId}, type={levelType}, score={request.Score}, wave={request.Wave}, " +
                 $"seed={request.Seed}, questFinished={questFinished}, passCounted={passCounted}, " +
                 $"scoreAdded={scoreAdded}, bestUpdated={bestUpdated}, killsRecorded={killsRecorded}, " +
                 $"girlKill={request.GirlKillNum}, girlSkill={request.GirlSkillNum}, " +
                 $"girlCounters={girlCounters}, rosterPassTimes={rosterPassTimes}, " +
                 $"unlocksChanged={unlocksChanged}).");

        return (new JsonObject
        {
            ["tbUnlockPal"] = unlockedPals,
            ["tbUnlockGirl"] = unlockedGirls
        }, sync);
    }

    /// <summary>
    /// Increments the per-level clear counter (22, nID) -- Launch.GPASSID. The client reads that
    /// slot through AlienDefenseLevel.GetPassTime (AlienDefenseLevel.lua:44-47) for
    /// IsFirstPass/IsPass/IsCompleted, and Condition.lua:351 hands the same (Launch.GPASSID, levelId)
    /// pair to condition type 1 ("cleared N times"), which is what alienaward girl rows such as
    /// Condition = [1, 6231, 1] evaluate. Nothing in the client scripts ever calls the matching
    /// AddPassTime (grep for "AddPassTime(" over Game/Content/Script returns no call site), so the
    /// server is the only writer -- without this bump IsPass() stays false forever and every
    /// pass-gated unlock stalls.
    /// </summary>
    private static bool AddPassTime(PlayerInstance player, uint levelId, NtfSyncPlayer sync)
    {
        if (levelId == 0)
            return false;

        var attr = player.Attributes.GetOrCreate(AttrIds.Launch.LevelPassGid, levelId);
        attr.Val += 1;
        player.Attributes.SyncTo(sync, attr);
        return true;
    }

    /// <summary>
    /// Accumulates the run's endless score into (143,2). Normal/challenge runs report nScore 0 and
    /// leave the total untouched.
    /// </summary>
    private static bool AddScore(PlayerInstance player, uint score, NtfSyncPlayer sync)
    {
        if (score == 0)
            return false;

        var attr = player.Attributes.GetOrCreate(AttrIds.AlienDefense.Gid, AttrIds.AlienDefense.ScoreSid);
        attr.Val += score;
        player.Attributes.SyncTo(sync, attr);
        return true;
    }

    /// <summary>
    /// Tracks the best single-run score (bits 0..15) and max wave (bits 16..31) in (143,6),
    /// mirroring GetMaxScoreAndWave. Each half only ever moves up.
    /// </summary>
    private static bool UpdateBest(PlayerInstance player, uint score, uint wave, NtfSyncPlayer sync)
    {
        if (score == 0 && wave == 0)
            return false;

        var attr = player.Attributes.GetOrCreate(AttrIds.AlienDefense.Gid, AttrIds.AlienDefense.MaxScoreSid);
        var bestScore = Math.Min(score, 0xFFFFu);
        var bestWave = Math.Min(wave, 0xFFFFu);
        var merged = Math.Max(attr.Val & 0xFFFFu, bestScore) | (Math.Max((attr.Val >> 16) & 0xFFFFu, bestWave) << 16);
        if (merged == attr.Val)
            return false;

        attr.Val = merged;
        player.Attributes.SyncTo(sync, attr);
        return true;
    }

    /// <summary>
    /// Accumulates per-pal kill counts into (143,10..110), mirroring GetKillNum:
    /// subId = palIdx + 10 + (levelType-1) * 20. tbKillInfo is keyed by pal index with kill counts
    /// as values; entries outside 1..20 or from an unknown level type are ignored. Girl kills and
    /// skill counts are aggregated by the client without a per-girl key and are attributed to the
    /// fielded girl by RecordGirlCounters instead.
    /// </summary>
    private static int RecordKills(
        PlayerInstance player,
        uint levelType,
        JsonObject? killInfo,
        NtfSyncPlayer sync)
    {
        if (killInfo == null || levelType == 0 || levelType > AttrIds.AlienDefense.MaxTypeNum)
            return 0;

        var recorded = 0;
        foreach (var (key, value) in killInfo)
        {
            if (!uint.TryParse(key, out var palIdx) ||
                palIdx == 0 || palIdx > AttrIds.AlienDefense.MaxPalNum)
                continue;

            if (!TryReadUint(value, out var kills) || kills == 0)
                continue;

            var subId = palIdx + AttrIds.AlienDefense.PalKillNumStartSid +
                        (levelType - 1) * AttrIds.AlienDefense.MaxPalNum;
            if (subId > AttrIds.AlienDefense.PalKillNumEndSid)
                continue;

            var attr = player.Attributes.GetOrCreate(AttrIds.AlienDefense.Gid, subId);
            attr.Val += kills;
            player.Attributes.SyncTo(sync, attr);
            recorded++;
        }

        return recorded;
    }

    private static bool TryReadUint(JsonNode? node, out uint value)
    {
        value = 0;
        if (node == null)
            return false;

        try
        {
            value = node.GetValue<uint>();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Girl kill and skill-cast counters. AlienDefense.LevelSettlement aggregates the whole run
    /// without a per-girl key (AlienDefense.lua:950-964): nGirlKillNum sums every skill kill plus
    /// the fielded girl's own totalKillCount, and nGirlSkillNum is the number of waves her skill was
    /// cast. The fielded girl is (143,7) -- the roster index picked in uw_mow_girl.lua:70 and stored
    /// by AlienDefense_SaveFightGirl -- so both land on her own rows:
    ///
    ///   * kills  at GirlKillNumStartSid + idx + (levelType-1) * MaxGirlNum  (GetKillNum :140)
    ///   * casts  at GirlSkillNumStartSid + idx - 1                          (GetGirlSkillNum :294-298)
    ///
    /// Without these, condition rows of the form [2, type, n] and [3, ...] could never be met and
    /// the matching handbook entries stayed locked (AlienDefenseUnlockService only evaluates the
    /// clear-count form [1, levelId, n, ...]).
    /// </summary>
    private static bool RecordGirlCounters(
        PlayerInstance player,
        uint levelType,
        AlienDefenseLevelSettlementParam request,
        NtfSyncPlayer sync)
    {
        if (levelType == 0 || levelType > AttrIds.AlienDefense.MaxTypeNum)
            return false;
        if (request.GirlKillNum == 0 && request.GirlSkillNum == 0)
            return false;

        var girlIdx = player.Attributes.GetValue(AttrIds.AlienDefense.Gid, AttrIds.AlienDefense.FightGirlSid);
        if (girlIdx == 0 || girlIdx > AttrIds.AlienDefense.MaxGirlNum)
        {
            Log.Warn($"Alien defense level {request.LevelId} reported girl kills/skills " +
                     $"({request.GirlKillNum}/{request.GirlSkillNum}) for uid={player.Uid} but no " +
                     $"valid fielded girl is stored in ({AttrIds.AlienDefense.Gid}," +
                     $"{AttrIds.AlienDefense.FightGirlSid}) -- counters dropped.");
            return false;
        }

        var changed = false;

        if (request.GirlKillNum != 0)
        {
            var subId = girlIdx + AttrIds.AlienDefense.GirlKillNumStartSid +
                        (levelType - 1) * AttrIds.AlienDefense.MaxGirlNum;
            if (subId <= AttrIds.AlienDefense.GirlKillNumEndSid)
            {
                var attr = player.Attributes.GetOrCreate(AttrIds.AlienDefense.Gid, subId);
                attr.Val += request.GirlKillNum;
                player.Attributes.SyncTo(sync, attr);
                changed = true;
            }
        }

        if (request.GirlSkillNum != 0)
        {
            var subId = AttrIds.AlienDefense.GirlSkillNumStartSid + girlIdx - 1;
            if (subId <= AttrIds.AlienDefense.GirlSkillNumEndSid)
            {
                var attr = player.Attributes.GetOrCreate(AttrIds.AlienDefense.Gid, subId);
                attr.Val += request.GirlSkillNum;
                player.Attributes.SyncTo(sync, attr);
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>
    /// Per-roster clear counters -- the (143,111..250) pal block and (143,451..550) girl block that
    /// AlienDefense.GetPassTime (:148-166) reads for condition rows carrying the e == 1 flag
    /// (CheckPalCondition :178-179). The client never writes them (no AddPassTime call site exists
    /// anywhere under Game/Content/Script), so the server derives the roster from the fielded
    /// lineup: (143,5) bits 0..15 / 16..31 for the two pals (GetFightPal :250-261) and (143,7) for
    /// the girl. Only the entries that actually fought are credited.
    /// </summary>
    private static int RecordPassTimes(
        PlayerInstance player,
        Data.Excel.AlienDefenseExcel? row,
        uint levelId,
        NtfSyncPlayer sync)
    {
        if (row == null || levelId == 0)
            return 0;

        var levelIdx = row.GetLevelIdx(levelId);
        if (levelIdx == 0)
            return 0;

        var written = 0;

        var lineup = player.Attributes.GetValue(AttrIds.AlienDefense.Gid, AttrIds.AlienDefense.FightPalSid);
        var pal1 = lineup & 0xFFFFu;
        var pal2 = (lineup >> 16) & 0xFFFFu;
        if (pal1 != 0)
        {
            written += BumpRosterPassTime(player, sync, AttrIds.AlienDefense.PalPassTimeStartSid,
                AttrIds.AlienDefense.PalPassTimeEndSid, AttrIds.AlienDefense.MaxPalNum, levelIdx, pal1);
        }

        if (pal2 != 0)
        {
            written += BumpRosterPassTime(player, sync, AttrIds.AlienDefense.PalPassTimeStartSid,
                AttrIds.AlienDefense.PalPassTimeEndSid, AttrIds.AlienDefense.MaxPalNum, levelIdx, pal2);
        }

        var girlIdx = player.Attributes.GetValue(AttrIds.AlienDefense.Gid, AttrIds.AlienDefense.FightGirlSid);
        if (girlIdx != 0)
        {
            written += BumpRosterPassTime(player, sync, AttrIds.AlienDefense.GirlPassTimeStartSid,
                AttrIds.AlienDefense.GirlPassTimeEndSid, AttrIds.AlienDefense.MaxGirlNum, levelIdx, girlIdx);
        }

        return written;
    }

    /// <summary>
    /// Adds one clear to a roster entry's 8-bit field inside the packed pass-time block, using the
    /// exact slot/bit arithmetic of AlienDefense.GetPassTime: step = ceil(maxNum / 4) slots per
    /// level, subId = start + (levelIdx-1) * step + idx / 4, bitStart = ((idx-1) % 4) * 8.
    /// </summary>
    private static int BumpRosterPassTime(
        PlayerInstance player,
        NtfSyncPlayer sync,
        uint startSid,
        uint endSid,
        uint maxNum,
        uint levelIdx,
        uint rosterIdx)
    {
        if (rosterIdx == 0 || rosterIdx > maxNum)
            return 0;

        var step = (maxNum + 3) / 4;
        var subId = startSid + (levelIdx - 1) * step + rosterIdx / 4;
        if (subId > endSid)
            return 0;

        var bitStart = (int)((rosterIdx - 1) % 4) * 8;
        var attr = player.Attributes.GetOrCreate(AttrIds.AlienDefense.Gid, subId);
        var current = (attr.Val >> bitStart) & 0xFFu;
        if (current == 0xFFu)
            return 0;

        var next = current + 1;
        attr.Val = (attr.Val & ~(0xFFu << bitStart)) | (next << bitStart);
        player.Attributes.SyncTo(sync, attr);
        return 1;
    }
}

public sealed class AlienDefenseLevelSettlementParam
{
    [JsonPropertyName("nID")] public uint LevelId { get; set; }
    [JsonPropertyName("nSeed")] public uint Seed { get; set; }
    [JsonPropertyName("tbKillInfo")] public JsonObject? KillInfo { get; set; }
    [JsonPropertyName("nScore")] public uint Score { get; set; }
    [JsonPropertyName("nWave")] public uint Wave { get; set; }
    [JsonPropertyName("nGirlKillNum")] public uint GirlKillNum { get; set; }
    [JsonPropertyName("nGirlSkillNum")] public uint GirlSkillNum { get; set; }
    [JsonPropertyName("tbLog")] public JsonNode? Log { get; set; }
}
