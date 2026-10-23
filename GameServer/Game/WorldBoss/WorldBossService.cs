using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Achievement;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Game.WorldBoss;

// 世界BOSS (DLC34 `Worldboss=1`). Contract: docs/contracts/dlc-minigames.md §3 and
// .temp/notes/worldboss-contract.md (the full client reading).
//
// The module is a single player "damage race" against one shared boss: the client reads its served
// pair from (179,0)/(179,1), looks the act up as tbBossListCfg[Id][ActID], spends one challenge per
// enter, and reports its damage through the wrapped settlement channel. Nothing here needs a DS or
// another player, so the whole loop - open -> enter -> settle -> task reward -> rank - can be served
// by attributes alone.
//
// Attribute layout (Challenge/WorldBoss/WorldBoss.lua:21-29, mirrored in AttrIds.WorldBoss):
//   (179,0) activity id      (179,1) act id       (179,2) remaining challenges
//   (179,3) last refresh day
//   per attempt block i (0 based), base = 1000*(act-1) + 10*i:
//     base+1 challenges, base+2 damage, base+3..+5 lineup, base+6 activity id, base+7 act id,
//     base+8 recorded flag
//   (181, ReferRank) 0 = never joined, 1 = joined / mail pending, 2 = mail sent.
//
// The two shipped acts (worldbosslist.json Id=1) both live in Dec 2025 / Jan 2026, i.e. before the
// served era; the client's own window check goes through the global IsInTime, which DlcWindowFix and
// GlobalTimeWindows already relax, so the server only has to publish a valid pair and keep the
// challenge count topped up.
public static class WorldBossService
{
    private static readonly Logger Log = new(nameof(WorldBossService));

    /// <summary>Attempts granted per day; the 382 quest line checks 1/2/3 participations.</summary>
    public const uint DailyChallenges = 3;

    private const uint MaxAttempts = 100;
    private const uint QuestGroupId = AttrIds.Achievement.QuestGroupId;
    private const uint PercentTaskType = 380;
    private const uint DamageTaskType = 381;
    private const uint ParticipateTaskType = 382;

    /// <summary>
    /// The served act: the row whose [StartTime, EndTime] contains <paramref name="now"/>, else the
    /// newest act that already started, else the last row. Purely time driven so a running server
    /// rolls into act 2 on its own; an all-expired table (the shipped one) resolves to the last act,
    /// which is what the DlcFuncWorldboss button should show under a relaxed window.
    /// </summary>
    public static WorldBossActExcel? SelectAct(DateTime now)
    {
        var rows = GameData.WorldBossActData;
        if (rows.Count == 0)
            return null;

        var nowUnix = DlcRuntimeOverrides.ToUnixSeconds(now);

        foreach (var row in rows)
        {
            var start = DlcRuntimeOverrides.ToUnixSeconds(DlcRuntimeOverrides.ParseConfigTime(row.StartTime));
            var end = DlcRuntimeOverrides.ToUnixSeconds(DlcRuntimeOverrides.ParseConfigTime(row.EndTime));
            if (start > 0 && end >= start && nowUnix >= start && nowUnix <= end)
                return row;
        }

        return rows
            .Where(row => DlcRuntimeOverrides.ToUnixSeconds(DlcRuntimeOverrides.ParseConfigTime(row.StartTime)) <= nowUnix)
            .OrderByDescending(row => DlcRuntimeOverrides.ToUnixSeconds(DlcRuntimeOverrides.ParseConfigTime(row.StartTime)))
            .FirstOrDefault() ?? rows[^1];
    }

    public static WorldBossActExcel? FindAct(uint activityId, uint actId)
        => GameData.WorldBossActData.FirstOrDefault(row => row.ActivityId == activityId && row.ActId == actId);

    public static uint ResolveLevelId(uint stageId)
        => GameData.WorldBossLevelDesData.TryGetValue(stageId, out var des) ? des.LevelId : 0;

    public static uint BossMaxHp(uint stageId)
    {
        var levelId = ResolveLevelId(stageId);
        return GameData.WorldBossLevelData.TryGetValue(levelId, out var level) ? level.BossMaxHp : 0;
    }

    /// <summary>
    /// Publishes the served act on login (and on era switch): the pair the client indexes with, the
    /// daily challenge count and its stamp. Idempotent; only changed values are pushed.
    /// </summary>
    public static void Seed(PlayerInstance player, NtfSyncPlayer? sync)
    {
        var act = SelectAct(DateTime.Now);
        if (act == null)
            return;

        Set(player, AttrIds.WorldBoss.ActivitySid, act.ActivityId, sync);
        Set(player, AttrIds.WorldBoss.ActivityIdSid, act.ActId, sync);

        var today = uint.Parse(DateTime.Now.ToString("yyyyMMdd"));
        var lastDay = player.Attributes.GetValue(AttrIds.WorldBoss.Gid, AttrIds.WorldBoss.RefreshTimeSid);
        if (lastDay != today)
        {
            Set(player, AttrIds.WorldBoss.RefreshTimeSid, today, sync);
            Set(player, AttrIds.WorldBoss.CurrentDataSid, DailyChallenges, sync);
        }
        else if (player.Attributes.GetValue(AttrIds.WorldBoss.Gid, AttrIds.WorldBoss.CurrentDataSid) == 0 &&
                 !HasRecordedAttempt(player, act))
        {
            // A save that spent every challenge and never settled keeps 0; anything else gets the
            // daily grant above. Top up when the day stamp already matches but nothing was spent.
            Set(player, AttrIds.WorldBoss.CurrentDataSid, DailyChallenges, sync);
        }
    }

    public static uint GetChallengeCount(PlayerInstance player)
        => player.Attributes.GetValue(AttrIds.WorldBoss.Gid, AttrIds.WorldBoss.CurrentDataSid);

    /// <summary>
    /// Spends one challenge and opens a fresh attempt block. Returns the block index, or -1 when the
    /// act / stage is unknown or the player has no challenge left (the client refuses to enter either
    /// way, but a stale button can still send the request).
    /// </summary>
    public static int BeginAttempt(PlayerInstance player, WorldBossActExcel act, uint stageId,
        NtfSyncPlayer sync, IReadOnlyList<uint>? lineup)
    {
        if (!act.LevelList.Contains(stageId))
        {
            Log.Warn($"WorldBoss enter: stage {stageId} is not in act {act.ActivityId}/{act.ActId}.");
            return -1;
        }

        var remaining = GetChallengeCount(player);
        if (remaining == 0)
            return -1;

        var index = FindFreeAttempt(player, act);
        if (index < 0)
            return -1;

        var baseSid = AttrIds.WorldBoss.AttemptSid(act.ActId, (uint)index);
        Set(player, baseSid + AttrIds.WorldBoss.ChallengeCountOffset, 0, sync);
        Set(player, baseSid + AttrIds.WorldBoss.DamageOffset, 0, sync);
        Set(player, baseSid + AttrIds.WorldBoss.ActivityOffset, act.ActivityId, sync);
        Set(player, baseSid + AttrIds.WorldBoss.ActivityIdOffset, act.ActId, sync);
        Set(player, baseSid + AttrIds.WorldBoss.RecordedOffset, 0, sync);

        for (var i = 0; i < 3; i++)
        {
            var character = lineup != null && i < lineup.Count ? lineup[i] : 0;
            Set(player, baseSid + AttrIds.WorldBoss.CharacterOffset + (uint)i, character, sync);
        }

        Set(player, AttrIds.WorldBoss.CurrentDataSid, remaining - 1, sync);

        // Joining the board is what arms the mail reward slot; 0 means "never joined".
        if (player.Attributes.GetValue(AttrIds.WorldBoss.RewardGid, act.ReferRank2) == 0)
            Set(player, AttrIds.WorldBoss.RewardGid, act.ReferRank2, 1, sync);

        DatabaseHelper.SaveDatabaseType(player.Data);
        Log.Info($"WorldBoss enter act={act.ActivityId}/{act.ActId} stage={stageId} block={index} " +
                 $"left={remaining - 1} for uid={player.Uid}.");
        return index;
    }

    /// <summary>
    /// Settlement outcome: records the damage, marks the block, advances the three quest families.
    /// Returns the granted-and-pending quest list so the reply can stay an empty object (the client's
    /// settlement callback reads no field).
    /// </summary>
    public static void ApplyResult(PlayerInstance player, WorldBossActExcel act, uint stageId,
        uint damage, NtfSyncPlayer sync)
    {
        var index = FindPendingAttempt(player, act);
        if (index >= 0)
        {
            var baseSid = AttrIds.WorldBoss.AttemptSid(act.ActId, (uint)index);
            Set(player, baseSid + AttrIds.WorldBoss.DamageOffset, damage, sync);
            Set(player, baseSid + AttrIds.WorldBoss.RecordedOffset, 1, sync);
        }

        var attempts = CountRecorded(player, act);
        var maxHp = BossMaxHp(stageId);

        foreach (var taskId in act.AllTaskIds)
        {
            if (!GameData.ActivityQuestData.TryGetValue(taskId, out var quest))
                continue;

            var reached = quest.Type switch
            {
                PercentTaskType => maxHp == 0 ? 0u : Math.Min(100u, (uint)((ulong)damage * 100 / maxHp)),
                DamageTaskType => damage,
                ParticipateTaskType => attempts,
                _ => 0u
            };

            if (reached == 0 || quest.CheckValue == 0)
                continue;

            if (AchievementClaimService.IsFinished(player, QuestGroupId, taskId, quest.CheckValue))
                continue;

            if (reached < quest.CheckValue)
                continue;

            AchievementClaimService.Complete(player, QuestGroupId, taskId, quest.CheckValue, sync);
        }

        DatabaseHelper.SaveDatabaseType(player.Data);
        Log.Info($"WorldBoss settlement act={act.ActivityId}/{act.ActId} stage={stageId} damage={damage} " +
                 $"attempts={attempts} for uid={player.Uid}.");
    }

    public static bool MarkRecorded(PlayerInstance player, WorldBossActExcel act)
    {
        var index = FindPendingAttempt(player, act);
        if (index < 0)
            return true;

        var sync = new NtfSyncPlayer();
        var baseSid = AttrIds.WorldBoss.AttemptSid(act.ActId, (uint)index);
        Set(player, baseSid + AttrIds.WorldBoss.RecordedOffset, 1, sync);
        DatabaseHelper.SaveDatabaseType(player.Data);
        return true;
    }

    private static void Set(PlayerInstance player, uint sid, uint value, NtfSyncPlayer? sync)
    {
        // Mirrors HandlerReqLogin.SeedPinnedAttr: a zero on a slot that was never written is not a
        // value, it is the absence of one, and persisting it as a null row would make the attempt
        // blocks unreadable in the database dump.
        var attr = player.Attributes.Get(AttrIds.WorldBoss.Gid, sid);
        if (attr == null)
        {
            if (value == 0)
                return;

            attr = player.Attributes.GetOrCreate(AttrIds.WorldBoss.Gid, sid);
        }

        if (attr.Val == value)
            return;

        attr.Val = value;
        if (sync != null)
            player.Attributes.SyncTo(sync, attr);
    }

    private static void Set(PlayerInstance player, uint gid, uint sid, uint value, NtfSyncPlayer? sync)
    {
        var attr = player.Attributes.Get(gid, sid);
        if (attr == null)
        {
            if (value == 0)
                return;

            attr = player.Attributes.GetOrCreate(gid, sid);
        }

        if (attr.Val == value)
            return;

        attr.Val = value;
        if (sync != null)
            player.Attributes.SyncTo(sync, attr);
    }

    private static int FindFreeAttempt(PlayerInstance player, WorldBossActExcel act)
    {
        for (uint i = 0; i < MaxAttempts; i++)
        {
            var baseSid = AttrIds.WorldBoss.AttemptSid(act.ActId, i);
            var activity = player.Attributes.GetValue(AttrIds.WorldBoss.Gid, baseSid + AttrIds.WorldBoss.ActivityOffset);
            if (activity == 0)
                return (int)i;
        }

        return -1;
    }

    /// <summary>The newest block of this act that was opened but not recorded yet.</summary>
    private static int FindPendingAttempt(PlayerInstance player, WorldBossActExcel act)
    {
        for (var i = (int)MaxAttempts - 1; i >= 0; i--)
        {
            var baseSid = AttrIds.WorldBoss.AttemptSid(act.ActId, (uint)i);
            if (player.Attributes.GetValue(AttrIds.WorldBoss.Gid, baseSid + AttrIds.WorldBoss.ActivityOffset) != act.ActivityId)
                continue;

            if (player.Attributes.GetValue(AttrIds.WorldBoss.Gid, baseSid + AttrIds.WorldBoss.ActivityIdOffset) != act.ActId)
                continue;

            if (player.Attributes.GetValue(AttrIds.WorldBoss.Gid, baseSid + AttrIds.WorldBoss.RecordedOffset) == 0)
                return i;
        }

        return -1;
    }

    private static uint CountRecorded(PlayerInstance player, WorldBossActExcel act)
    {
        uint count = 0;
        for (uint i = 0; i < MaxAttempts; i++)
        {
            var baseSid = AttrIds.WorldBoss.AttemptSid(act.ActId, i);
            if (player.Attributes.GetValue(AttrIds.WorldBoss.Gid, baseSid + AttrIds.WorldBoss.ActivityOffset) != act.ActivityId)
                continue;

            if (player.Attributes.GetValue(AttrIds.WorldBoss.Gid, baseSid + AttrIds.WorldBoss.RecordedOffset) != 0)
                count++;
        }

        return count;
    }

    private static bool HasRecordedAttempt(PlayerInstance player, WorldBossActExcel act) => CountRecorded(player, act) > 0;
}
