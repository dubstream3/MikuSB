using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using System.Globalization;
using System.Text.Json.Nodes;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

[CallGSApi("VirCapture_CheckOpenAct")]
public class VirCapture_CheckOpenAct : CallGSHandler
{
    private const uint GroupId = AttrIds.VirCapture.Gid;
    private const uint ActIdSid = AttrIds.VirCapture.ActivitySid;
    private const uint CurLevelSid = AttrIds.VirCapture.CurrentLevelSid;
    private const uint TrialActIdSid = AttrIds.VirCapture.TrialActIdSid;
    private const uint SeasonActIdSid = AttrIds.VirCapture.SeasonActIdSid;

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var now = ServerClock.Now;
        var act = VirCaptureEraTime.ResolveCurrent(
            GameData.VirCaptureTimeData.Values, x => x.StartTime, x => x.EndTime);
        if (act == null)
        {
            return Task.FromResult(CallGSResult.Ok("{\"bOpen\":false}"));
        }

        var player = context.Connection.Player!;
        var sync = new NtfSyncPlayer();

        SetAttr(player, ActIdSid, act.Id, sync);
        EnsureMinAttr(player, CurLevelSid, 1, sync);

        // nAwardTime is a settle time the client only accepts when it is later than
        // the period end, so falling back to the end time keeps the old behaviour.
        var endTime = ParseConfigTime(act.EndTime);
        var awardTime = ParseConfigTime(act.AwardTime) ?? endTime;

        var response = new JsonObject
        {
            ["bOpen"] = true,
            ["nId"] = act.Id,
            ["nStartTime"] = ToUnixSeconds(ParseConfigTime(act.StartTime)),
            ["nEndTime"] = ToUnixSeconds(endTime),
            ["nAwardTime"] = ToUnixSeconds(awardTime)
        };

        var season = VirCaptureEraTime.ResolveCurrent(
            GameData.VirCaptureSeasonData.Values, x => x.StartTime, x => x.EndTime);
        if (season != null)
        {
            SetAttr(player, SeasonActIdSid, season.Id, sync);
            response["tbSeason"] = new JsonObject
            {
                ["nId"] = season.Id,
                ["nStartTime"] = ToUnixSeconds(ParseConfigTime(season.StartTime)),
                ["nEndTime"] = ToUnixSeconds(ParseConfigTime(season.EndTime))
            };
        }
        else
        {
            SetAttr(player, SeasonActIdSid, 0, sync);
        }

        // 试炼期次以 max(EndTime, AwardTime) 作为关闭时间（VirCaptureConfig:CheckOpenTrialConfig），
        // 并且和其它窗口一样服从所服务的 DLC 时代：pin 时代内已过期的期次仍然开放。
        var trial = ResolveCurrentTrial();
        SetAttr(player, TrialActIdSid, trial?.Id ?? 0, sync);
        if (trial != null)
        {
            response["nTrialId"] = trial.Id;
            response["nTrialStartTime"] = ToUnixSeconds(ParseConfigTime(trial.StartTime));
            response["nTrialEndTime"] = ToUnixSeconds(ParseConfigTime(trial.EndTime));
            response["nTrialAwardTime"] = ToUnixSeconds(ParseConfigTime(trial.AwardTime));
        }

        return Task.FromResult(CallGSResult.Ok(response.ToJsonString(), sync));
    }

    private static VirCaptureTrialTimeExcel? ResolveCurrentTrial()
    {
        VirCaptureTrialTimeExcel? best = null;
        DateTime bestStart = DateTime.MinValue;

        foreach (var row in GameData.VirCaptureTrialTimeData.Values)
        {
            var start = ParseConfigTime(row.StartTime);
            if (start == null || start > VirCaptureEraTime.Now)
                continue;

            if (!VirCaptureEraTime.IsOpen(row.StartTime, row.EndTime, row.AwardTime))
                continue;

            if (start.Value >= bestStart)
            {
                bestStart = start.Value;
                best = row;
            }
        }

        return best;
    }

    private static DateTime? ParseConfigTime(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var normalized = raw.Trim().Trim('[', ']');
        if (normalized.Length != 12)
            return null;

        return DateTime.TryParseExact(
            normalized,
            "yyyyMMddHHmm",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var value)
            ? value
            : null;
    }

    private static long ToUnixSeconds(DateTime? value)
    {
        return value.HasValue ? new DateTimeOffset(value.Value).ToUnixTimeSeconds() : 0L;
    }

    private static void EnsureMinAttr(PlayerInstance player, uint sid, uint minValue, NtfSyncPlayer sync)
    {
        var attr = player.Attributes.GetOrCreate(GroupId, sid);
        if (attr.Val < minValue)
        {
            attr.Val = minValue;
            player.Attributes.SyncTo(sync, attr);
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
