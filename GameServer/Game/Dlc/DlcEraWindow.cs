namespace MikuSB.GameServer.Game.Dlc;

/// <summary>
/// Pure half-open era window arithmetic, shared by every server-side selector and by the
/// <c>/dlc</c> control path so that "does this row belong to the served era" has exactly one
/// definition (docs/contracts/activity-era-architecture.md 3.3).
///
/// Every window is <c>[start, end)</c>:
/// <list type="bullet">
/// <item>a row that starts exactly at <c>floor</c> belongs to the era;</item>
/// <item>a row that starts exactly at <c>stop</c> does not (it belongs to the next era);</item>
/// <item>a row that ends exactly at <c>floor</c> does not belong (it closed as the era opened);</item>
/// <item><c>0</c> means "unset", not "the epoch": an unset start is never membership evidence.</item>
/// </list>
///
/// The helpers take unix seconds as plain numbers and have no dependency on GameData, which is what
/// lets the L1 tests exercise the boundary truth table without booting the server.
/// </summary>
public static class DlcEraWindow
{
    /// <summary>
    /// A window is historical when it closed at or before the era opened. Permanent rows (empty end,
    /// -1) are never historical: their end parses to 0 and stays unknown rather than "expired".
    /// </summary>
    public static bool IsHistorical(long floor, long windowEnd) =>
        floor > 0 && windowEnd > 0 && windowEnd <= floor;

    /// <summary>
    /// Eligibility test for rows that grant or unlock something during the served era. With no era
    /// pinned (<paramref name="floor"/> &lt;= 0) the shipped live-server semantics stay in force, so
    /// everything answers true; the half-open checks only apply once a pin exists.
    /// </summary>
    public static bool IsInside(long floor, long stop, long start, long end)
    {
        if (floor <= 0)
            return true;

        if (start <= 0 || start < floor)
            return false;

        if (stop > 0 && start >= stop)
            return false;

        if (end > 0 && end <= floor)
            return false;

        return true;
    }

    /// <summary>
    /// Membership test for content rows (gacha acts, seasons, defend acts). Unlike
    /// <see cref="IsInside"/> an unpinned era is <em>not</em> a wildcard here: a selector only ever
    /// runs to answer "what belongs to era N", and with no N there is no membership to report. Rows
    /// whose start is unknown are not members either - "unknown" must never be read as "current".
    /// </summary>
    public static bool Intersects(long floor, long stop, long start, long end)
    {
        if (floor <= 0)
            return false;

        if (start <= 0)
            return false;

        if (stop > 0 && start >= stop)
            return false;

        if (end > 0 && end <= floor)
            return false;

        return true;
    }
}
