using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.GameServer.Game.Achievement;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Game.Jigsaw;

/// <summary>
/// 适格启示录 (jigsaw) bookkeeping. The client is the only writer of nothing at all here: every piece
/// state, the girl bit, the tuning bit, the hint timestamp and the first-entry timestamp live in
/// attribute group 34 and are read straight back by Activity/JigsawPuzzle.lua (see the constant block
/// at :8-32). Everything is derived from the schedule table, so the server only has to keep the
/// bitfields consistent.
///
/// Piece state packing (JigsawPuzzle.lua:285-315): schedule n occupies 5 consecutive ints starting at
/// sid 100 + (n-1)*5; inside one int, piece index (id-1)%10 sits at bit ((id-1)%10)*3 and is 3 bits
/// wide (0 = not owned, 1 = owned, 2 = placed).
/// </summary>
public static class JigsawPuzzleService
{
    private const uint StateMask = (1u << (int)AttrIds.Jigsaw.PieceStateBits) - 1;

    public static JigsawScheduleExcel? FindSchedule(uint scheduleId)
        => GameData.JigsawScheduleData.GetValueOrDefault(scheduleId);

    public static JigsawPieceExcel? FindPiece(uint scheduleId, uint pieceId)
        => GameData.JigsawPieceData.GetValueOrDefault(scheduleId * 1000 + pieceId);

    public static IReadOnlyList<JigsawPieceExcel> PiecesOf(uint scheduleId)
        => GameData.JigsawPieceData.Values
            .Where(piece => piece.ScheduleId == scheduleId)
            .OrderBy(piece => piece.Position)
            .ToList();

    // ---------------------------------------------------------------- bit fields

    private static uint StateSid(uint scheduleId, uint pieceId)
    {
        var index = pieceId - 1;
        return AttrIds.Jigsaw.PieceStateStartSid +
               (scheduleId - 1) * AttrIds.Jigsaw.PieceStatesPerSchedule +
               index / AttrIds.Jigsaw.PiecesPerStateInt;
    }

    private static int StateShift(uint pieceId)
        => (int)(((pieceId - 1) % AttrIds.Jigsaw.PiecesPerStateInt) * AttrIds.Jigsaw.PieceStateBits);

    public static uint GetPieceState(PlayerInstance player, uint scheduleId, uint pieceId)
    {
        var value = player.Attributes.GetValue(AttrIds.Jigsaw.Gid, StateSid(scheduleId, pieceId));
        return (value >> StateShift(pieceId)) & StateMask;
    }

    public static void SetPieceState(
        PlayerInstance player,
        uint scheduleId,
        uint pieceId,
        uint state,
        NtfSyncPlayer sync)
    {
        var sid = StateSid(scheduleId, pieceId);
        var shift = StateShift(pieceId);
        var attr = player.Attributes.GetOrCreate(AttrIds.Jigsaw.Gid, sid);
        var cleared = attr.Val & ~(StateMask << shift);
        attr.Val = cleared | ((state & StateMask) << shift);
        player.Attributes.SyncTo(sync, attr);
    }

    public static uint CountPlaced(PlayerInstance player, uint scheduleId)
        => (uint)PiecesOf(scheduleId).Count(piece => GetPieceState(player, scheduleId, piece.PieceId) == AttrIds.Jigsaw.StatePlaced);

    public static bool AllPlaced(PlayerInstance player, uint scheduleId)
    {
        var pieces = PiecesOf(scheduleId);
        return pieces.Count > 0 && pieces.All(piece => GetPieceState(player, scheduleId, piece.PieceId) == AttrIds.Jigsaw.StatePlaced);
    }

    public static uint GetGirlBit(uint scheduleId)
        => scheduleId > 30 ? AttrIds.Jigsaw.GirlBitHighSid : AttrIds.Jigsaw.GirlBitLowSid;

    public static int GetGirlShift(uint scheduleId)
        => (int)(scheduleId > 30 ? scheduleId - 30 : scheduleId);

    public static bool HasGirl(PlayerInstance player, uint scheduleId)
        => ((player.Attributes.GetValue(AttrIds.Jigsaw.Gid, GetGirlBit(scheduleId)) >> GetGirlShift(scheduleId)) & 1) != 0;

    public static void SetGirl(PlayerInstance player, uint scheduleId, NtfSyncPlayer sync)
        => SetFlag(player, GetGirlBit(scheduleId), GetGirlShift(scheduleId), sync);

    public static bool IsActive(PlayerInstance player, uint scheduleId)
    {
        var sid = scheduleId > 30 ? AttrIds.Jigsaw.ActiveBitHighSid : AttrIds.Jigsaw.ActiveBitLowSid;
        var shift = GetGirlShift(scheduleId);
        return ((player.Attributes.GetValue(AttrIds.Jigsaw.Gid, sid) >> shift) & 1) != 0;
    }

    public static void SetActive(PlayerInstance player, uint scheduleId, NtfSyncPlayer sync)
    {
        var sid = scheduleId > 30 ? AttrIds.Jigsaw.ActiveBitHighSid : AttrIds.Jigsaw.ActiveBitLowSid;
        SetFlag(player, sid, GetGirlShift(scheduleId), sync);
    }

    private static void SetFlag(PlayerInstance player, uint sid, int shift, NtfSyncPlayer sync)
    {
        var attr = player.Attributes.GetOrCreate(AttrIds.Jigsaw.Gid, sid);
        attr.Val |= 1u << shift;
        player.Attributes.SyncTo(sync, attr);
    }

    // ---------------------------------------------------------------- timestamps

    public static long GetEnterTime(PlayerInstance player, uint scheduleId)
        => player.Attributes.GetValue(AttrIds.Jigsaw.Gid, AttrIds.Jigsaw.EnterTimeStartSid + scheduleId);

    public static void SetEnterTime(PlayerInstance player, uint scheduleId, long now, NtfSyncPlayer? sync)
    {
        if (GetEnterTime(player, scheduleId) > 0)
            return;

        var attr = player.Attributes.GetOrCreate(
            AttrIds.Jigsaw.Gid,
            AttrIds.Jigsaw.EnterTimeStartSid + scheduleId);
        attr.Val = (uint)now;
        if (sync != null)
            player.Attributes.SyncTo(sync, attr);
    }

    public static long GetHelpTime(PlayerInstance player, uint scheduleId)
        => player.Attributes.GetValue(AttrIds.Jigsaw.Gid, AttrIds.Jigsaw.HelpTimeStartSid + scheduleId);

    public static void SetHelpTime(PlayerInstance player, uint scheduleId, long now, NtfSyncPlayer sync)
    {
        var attr = player.Attributes.GetOrCreate(
            AttrIds.Jigsaw.Gid,
            AttrIds.Jigsaw.HelpTimeStartSid + scheduleId);
        attr.Val = (uint)now;
        player.Attributes.SyncTo(sync, attr);
    }

    // ---------------------------------------------------------------- stage / activity

    /// <summary>Activity window start in unix seconds, or 0 when the row is missing.</summary>
    public static long ActivityStart(uint activityId)
    {
        var row = GameData.ActivityData.GetValueOrDefault(activityId);
        return row is null
            ? 0
            : DlcRuntimeOverrides.ToUnixSeconds(DlcRuntimeOverrides.ParseConfigTime(row.StartTime));
    }

    /// <summary>
    /// Phase unlock time (JigsawPuzzle.lua:366-381 GetStageTime): activity start + phase offset, unless the
    /// player entered the activity before it started (then every phase is open at once).
    /// </summary>
    public static long StageTime(PlayerInstance player, uint scheduleId, uint activityId, int stage)
    {
        var schedule = FindSchedule(scheduleId);
        if (schedule is null || stage < 1)
            return long.MaxValue;

        var start = ActivityStart(activityId);
        if (start == 0)
            return 0;

        var enter = GetEnterTime(player, scheduleId);
        var early = enter > 0 && enter < start;
        if (early)
            return start;

        var offsets = schedule.PhaseTimes;
        return start + (stage <= offsets.Count ? offsets[stage - 1] : 0);
    }

    /// <summary>
    /// Maps an activity row to its schedule. The table has no activity column at all, so the only stable
    /// link is activity.CustomData[0] == schedule.RoleID (verified for 390 -> 1, 417 -> 2, 602 -> 3).
    /// </summary>
    public static uint ResolveScheduleForActivity(uint activityId)
    {
        var row = GameData.ActivityData.GetValueOrDefault(activityId);
        if (row is null || !string.Equals(row.Class, "jigsaw_puzzle", StringComparison.Ordinal))
            return 0;

        if (row.CustomData is Newtonsoft.Json.Linq.JArray outer && outer.Count > 0 &&
            outer[0] is Newtonsoft.Json.Linq.JArray first)
        {
            var role = new List<uint>();
            foreach (var node in first)
                role.Add(node.Type == Newtonsoft.Json.Linq.JTokenType.Integer ? (uint)(long)node : 0u);

            foreach (var schedule in GameData.JigsawScheduleData.Values.OrderBy(s => s.ScheduleId))
            {
                var candidate = schedule.Role;
                if (candidate.Count != role.Count)
                    continue;
                if (!candidate.Where((value, index) => value != role[index]).Any())
                    return schedule.ScheduleId;
            }
        }

        return 0;
    }

    /// <summary>Latest schedule (fallback when no activity row is pinned/open).</summary>
    public static uint LatestSchedule()
        => GameData.JigsawScheduleData.Keys.Count == 0 ? 0 : GameData.JigsawScheduleData.Keys.Max();

    // ---------------------------------------------------------------- quests

    /// <summary>
    /// Pushes jigsaw quest progress. Quest types are only defined in the table (the client never sends
    /// progress): 376 = "place N pieces", 377 Param=[schedule, phase] = "finish phase N", 378 = "tune
    /// the completed puzzle". 377/378 are B-level: the table gives no wording, so phase N is satisfied
    /// once every piece of phases 1..N is placed.
    /// </summary>
    public static void ReportProgress(PlayerInstance player, uint scheduleId, NtfSyncPlayer sync)
    {
        var schedule = FindSchedule(scheduleId);
        if (schedule is null)
            return;

        var placed = CountPlaced(player, scheduleId);
        foreach (var taskId in schedule.TaskIds)
        {
            var quest = GameData.ActivityQuestData.GetValueOrDefault(taskId);
            if (quest is null)
                continue;

            var checkValue = quest.CheckValue;
            switch (quest.Type)
            {
                case 376:
                    AchievementClaimService.Complete(
                        player, AchievementClaimService.QuestGroupId, taskId, placed, sync);
                    break;

                case 377 when ReadParams(quest.Param) is { Count: >= 2 } parameters:
                    if (PhasePlaced(player, schedule, parameters[1]))
                    {
                        AchievementClaimService.Complete(
                            player, AchievementClaimService.QuestGroupId, taskId, checkValue, sync);
                    }

                    break;

                case 378:
                    if (IsActive(player, scheduleId))
                    {
                        AchievementClaimService.Complete(
                            player, AchievementClaimService.QuestGroupId, taskId, checkValue, sync);
                    }

                    break;
            }
        }
    }

    /// <summary>activity_quest.Param as a plain uint list ([schedule, phase] for type 377).</summary>
    private static List<uint> ReadParams(Newtonsoft.Json.Linq.JToken? token)
    {
        var result = new List<uint>();
        if (token is not Newtonsoft.Json.Linq.JArray array)
            return result;

        foreach (var node in array)
        {
            if (node.Type == Newtonsoft.Json.Linq.JTokenType.Integer)
                result.Add((uint)(long)node);
        }

        return result;
    }

    private static bool PhasePlaced(PlayerInstance player, JigsawScheduleExcel schedule, uint phase)
    {
        if (phase == 0 || phase > schedule.PhasePieces.Count)
            return false;

        for (var index = 0; index < phase; index++)
        {
            foreach (var pieceId in schedule.PhasePieces[index])
            {
                if (GetPieceState(player, schedule.ScheduleId, pieceId) != AttrIds.Jigsaw.StatePlaced)
                    return false;
            }
        }

        return true;
    }
}
