using MikuSB.Data;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Game.Activity;

/// <summary>
/// Daily sign-in ("signin_monthly", activity id 1) state, kept exactly where the client reads it:
///
///   Activity/Activity.lua:101 GetBaseTaskId(nId, nPos) = nId * 10 + nPos       (nPos 0..9)
///   Activity/Activity.lua:159 GetDiyTaskId(nId, nPos)  = nId * 10 + nPos + 2   (nPos 1..7)
///   Activity/Sign.lua:12 Sign.SignTag_TaskId = 1 -> (50, nId*10 + 3)  signed-day counter
///   Activity/Sign.lua:14 Sign.SignDay_TaskId = 2 -> (50, nId*10 + 4)  today's status
///
/// DIY slot 3 ((50, nId*10 + 5)) is unused by the monthly sign-in and stores the yyyymmdd stamp of
/// the last grant, which is what lets the server roll the status over on a new day. It must not be
/// reused by another "signin_monthly" row, but only one of those is ever open at a time.
///
/// Why the roll-over matters: UMG/Activitydate/uw_activitydate.lua:50 only auto-sends the sign-in
/// request while the status is &lt; 2, and Sign.Req_SignDay (:386) refuses with
/// "tip.today_alreaded_signed" above that. Without a reset a player who signed once could never
/// sign again, and the client would keep re-opening the window on every refresh.
/// </summary>
public static class ActivitySignService
{
    /// <summary>Activity/Sign.lua:16 Sign.AWARD_TYPE_DAY.</summary>
    public const uint AwardTypeDay = 1;

    /// <summary>Activity/Sign.lua:17 Sign.AWARD_TYPE_AMOUNT (short sign, signshort.txt).</summary>
    public const uint AwardTypeAmount = 2;

    public const uint GroupId = AttrIds.Activity.GroupId; // Activity.nGroupId
    public const uint IdPosStep = 10;                     // Activity.IdPosStep

    private const uint SignedDayPos = 1;     // Sign.SignTag_TaskId
    private const uint TodayStatusPos = 2;   // Sign.SignDay_TaskId
    private const uint LastSignDatePos = 3;  // server bookkeeping (free DIY slot)

    /// <summary>
    /// Activity/Sign.lua:386 and UMG/Activitydate/uw_activitydate.lua:50 both stop asking once the
    /// status reaches 2, so a granted day is marked as shown immediately - the client's DaySign
    /// callback opens the GainItem panel right away.
    /// </summary>
    public const uint StatusSigned = 2;

    /// <summary>Activity/Sign.lua:240 Sign.CheckEnd counts 0 and 3 as "still has to sign today".</summary>
    public const uint StatusPending = 0;

    public const string MonthlySignClass = "signin_monthly";

    /// <summary>
    /// Activity/Activity.lua:159 GetDiyTaskId(nId, nPos) = nId * IdPosStep + nPos + 2 - every DIY slot
    /// sits two ids above the base slot of the same position (GetBaseTaskId is the one without the +2).
    /// Missing this offset writes the state two ids too low, where the client never reads it
    /// (2026-10-05: the sign-in stamp landed on sid 13 = activityId*10 + 3, the slot the client uses
    /// for Sign.SignTag_TaskId, instead of sid 15).
    /// </summary>
    private const uint DiySidOffset = 2;

    public static uint SignedDaySid(uint activityId) => activityId * IdPosStep + DiySidOffset + SignedDayPos;
    public static uint TodayStatusSid(uint activityId) => activityId * IdPosStep + DiySidOffset + TodayStatusPos;
    public static uint LastSignDateSid(uint activityId) => activityId * IdPosStep + DiySidOffset + LastSignDatePos;

    /// <summary>Local yyyymmdd, matching the wall clock the client's os.date uses.</summary>
    public static uint TodayStamp()
    {
        var now = DateTime.Now;
        return (uint)(now.Year * 10000 + now.Month * 100 + now.Day);
    }

    /// <summary>
    /// Rolls every monthly sign-in activity over to "pending" when the stored stamp is not today.
    /// Called from CompleteLogin before PacketRspLogin serializes the player, so the very first
    /// attribute sync the client sees already carries the reset status.
    /// </summary>
    public static void RollDailyState(PlayerInstance player)
    {
        foreach (var conf in GameData.ActivityData.Values)
        {
            if (!string.Equals(conf.Class, MonthlySignClass, StringComparison.OrdinalIgnoreCase))
                continue;

            RollDailyState(player, conf.Id, null);
        }
    }

    /// <summary>
    /// Puts the status back to 0 when the stored stamp is not today, so the client pops the sign-in
    /// window again (Sign.CheckSignPop -&gt; nRet &lt; 2) and auto-sends the request.
    /// </summary>
    public static void RollDailyState(PlayerInstance player, uint activityId, NtfSyncPlayer? sync)
    {
        // A finished short-sign card must not roll over: the client would pop the widget again
        // (Sign.CheckShortPop) on every login just to be told there is nothing left to sign.
        if (IsShortSignComplete(player, activityId))
            return;

        if (player.Attributes.GetValue(GroupId, LastSignDateSid(activityId)) == TodayStamp())
            return;

        var statusSid = TodayStatusSid(activityId);
        if (player.Attributes.GetValue(GroupId, statusSid) == StatusPending)
            return;

        SetAttr(player, statusSid, StatusPending, sync);
    }

    internal static void SetAttr(PlayerInstance player, uint sid, uint value, NtfSyncPlayer? sync)
    {
        var attr = player.Attributes.GetOrCreate(GroupId, sid);
        if (attr.Val == value)
            return;

        attr.Val = value;
        if (sync != null)
            player.Attributes.SyncTo(sync, attr);
    }

    /// <summary>
    /// Highest DayId configured for a short-sign card (activity/sign/signshort.json: 7 for every DLC
    /// era event, 8 for activity 2 and 14 for the older 14 day cards). Returns 0 when the activity is
    /// not a short-sign card at all (e.g. the monthly 31 day sign-in, activity 1).
    /// </summary>
    public static uint MaxShortDay(uint activityId)
    {
        var max = 0u;
        foreach (var pair in GameData.SignShortData)
        {
            if (pair.Key.ActivityId == activityId && pair.Key.DayId > max)
                max = pair.Key.DayId;
        }

        return max;
    }

    /// <summary>True once the short-sign counter reached the last configured day.</summary>
    public static bool IsShortSignComplete(PlayerInstance player, uint activityId)
    {
        var max = MaxShortDay(activityId);
        return max > 0 &&
               player.Attributes.GetValue(GroupId, SignedDaySid(activityId)) >= max;
    }
}
