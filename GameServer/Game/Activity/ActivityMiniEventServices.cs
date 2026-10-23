using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Achievement;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Game.Activity;

/// <summary>
/// New-player "7 天乐" board (client Activity/SevenDay.lua, ModeId 1043, activity row Id=5).
///
/// The board is time-window free: activities.json Id=5 has an empty StartTime/EndTime, ParseTime('')
/// is -1 and IsInTime(-1,-1) is always true (Misc/Lib.lua:451-453), so the only gate is
/// FunctionType.SevenDay=24 plus a cleared story stage. What the client does read is the unlock
/// counter: Activity.GetDiyData(5, 1) = (50, 53) decides which day columns are lit
/// (SevenDay.lua:94, 128-129, 164-165), and Activity.GetDiyData(5, 2) = (50, 54) is the stage award
/// bitmap (SevenDay.lua:113, 128, 139 GetBits(nowIndexAward, i, i)).
///
/// Both slots are server-authoritative: there is no SetAttribute(50, *) anywhere in the script tree.
/// The board therefore has to be seeded on login, otherwise every day stays greyed out and the
/// "一键领取" button has nothing to claim.
/// </summary>
public static class SevenDayService
{
    /// <summary>The only row in activity/seven_day/seven_day_info.json.</summary>
    public const uint ActId = 5;

    public const uint GroupId = AttrIds.Activity.GroupId; // Activity.nGroupId = 50

    /// <summary>Activity.GetDiyData(actId, 1) = actId*10 + 1 + 2.</summary>
    public const uint UnlockDaySid = ActId * 10 + 3;      // 53

    /// <summary>Activity.GetDiyData(actId, 2) - stage award bitmap, bit (awardId-1).</summary>
    public const uint StageAwardSid = ActId * 10 + 4;     // 54

    /// <summary>
    /// The point bar reads a real currency, not a dedicated counter: SevenDay.lua:134
    /// "nowPoint = Cash.GetMoneyCount(tbCash[1])" with tbCash = the activity row's CashID, which for
    /// activities.json Id=5 is [5] = Cash.MoneyType_TOKEN (雪之形, Cash.lua:15).
    /// </summary>
    public const uint PointMoneyType = 5;

    public static uint MaxDay()
    {
        var max = 0u;
        foreach (var pair in GameData.SevenDayAchievementData)
        {
            if (pair.Key.ActId == ActId && pair.Key.DayId > max)
                max = pair.Key.DayId;
        }

        return max;
    }

    /// <summary>
    /// Unlocks every day column of the board. Called from ApplyPinnedEra so the very first attribute
    /// sync already carries it - SevenDay.lua:231 reads it while building the tab list, and a 0 there
    /// leaves day 2..7 permanently locked.
    /// </summary>
    public static void Seed(PlayerInstance player, NtfSyncPlayer? sync)
    {
        if (!GameData.SevenDayInfoData.ContainsKey(ActId))
            return;

        var max = MaxDay();
        if (max == 0)
            return;

        if (player.Attributes.GetValue(GroupId, UnlockDaySid) < max)
            SetAttr(player, UnlockDaySid, max, sync);

        EnsureQuestsFinished(player, sync);
    }

    /// <summary>
    /// Marks every board task finished so the claim buttons work.
    ///
    /// The 49 rows of achievement/activity_quest.json 101..707 are daily tasks whose progress the
    /// client fetches from the server (Achievement.lua:743 GetProgres reads the attribute bits), and
    /// their Reward types (350/30/31/43/10/8/4) are all tracked by game systems this server does not
    /// run. Without this the board renders, but IsFinished is false for every row
    /// (AchievementClaimService.IsFinished treats CheckValue 0 as "needs progress 1"), so every claim
    /// silently answers nothing. Marking them finished - not claimed - keeps the reward flow explicit:
    /// the player still has to press 领取, and AchievementClaimService still pays only once.
    ///
    /// Attribute layout: group 7, bit 0 = claimed, bits 1..31 = progress.
    /// </summary>
    public static void EnsureQuestsFinished(PlayerInstance player, NtfSyncPlayer? sync)
    {
        const uint group = AchievementClaimService.QuestGroupId;

        foreach (var questId in QuestIds())
        {
            var attr = player.Attributes.GetOrCreate(group, questId);
            if ((attr.Val & 1u) != 0 || (attr.Val >> 1) >= 1)
                continue;

            attr.Val |= 1u << 1;
            if (sync != null)
                player.Attributes.SyncTo(sync, attr);
        }
    }

    public static bool IsStageAwarded(PlayerInstance player, uint awardId)
    {
        if (awardId == 0 || awardId > 31)
            return false;

        var bits = player.Attributes.GetValue(GroupId, StageAwardSid);
        return (bits & (1u << (int)(awardId - 1))) != 0;
    }

    public static void MarkStageAwarded(PlayerInstance player, uint awardId, NtfSyncPlayer? sync)
    {
        if (awardId == 0 || awardId > 31)
            return;

        var bits = player.Attributes.GetValue(GroupId, StageAwardSid);
        SetAttr(player, StageAwardSid, bits | (1u << (int)(awardId - 1)), sync);
    }

    /// <summary>Current point-bar value (the 雪之形 balance).</summary>
    public static uint Points(PlayerInstance player) => CurrencyService.GetBalance(player, PointMoneyType);

    /// <summary>Every quest id of the board, in day order - used by the "all claimable" scan.</summary>
    public static IEnumerable<uint> QuestIds()
    {
        var ids = new List<uint>();
        for (var day = 1u; day <= MaxDay(); day++)
        {
            if (GameData.SevenDayAchievementData.TryGetValue((ActId, day), out var row))
                ids.AddRange(row.AchieveIds);
        }

        return ids;
    }

    private static void SetAttr(PlayerInstance player, uint sid, uint value, NtfSyncPlayer? sync)
    {
        var attr = player.Attributes.GetOrCreate(GroupId, sid);
        if (attr.Val == value)
            return;

        attr.Val = value;
        if (sync != null)
            player.Attributes.SyncTo(sync, attr);
    }
}

/// <summary>
/// Free vigour meals (client Activity/VigourSupply.lua).
///
/// State lives in AttrIds.Activity.VigourSupplyGroupId = 105 and the client never writes it:
///   (105, 0) = next daily reset timestamp  -> template08:149-155 re-sends CheckRefresh every second
///              while it is 0, and a 0 value also leaves the red dot on forever
///   (105, 1) = claim bitmap, bit (nId-1)
///
/// The two windows are inside the day (11:00-23:59 and 17:00-23:59 in vigour_supply.json) and are
/// compared locally by the client against ParseBriefTime(TimeStart/TimeEnd), so a
/// GlobalTimeWindows relaxation does nothing here - the server has to gate on the same clock.
/// </summary>
public static class VigourSupplyService
{
    public const uint GroupId = AttrIds.Activity.VigourSupplyGroupId; // 105
    public const uint NextResetSid = 0;
    public const uint ClaimedMaskSid = 1;

    /// <summary>Daily reset hour, matching Activityface.DailyTime = 4 used by the activity popups.</summary>
    private const int ResetHour = 4;

    public static long NextResetUnix(DateTime now)
    {
        var today = now.Date.AddHours(ResetHour);
        var next = now < today ? today : today.AddDays(1);
        return new DateTimeOffset(next).ToUnixTimeSeconds();
    }

    /// <summary>Seeds the next daily reset stamp so the panel stops re-sending and the red dot clears.</summary>
    public static void Seed(PlayerInstance player, NtfSyncPlayer? sync)
    {
        if (GameData.VigourSupplyData.Count == 0)
            return;

        var next = (uint)Math.Min(NextResetUnix(DateTime.Now), uint.MaxValue);
        var current = player.Attributes.GetValue(GroupId, NextResetSid);
        if (current == next)
            return;

        SetAttr(player, NextResetSid, next, sync);
    }

    public static bool IsClaimed(PlayerInstance player, uint id)
    {
        if (id == 0 || id > 31)
            return false;

        var bits = player.Attributes.GetValue(GroupId, ClaimedMaskSid);
        return (bits & (1u << (int)(id - 1))) != 0;
    }

    public static void MarkClaimed(PlayerInstance player, uint id, NtfSyncPlayer? sync)
    {
        if (id == 0 || id > 31)
            return;

        var bits = player.Attributes.GetValue(GroupId, ClaimedMaskSid);
        SetAttr(player, ClaimedMaskSid, bits | (1u << (int)(id - 1)), sync);
    }

    /// <summary>True while the local wall clock is inside [TimeStart, TimeEnd]; an inverted window wraps midnight.</summary>
    public static bool InWindow(VigourSupplyExcel row, DateTime now)
    {
        var current = (uint)(now.Hour * 100 + now.Minute);
        var start = row.StartHhmm;
        var end = row.EndHhmm;
        if (start == 0 && end == 0)
            return true;

        return start <= end
            ? current >= start && current <= end
            : current >= start || current <= end;
    }

    private static void SetAttr(PlayerInstance player, uint sid, uint value, NtfSyncPlayer? sync)
    {
        var attr = player.Attributes.GetOrCreate(GroupId, sid);
        if (attr.Val == value)
            return;

        attr.Val = value;
        if (sync != null)
            player.Attributes.SyncTo(sync, attr);
    }
}

/// <summary>
/// Cumulative top-up rewards (client Activity/PlayerCharge.lua, MallCharge page).
///
/// There is no GID on the client side - PlayerCharge.lua:8 holds a copy/paste bug that assigns
/// PlayerReturn.GID to PlayerCharge.GID, so every read is me:GetAttribute(nil, 18/19/20) and lands in
/// attribute group 0:
///   (0, 18) = cumulative charged amount
///   (0, 19) = claim bitmap (bit index-1) plus the "show amounts" flag at bit 30
///   (0, 20) = last selected show id (written by SetShowIDState, client to server only)
///
/// This server never takes real money, so the charged amount stays whatever the player already has
/// (usually 0) and the award list is empty - the point of these two handlers is to answer with the
/// right shape instead of the router's ui.TxtNotOpen, which is what the MallCharge page shows today.
/// </summary>
public static class PlayerChargeService
{
    public const uint GroupId = 0;          // client reads a nil gid, which serialises as 0
    public const uint ChargedSid = 18;
    public const uint ClaimedMaskSid = 19;
    public const uint ShowStateSid = 20;

    /// <summary>"Switch" toggle lives in bit 31 of the bitmap, i.e. zero-based bit 30 (PlayerCharge.lua:35-38).</summary>
    public const uint ShowAmountBit = 30;

    public static uint Charged(PlayerInstance player) => player.Attributes.GetValue(GroupId, ChargedSid);

    public static bool IsClaimed(PlayerInstance player, uint index)
    {
        if (index == 0 || index > 30)
            return false;

        var bits = player.Attributes.GetValue(GroupId, ClaimedMaskSid);
        return (bits & (1u << (int)(index - 1))) != 0;
    }

    public static void MarkClaimed(PlayerInstance player, uint index, NtfSyncPlayer? sync)
    {
        if (index == 0 || index > 30)
            return;

        var bits = player.Attributes.GetValue(GroupId, ClaimedMaskSid);
        SetAttr(player, ClaimedMaskSid, bits | (1u << (int)(index - 1)), sync);
    }

    public static bool ShowAmounts(PlayerInstance player)
        => (player.Attributes.GetValue(GroupId, ClaimedMaskSid) & (1u << (int)ShowAmountBit)) != 0;

    public static void SetShowAmounts(PlayerInstance player, bool on, NtfSyncPlayer? sync)
    {
        var bits = player.Attributes.GetValue(GroupId, ClaimedMaskSid);
        var mask = 1u << (int)ShowAmountBit;
        bits = on ? bits | mask : bits & ~mask;
        SetAttr(player, ClaimedMaskSid, bits, sync);
    }

    private static void SetAttr(PlayerInstance player, uint sid, uint value, NtfSyncPlayer? sync)
    {
        var attr = player.Attributes.GetOrCreate(GroupId, sid);
        if (attr.Val == value)
            return;

        attr.Val = value;
        if (sync != null)
            player.Attributes.SyncTo(sync, attr);
    }
}
