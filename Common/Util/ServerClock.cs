using System.Globalization;

namespace MikuSB.Util;

/// <summary>
/// Server-side clock indirection so acceptance runs can pin "now" without touching the machine clock.
/// Set MIKU_FAKE_NOW="yyyy-MM-dd HH:mm:ss" (or yyyyMMddHHmm / yyyyMMddHHmmss) to freeze server time.
/// See docs/progress/073-新星开拓可执行验收契约.md — time is an input, never a side effect.
/// </summary>
public static class ServerClock
{
    private const string EnvName = "MIKU_FAKE_NOW";
    private static readonly Logger Logger = new("ServerClock");
    private static readonly Lazy<DateTime?> Pinned = new(ResolvePinned, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly string[] Formats =
    [
        "yyyy-MM-dd HH:mm:ss",
        "yyyyMMddHHmm",
        "yyyyMMddHHmmss"
    ];

    /// <summary>Wall-clock "now" for game logic (local time, same semantics as DateTime.Now).</summary>
    public static DateTime Now => Pinned.Value ?? DateTime.Now;

    public static bool IsPinned => Pinned.Value.HasValue;

    private static DateTime? ResolvePinned()
    {
        var raw = Environment.GetEnvironmentVariable(EnvName);
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (DateTime.TryParseExact(raw.Trim(), Formats, CultureInfo.InvariantCulture, DateTimeStyles.None,
                out var pinned))
        {
            Logger.Warn($"Server clock pinned to {pinned:yyyy-MM-dd HH:mm:ss} by {EnvName}; do not use in production.");
            return pinned;
        }

        Logger.Error($"Invalid {EnvName} value '{raw}', falling back to system clock. Expected yyyy-MM-dd HH:mm:ss.");
        return null;
    }
}
