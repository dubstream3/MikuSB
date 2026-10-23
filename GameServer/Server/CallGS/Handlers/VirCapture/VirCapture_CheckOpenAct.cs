using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
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
        var now = DateTime.Now;
        var act = ResolveCurrent(GameData.VirCaptureTimeData.Values, now);
        if (act == null)
        {
            return Task.FromResult(CallGSResult.Ok("{\"bOpen\":false}"));
        }

        var player = context.Connection.Player!;
        var sync = new NtfSyncPlayer();

        SetAttr(player, ActIdSid, act.Id, sync);
        EnsureMinAttr(player, CurLevelSid, 1, sync);

        var response = new JsonObject
        {
            ["bOpen"] = true,
            ["nId"] = act.Id,
            ["nStartTime"] = ToUnixSeconds(ParseConfigTime(act.StartTime)),
            ["nEndTime"] = ToUnixSeconds(ParseConfigTime(act.EndTime))
        };

        var season = ResolveCurrent(GameData.VirCaptureSeasonData.Values, now);
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

        var trial = ResolveCurrent(GameData.VirCaptureTrialTimeData.Values, now);
        SetAttr(player, TrialActIdSid, trial?.Id ?? 0, sync);

        return Task.FromResult(CallGSResult.Ok(response.ToJsonString(), sync));
    }

    private static T? ResolveCurrent<T>(IEnumerable<T> configs, DateTime now) where T : class
    {
        var parsed = configs
            .Select(x => new
            {
                Config = x,
                Start = ParseConfigTime(GetTimeValue(x, true)),
                End = ParseConfigTime(GetTimeValue(x, false))
            })
            .Where(x => x.Start.HasValue && x.End.HasValue)
            .OrderBy(x => x.Start)
            .ToList();

        var current = parsed.FirstOrDefault(x => x.Start <= now && now < x.End);
        if (current != null)
            return current.Config;

        var latestStarted = parsed.LastOrDefault(x => x.Start <= now);
        if (latestStarted != null && latestStarted.End > latestStarted.Start)
            return latestStarted.Config;

        return null;
    }

    private static string? GetTimeValue<T>(T value, bool start) where T : class
    {
        return value switch
        {
            VirCaptureTimeExcel time => start ? time.StartTime : time.EndTime,
            VirCaptureSeasonExcel season => start ? season.StartTime : season.EndTime,
            _ => null
        };
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
