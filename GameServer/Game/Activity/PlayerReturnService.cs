using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Game.Activity;

/// <summary>
/// 回归活动 (player return) state. The client module is Activity/PlayerReturn.lua; the activity row
/// is the activities.txt row with Class "player_return" (id 560 for the pinned DLC35 era, window
/// [202601290400]..[202603120359]).
///
/// The client owns the layout of the three tabs and reads everything out of two attribute groups:
///
///   * group 0 (AttrIds.Activity.PlayerReturnGroupId) — the act id at sid 15 and the trigger stamp
///     at sid 16 (PlayerReturn.GetActId -> me:GetAttribute(0, 15)).
///   * group 50 (AttrIds.Activity.GroupId) — the per-act slots, reached through
///     Activity.GetDiyTaskId(nActId, nPos) = nActId * 10 + nPos + 2 (Activity.lua), so
///     StartTime -> actId*10+3, Sign -> actId*10+4, FirstGet -> actId*10+6, ProtectAdd -> actId*10+7.
///
/// GetTimeConf (PlayerReturn.lua:78) builds endTime = startTime + (ActiveDay + protectAdd) * 86400
/// out of the StartTime/ProtectAdd slots plus the client's own return.txt row, and IsOpen is
/// IsInTime over that pair — which is what umg_main.ShowBtnReturn keys the main-UI button on.
///
/// Sign is packed like the client reads it (PlayerReturn.GetSignDay): bits 0..7 = days signed so
/// far, bits 8..15 = days already claimed. The mission tab works on the achievement quest group
/// (AttrIds.Achievement.QuestGroupId = 7) with bit 0 = claimed and bits 1..31 = progress
/// (Achievement.lua:743/:783), the same packing the alien defense and season boss services use.
/// </summary>
public static class PlayerReturnService
{
    private const string ReturnClass = "player_return";

    private const uint ProgressShift = 1;
    private const uint ProgressMask = 0x7FFFFFFFu;
    private const uint SignedDayMask = 0xFFu;
    private const int ClaimedDayShift = 8;
    private const uint ClaimedDayMask = 0xFFu;

    private const long SecondsPerDay = 86400;

    /// <summary>
    /// Days of window that must stay ahead of "now" after the trigger has been back-dated, so the
    /// entry button and the countdown never look expired (see <see cref="BackDateDays"/>).
    /// </summary>
    private const uint MinWindowDays = 7;

    private static readonly Logger Log = new(nameof(PlayerReturnService));

    // Activity.GetDiyTaskId(nActId, nPos) = nActId * 10 + nPos + 2 with the nPos values of
    // PlayerReturn.lua:12-16.
    private static uint StartTimeSid(uint actId) => (actId * 10) + 3;
    private static uint SignSid(uint actId) => (actId * 10) + 4;
    private static uint FirstGetSid(uint actId) => (actId * 10) + 6;
    private static uint ProtectAddSid(uint actId) => (actId * 10) + 7;

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public static uint GetActId(PlayerInstance player) =>
        player.Attributes.GetValue(AttrIds.Activity.PlayerReturnGroupId, Attrs.ActIdSid);

    /// <summary>
    /// The return activity of the activated era: the earliest activities.txt row of class
    /// "player_return" that starts inside the era (its start time at or after the activation floor
    /// and its window not historical). Rows of later eras are skipped on purpose, so a pinned DLC
    /// only ever opens its own return activity.
    /// </summary>
    public static uint SelectActId()
    {
        var floor = DlcRuntimeOverrides.ActivationFloor();
        var best = 0u;
        var bestStart = long.MaxValue;

        foreach (var conf in GameData.ActivityData.Values)
        {
            if (!string.Equals(conf.Class, ReturnClass, StringComparison.OrdinalIgnoreCase))
                continue;

            if (DlcRuntimeOverrides.IsHistoricalWindow(conf.EndTime))
                continue;

            var start = DlcRuntimeOverrides.ToUnixSeconds(DlcRuntimeOverrides.ParseConfigTime(conf.StartTime));
            if (floor != 0 && start < floor)
                continue;

            if (start == 0 || start >= bestStart)
                continue;

            bestStart = start;
            best = conf.Id;
        }

        return best;
    }

    /// <summary>
    /// Login roll: seeds the era's return activity for accounts that never saw it, advances the
    /// signed-day counter once per elapsed day and marks the open achievement stages as finished so
    /// the mission tab has something to claim.
    /// </summary>
    public static void RollDailyState(PlayerInstance player) => Ensure(player, null);

    /// <summary>Same as <see cref="RollDailyState"/> but syncing the touched attributes.</summary>
    public static void Ensure(PlayerInstance player, NtfSyncPlayer? sync)
    {
        var actId = SelectActId();
        if (actId == 0)
        {
            Log.Warn($"No player_return activity inside the activated era for uid={player.Uid}.");
            return;
        }

        if (!GameData.PlayerReturnData.TryGetValue(actId, out var row))
        {
            Log.Warn($"Return activity {actId} has no return.json row for uid={player.Uid}.");
            return;
        }

        var stored = GetActId(player);
        var now = Now();

        var stageCount = GameData.PlayerReturnRewardData.TryGetValue(actId, out var rewardConf)
            ? (uint)rewardConf.StageCount
            : 0u;

        // Retail hands the stages and the sign-in days out day by day: the client derives the open
        // rank from ceil((now - startTime) / 86400) (PlayerReturn.GetAchievementStage, consumed as
        // "rank i is unlocked while i - 1 <= stage"), so a trigger stamped with today would expose a
        // single rank and a single sign-in day. Compress that timeline instead - back-date the start
        // so every rank and every sign-in day is claimable in one session - while keeping at least
        // MinWindowDays of the window ahead of now so the entry never reads as expired.
        var backDate = BackDateDays(row.ActiveDay, stageCount);
        var wantedStart = now - (backDate * SecondsPerDay);
        var storedStart = player.Attributes.GetValue(AttrIds.Activity.GroupId, StartTimeSid(actId));

        if (stored != actId || storedStart == 0 || storedStart > wantedStart)
        {
            var fresh = stored != actId;
            SetAttr(player, AttrIds.Activity.PlayerReturnGroupId, Attrs.ActIdSid, actId, sync);
            SetAttr(player, AttrIds.Activity.GroupId, StartTimeSid(actId), (uint)wantedStart, sync);

            if (fresh)
            {
                SetAttr(player, AttrIds.Activity.GroupId, SignSid(actId), 0, sync);
                SetAttr(player, AttrIds.Activity.GroupId, FirstGetSid(actId), 0, sync);
                SetAttr(player, AttrIds.Activity.GroupId, ProtectAddSid(actId), row.OfflineProtectionDay, sync);
                SetAttr(player, AttrIds.Activity.PlayerReturnGroupId, Attrs.LastActiveTimeSid, (uint)now, sync);

                Log.Info($"Return activity {actId} triggered for uid={player.Uid} " +
                         $"(activeDay={row.ActiveDay}, protectAdd={row.OfflineProtectionDay}, backDate={backDate}d).");
            }
            else
            {
                Log.Info($"Return activity {actId}: start re-seeded to {wantedStart} for uid={player.Uid} " +
                         $"(was {storedStart}, backDate={backDate}d).");
            }
        }

        AdvanceSignedDays(player, actId, sync);
        MarkOpenStagesFinished(player, actId, sync);
    }

    /// <summary>
    /// Days to back-date the trigger so every achievement rank and every sign-in day is reachable at
    /// once. Bounded by the act's ActiveDay: acts whose window is not wider than
    /// <see cref="MinWindowDays"/> keep the retail day-by-day pacing instead of being back-dated
    /// into an already expired window.
    /// </summary>
    private static uint BackDateDays(uint activeDay, uint stageCount)
    {
        if (stageCount <= 1 || activeDay <= MinWindowDays)
            return 0;

        return Math.Min(stageCount - 1, activeDay - MinWindowDays);
    }

    /// <summary>Claimable window of the stored return activity, mirroring PlayerReturn.GetTimeConf.</summary>
    public static (long Start, long End) GetTimeConf(PlayerInstance player, uint actId)
    {
        var start = player.Attributes.GetValue(AttrIds.Activity.GroupId, StartTimeSid(actId));
        var protectAdd = player.Attributes.GetValue(AttrIds.Activity.GroupId, ProtectAddSid(actId));
        var activeDay = GameData.PlayerReturnData.TryGetValue(actId, out var row) ? row.ActiveDay : 0;

        return (start, start + ((activeDay + protectAdd) * SecondsPerDay));
    }

    /// <summary>
    /// One-shot trigger reward (PlayerReturn_GetReward nType 1): pays the act's FirstReward rows and
    /// flips the FirstGet slot, which is what umg_actvityreturn's first tab keys the claim button on.
    /// </summary>
    public static async Task<JsonArray> ClaimFirstAsync(
        PlayerInstance player,
        uint actId,
        NtfSyncPlayer sync)
    {
        var granted = new JsonArray();

        if (!GameData.PlayerReturnRewardData.TryGetValue(actId, out var rewardConf))
        {
            Log.Warn($"Return first reward: act {actId} has no return_reward row for uid={player.Uid}.");
            return granted;
        }

        if (player.Attributes.GetValue(AttrIds.Activity.GroupId, FirstGetSid(actId)) == 1)
        {
            Log.Warn($"Return first reward: act {actId} already claimed for uid={player.Uid}.");
            return granted;
        }

        var rows = DlcRuntimeOverrides.ReadRewardRows(rewardConf.FirstReward);
        var rewards = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);
        foreach (var row in rewards)
            granted.Add(row?.DeepClone());

        SetAttr(player, AttrIds.Activity.GroupId, FirstGetSid(actId), 1, sync);
        Save(player);

        Log.Info($"Return first reward: act {actId}, {rewards.Count} row(s) granted for uid={player.Uid}.");
        return granted;
    }

    /// <summary>
    /// Sign-in reward (nType 2): pays the row of claimedDays + 1 out of return_sign.json and bumps
    /// the claimed-day nibble. Returns the day ids that were signed, which is what the sign tab
    /// animates (uw_activityreturn01.Show reads tbParam.tbSignDay).
    /// </summary>
    public static async Task<(JsonArray Granted, List<uint> Days)> ClaimSignAsync(
        PlayerInstance player,
        uint actId,
        NtfSyncPlayer sync)
    {
        var granted = new JsonArray();
        var days = new List<uint>();

        var (signed, claimed) = GetSignDays(player, actId);
        if (claimed >= signed)
        {
            Log.Info($"Return sign: act {actId} nothing due ({signed} signed / {claimed} claimed) for uid={player.Uid}.");
            return (granted, days);
        }

        var dayId = claimed + 1;
        if (!GameData.PlayerReturnSignData.TryGetValue((actId, dayId), out var row))
        {
            Log.Warn($"Return sign: act {actId} has no return_sign row for day {dayId} (uid={player.Uid}).");
            return (granted, days);
        }

        var rows = DlcRuntimeOverrides.ReadRewardRows(row.SignReward);
        var rewards = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);
        foreach (var reward in rewards)
            granted.Add(reward?.DeepClone());

        SetAttr(player, AttrIds.Activity.GroupId, SignSid(actId), PackSignDays(signed, dayId), sync);
        Save(player);
        days.Add(dayId);

        Log.Info($"Return sign: act {actId} day {dayId} claimed, {rewards.Count} row(s) granted for uid={player.Uid}.");
        return (granted, days);
    }

    /// <summary>
    /// Mission/achievement reward (nType 3). Only quest rows listed in the act's return_reward
    /// AchieveId stages are payable, and only from a stage the elapsed days already unlocked
    /// (PlayerReturn.GetAchievementStage + 1 is the client's own gate). <paramref name="quick"/>
    /// claims every claimable row of those stages, mirroring the tab's quick-claim button.
    /// </summary>
    public static async Task<JsonArray> ClaimMissionsAsync(
        PlayerInstance player,
        uint actId,
        bool quick,
        uint missionId,
        NtfSyncPlayer sync)
    {
        var granted = new JsonArray();

        if (!GameData.PlayerReturnRewardData.TryGetValue(actId, out var rewardConf))
        {
            Log.Warn($"Return mission reward: act {actId} has no return_reward row for uid={player.Uid}.");
            return granted;
        }

        var openStage = Math.Min(CurrentStage(player, actId), (uint)rewardConf.StageCount);
        var candidates = new List<uint>();

        if (quick)
        {
            for (var rank = 1u; rank <= openStage; rank++)
                candidates.AddRange(rewardConf.StageQuestIds((int)rank));
        }
        else
        {
            var rank = FindStage(rewardConf, missionId);
            if (rank == 0)
            {
                Log.Warn($"Return mission reward: quest {missionId} is not a row of act {actId} (uid={player.Uid}).");
                return granted;
            }

            if (rank > openStage)
            {
                Log.Warn($"Return mission reward: quest {missionId} sits in stage {rank} but only " +
                         $"{openStage} stage(s) are open for uid={player.Uid}.");
                return granted;
            }

            candidates.Add(missionId);
        }

        var claimedAny = false;
        foreach (var questId in candidates.Distinct())
        {
            if (!GameData.ActivityQuestData.TryGetValue(questId, out var quest))
            {
                Log.Warn($"Return mission reward: unknown activity quest {questId} for uid={player.Uid}.");
                continue;
            }

            var attr = player.Attributes.GetOrCreate(AttrIds.Achievement.QuestGroupId, questId);
            if (IsReceived(attr.Val))
                continue;

            var progress = GetProgress(attr.Val);
            if (progress < quest.CheckValue)
            {
                Log.Warn($"Return mission reward: quest {questId} not finished ({progress}/{quest.CheckValue}) " +
                         $"for uid={player.Uid}.");
                continue;
            }

            var rows = DlcRuntimeOverrides.ReadRewardRows(quest.Rewards);
            var rewards = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);
            foreach (var row in rewards)
                granted.Add(row?.DeepClone());

            attr.Val = Pack(progress, true);
            player.Attributes.SyncTo(sync, attr);
            claimedAny = true;

            Log.Info($"Return mission reward: quest {questId} claimed, {rewards.Count} row(s) granted for uid={player.Uid}.");
        }

        if (claimedAny)
            Save(player);

        return granted;
    }

    /// <summary>Signed days and claimed days out of the packed Sign slot.</summary>
    public static (uint Signed, uint Claimed) GetSignDays(PlayerInstance player, uint actId)
    {
        var raw = player.Attributes.GetValue(AttrIds.Activity.GroupId, SignSid(actId));
        return (raw & SignedDayMask, (raw >> ClaimedDayShift) & ClaimedDayMask);
    }

    /// <summary>Elapsed days since the trigger + 1, the same stage number the client computes.</summary>
    public static uint CurrentStage(PlayerInstance player, uint actId)
    {
        var start = player.Attributes.GetValue(AttrIds.Activity.GroupId, StartTimeSid(actId));
        if (start == 0)
            return 1;

        var elapsed = (Now() - start) / SecondsPerDay;
        return (uint)Math.Max(0, elapsed) + 1;
    }

    /// <summary>How many sign-in rows the act has; 7 for act 560.</summary>
    public static uint MaxSignDay(uint actId)
    {
        var max = 0u;
        foreach (var key in GameData.PlayerReturnSignData.Keys)
        {
            if (key.ActId == actId && key.DayId > max)
                max = key.DayId;
        }

        return max;
    }

    /// <summary>
    /// Keeps bits 0..7 of the Sign slot in step with the calendar: day N of the window counts as
    /// signed on login, clamped to the number of rows the act has so the sign tab stops re-asking
    /// once the last day is claimed.
    /// </summary>
    private static void AdvanceSignedDays(PlayerInstance player, uint actId, NtfSyncPlayer? sync)
    {
        var maxDay = MaxSignDay(actId);
        if (maxDay == 0)
            return;

        var (signed, claimed) = GetSignDays(player, actId);
        var due = Math.Min(CurrentStage(player, actId), maxDay);
        if (due <= signed)
            return;

        SetAttr(player, AttrIds.Activity.GroupId, SignSid(actId), PackSignDays(due, claimed), sync);
    }

    /// <summary>
    /// Marks every quest of the stages the elapsed days unlocked as finished. The counters behind
    /// those rows (login days, spending, clears) are not simulated by this server, and the client
    /// only offers stages up to GetAchievementStage + 1 anyway, so this keeps the tab claimable
    /// without handing out later stages early. Because Ensure back-dates the trigger, "elapsed days"
    /// already covers every stage for acts whose window is wider than MinWindowDays.
    /// </summary>
    private static void MarkOpenStagesFinished(PlayerInstance player, uint actId, NtfSyncPlayer? sync)
    {
        if (!GameData.PlayerReturnRewardData.TryGetValue(actId, out var rewardConf))
            return;

        var openStage = Math.Min(CurrentStage(player, actId), (uint)rewardConf.StageCount);
        var changed = false;

        for (var rank = 1u; rank <= openStage; rank++)
        {
            foreach (var questId in rewardConf.StageQuestIds((int)rank))
            {
                if (!GameData.ActivityQuestData.TryGetValue(questId, out var quest))
                    continue;

                var attr = player.Attributes.GetOrCreate(AttrIds.Achievement.QuestGroupId, questId);
                if (GetProgress(attr.Val) >= quest.CheckValue)
                    continue;

                attr.Val = Pack(quest.CheckValue, IsReceived(attr.Val));
                if (sync != null)
                    player.Attributes.SyncTo(sync, attr);
                changed = true;
            }
        }

        if (changed)
            Save(player);
    }

    private static uint FindStage(PlayerReturnRewardExcel rewardConf, uint questId)
    {
        for (var rank = 1; rank <= rewardConf.StageCount; rank++)
        {
            if (rewardConf.StageQuestIds(rank).Contains(questId))
                return (uint)rank;
        }

        return 0;
    }

    private static uint PackSignDays(uint signed, uint claimed) =>
        ((claimed & ClaimedDayMask) << ClaimedDayShift) | (signed & SignedDayMask);

    private static uint GetProgress(uint raw) => (raw >> (int)ProgressShift) & ProgressMask;

    private static bool IsReceived(uint raw) => (raw & 1u) != 0;

    private static uint Pack(uint progress, bool received) =>
        (Math.Min(progress, ProgressMask) << (int)ProgressShift) | (received ? 1u : 0u);

    private static void SetAttr(PlayerInstance player, uint gid, uint sid, uint value, NtfSyncPlayer? sync)
    {
        var attr = player.Attributes.GetOrCreate(gid, sid);
        if (attr.Val == value)
            return;

        attr.Val = value;
        if (sync != null)
            player.Attributes.SyncTo(sync, attr);
    }

    private static void Save(PlayerInstance player)
    {
        DatabaseHelper.SaveDatabaseType(player.Data);
        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
        DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
    }

    /// <summary>Sids of the player-level group (AttrIds.Activity.PlayerReturnGroupId).</summary>
    private static class Attrs
    {
        public const uint ActIdSid = AttrIds.Activity.PlayerReturnActSid;
        public const uint LastActiveTimeSid = AttrIds.Activity.PlayerReturnLastActiveTimeSid;
    }
}
