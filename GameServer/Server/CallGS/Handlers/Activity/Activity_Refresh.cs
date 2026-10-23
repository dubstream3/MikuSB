using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Activity;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using System.Globalization;
using System.Text.Json.Nodes;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

/// <summary>
/// Client side origin: Activity.Quest_Refresh (Activity/Activity.lua:1228-1252) is called from
/// Activity.IsOpen whenever the activity open time stored in the player attribute
/// (group 50, sid = nId * 10 + 0) differs from the open time of the row shipped in the client's
/// own copy of activity/activities.txt. It then sends this request and treats the activity as
/// closed until a refresh arrives.
///
/// There is NO s2c callback named Activity_Refresh on the client, so the answer has to be
/// delivered through ActivityQuest_Open (registered at Activity.lua:1367 -> DoOpenActivity at
/// Activity.lua:1304-1332, which copies every received key over the local tbConf row).
///
/// Every activity row of the CN client build is already expired, so a "currently open" window is
/// pushed back and (50, nId * 10 + 0) is set to the same start value: the next Quest_Refresh then
/// hits its "nServerTime == tbConf.nStartTime" early return and stops polling.
/// </summary>
[CallGSApi("Activity_Refresh")]
public class Activity_Refresh : CallGSHandler
{
    private const uint GroupId = AttrIds.Activity.GroupId; // Activity.nGroupId
    private const uint IdPosStep = 10;                     // Activity.IdPosStep
    private const long WindowBeforeSeconds = 24 * 3600;     // opened one day ago
    private const long WindowAfterSeconds = 60 * 24 * 3600; // closes in 60 days

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player;
        if (player == null)
            return Task.FromResult(CallGSResult.NoResponse());

        var activityId = ParseActivityId(param);
        if (activityId == 0)
        {
            Logger.GetByClassName().Warn($"Activity_Refresh without a usable nId (param='{param}').");
            return Task.FromResult(CallGSResult.NoResponse());
        }

        var conf = GameData.ActivityData.GetValueOrDefault(activityId);

        // Era gate: a row that ended before the pinned DLC began belongs to an earlier version, and
        // activating DLCxx only activates that DLC's content. Live-server semantics for those rows is
        // "expired = closed", so stay silent instead of stamping a fresh window - the client keeps
        // treating the activity as closed (its own refresh throttle stops the polling loop from
        // spinning) and the 特别邀请 / login splashes behind them never open.
        if (conf != null && DlcRuntimeOverrides.IsHistoricalWindow(conf.EndTime))
        {
            Logger.GetByClassName().Info(
                $"Refusing activity {activityId} (class='{conf.Class}') window {conf.StartTime}..{conf.EndTime}: " +
                $"it ended before the pinned DLC era (floor={DlcRuntimeOverrides.ActivationFloor()}) for uid={player.Data.Uid}.");
            return Task.FromResult(CallGSResult.NoResponse());
        }

        // Timestamps are sent as NUMBERS, not as "yyyyMMddHHmm" strings. The client's ParseTime /
        // os.time treat a wall-clock table as UTC (probe: os.time{year=2026,month=10,day=5,hour=15,
        // min=32} == ParseTime('202610051532') == 1791214320 == 2026-10-05 15:32 UTC), while
        // GetTime() returns a real epoch. A string formatted from DateTime.Now therefore comes back
        // as (local epoch + utc offset) - 8h here - so Quest_Refresh's |nServerTime - nStartTime|
        // test missed its 3600s tolerance, the activity stayed closed and the client re-polled every
        // 30 seconds forever (observed on 580: attr 1791185574 vs tbConf 1791214320). Sending the
        // same epoch we store in the attribute makes DoOpenActivity's key copy an exact match, which
        // is the early return Quest_Refresh wants.
        var sync = new NtfSyncPlayer();
        var (start, end) = StampOpenWindow(player, activityId, sync);
        var startText = DateTimeOffset.FromUnixTimeSeconds(start).ToLocalTime()
            .ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
        var endText = DateTimeOffset.FromUnixTimeSeconds(end).ToLocalTime()
            .ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);

        // Keep the monthly sign-in in step with the calendar. Activity.IsOpen is what Sign.CheckSignPop
        // walks (Activity/Sign.lua:279 GetSignConfigList), so a status left over from yesterday is
        // rolled back here too - the login path covers the normal case, this covers a client that stays
        // open across midnight. The reset rides along in the same attribute sync.
        if (string.Equals(conf?.Class, ActivitySignService.MonthlySignClass, StringComparison.OrdinalIgnoreCase))
            ActivitySignService.RollDailyState(player, activityId, sync);

        var payload = new JsonObject
        {
            ["nId"] = activityId,
            ["nStartTime"] = start,
            ["nEndTime"] = end,
            ["nCloseFlag"] = 0
        };

        Logger.GetByClassName().Info(
            $"Opening activity {activityId} (class='{conf?.Class ?? "?"}') window {startText}..{endText} " +
            $"(attr {start}) for uid={player.Data.Uid}.");

        return Task.FromResult(CallGSResult.Ok(payload.ToJsonString(), sync, "ActivityQuest_Open"));
    }

    /// <summary>
    /// Stamps the "currently open" window into (group 50, sid = activityId * IdPosStep) and returns the
    /// same epoch pair that goes to the client. Shared with the login path so the 先行试用 activity
    /// already has a non-zero attribute before the player clicks it: the client patch GachaTryWindows
    /// reads it back through Activity.GetOpenTime and uses it as the row's nStartTime, which is what
    /// makes Quest_Refresh's "nServerTime == tbConf.nStartTime" early return fire on the FIRST click
    /// instead of after one 30 second polling round.
    /// </summary>
    public static (uint Start, uint End) StampOpenWindow(PlayerInstance player, uint activityId, NtfSyncPlayer sync)
    {
        var startLocal = DateTime.Now.AddSeconds(-WindowBeforeSeconds);
        var endLocal = startLocal.AddSeconds(WindowAfterSeconds);
        var start = (uint)new DateTimeOffset(startLocal).ToUnixTimeSeconds();
        var end = (uint)new DateTimeOffset(endLocal).ToUnixTimeSeconds();

        SetAttr(player, activityId * IdPosStep, start, sync);
        return (start, end);
    }

    private static uint ParseActivityId(string param)
    {
        if (string.IsNullOrWhiteSpace(param))
            return 0;

        try
        {
            if (JsonNode.Parse(param) is not JsonObject json || json["nId"] == null)
                return 0;

            return uint.TryParse(json["nId"]!.ToString(), out var id) ? id : 0;
        }
        catch (Exception ex)
        {
            Logger.GetByClassName().Warn($"Activity_Refresh param could not be parsed: {ex.Message}");
            return 0;
        }
    }

    private static void SetAttr(PlayerInstance player, uint sid, uint value, NtfSyncPlayer sync)
    {
        var attr = player.Attributes.GetOrCreate(GroupId, sid);
        if (attr.Val != value)
        {
            attr.Val = value;
            player.Attributes.SyncTo(sync, attr);
        }
    }
}
