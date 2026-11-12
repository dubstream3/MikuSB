using MikuSB.GameServer.Game.Dlc;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// One time rule for every 新星开拓 window (activity / capture region / trial period / season).
///
/// The DLC module owns the era: when a DLC is pinned the server serves that era, so a window that
/// closed inside the served era must stay open (that is exactly what
/// <see cref="DlcRuntimeOverrides.IsInsideServedEra"/> answers), while a window that ended before the
/// era began belongs to an earlier version and stays closed. A window that has not started yet is
/// never opened. With no pin the shipped wall-clock semantics are kept untouched, so a live server
/// behaves as before.
/// </summary>
internal static class VirCaptureEraTime
{
    private static readonly Logger Logger = new("VirCapture.EraTime");

    public static DateTime Now => ServerClock.Now;

    public static bool IsEraPinned => DlcRuntimeOverrides.PinActive;

    public static DateTime? Parse(string? raw) => DlcRuntimeOverrides.ParseConfigTime(raw);

    /// <summary>True when the row's [start, end) window is open under the served-era rule.</summary>
    public static bool IsOpen(string? startRaw, string? endRaw)
    {
        var start = Parse(startRaw);
        var end = Parse(endRaw);
        if (start == null || end == null)
            return false;

        var now = Now;
        if (start > now)
            return false;

        if (now < end)
            return true;

        // Closed on the clock: only a window that closed inside the served era may stay open.
        return DlcRuntimeOverrides.IsInsideServedEra(startRaw, endRaw);
    }

    /// <summary>
    /// Same rule with an award time that may extend the period (试炼 runs to max(end, awardTime)).
    /// </summary>
    public static bool IsOpen(string? startRaw, string? endRaw, string? awardRaw)
    {
        var end = Parse(endRaw);
        var award = Parse(awardRaw);
        if (award.HasValue && (end == null || award > end))
            end = award;

        if (end == null)
            return false;

        var start = Parse(startRaw);
        if (start == null)
            return false;

        var now = Now;
        if (start > now)
            return false;
        if (now < end)
            return true;

        var extended = awardRaw != null && Parse(awardRaw) == end;
        return DlcRuntimeOverrides.IsInsideServedEra(startRaw, extended ? awardRaw : endRaw);
    }

    /// <summary>
    /// Picks the row that is running now: the latest row whose window opened and is still open under
    /// <see cref="IsOpen(string?, string?)"/>. Rows without a parsable window are skipped.
    /// </summary>
    public static T? ResolveCurrent<T>(
        IEnumerable<T> rows,
        Func<T, string?> startOf,
        Func<T, string?> endOf)
        where T : class
    {
        T? best = null;
        DateTime bestStart = DateTime.MinValue;

        foreach (var row in rows)
        {
            var start = Parse(startOf(row));
            if (start == null || start > Now)
                continue;

            if (!IsOpen(startOf(row), endOf(row)))
                continue;

            if (start.Value >= bestStart)
            {
                bestStart = start.Value;
                best = row;
            }
        }

        return best;
    }

    /// <summary>
    /// Era-aligned client window (unix seconds) used when pushing `VirCapture_GMChangeTime`:
    /// it reaches back to the era floor so a period that closed inside the served era keeps its
    /// window on the client too, and it stretches 30 days forward like the other DLC pushes.
    /// </summary>
    public static (long Start, long End) ClientWindow()
    {
        var now = Now;
        var nowSeconds = new DateTimeOffset(now).ToUnixTimeSeconds();
        var floor = DlcRuntimeOverrides.ActivationFloor();

        var start = floor > 0 ? Math.Min(nowSeconds - 3600, floor - 3600) : nowSeconds - 3600;
        var end = nowSeconds + 30L * 24 * 60 * 60;
        return (start, end);
    }

    public static void LogResolution(string scope, string? startRaw, string? endRaw, bool open) =>
        Logger.Info($"window {scope} {startRaw}..{endRaw} open={open} (eraPinned={IsEraPinned}, now={Now:yyyy-MM-dd HH:mm})");
}
