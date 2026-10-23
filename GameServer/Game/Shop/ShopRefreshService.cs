using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using MikuSB.Util.Extensions;
using System.Globalization;

namespace MikuSB.GameServer.Game.Shop;

/// <summary>
/// Period bookkeeping for the main shop tabs: when the current shelf / purchase-limit window started,
/// what to re-roll when it rolls over, and what a manual refresh costs.
///
/// The client derives the SAME window boundaries locally and only uses them to decide whether to
/// offer a refresh button or show a countdown (Script/Shop/Shop.lua:403-451 GetNextRefreshTime), so
/// the server has to agree with those rules or the two drift apart:
///   RefreshRule 1 -> every day at the configured tbRefreshTime, e.g. "0400"
///   RefreshRule 2 -> Monday 04:00
///   RefreshRule 3 -> the 1st of the month at 04:00
///   RefreshRule 4 -> follows the activity's own end time (ClimbTower); we approximate with the
///                    tower event window so the tab still rolls over instead of freezing forever
/// The same rule numbers are reused by a goods row's LimitType, which is why both windows are
/// computed by one method.
/// </summary>
public static class ShopRefreshService
{
    private static readonly Logger Log = new(nameof(ShopRefreshService));

    /// <summary>RefreshRule/LimitType values the client knows about.</summary>
    public const uint RuleDaily = 1;
    public const uint RuleWeekly = 2;
    public const uint RuleMonthly = 3;
    public const uint RuleActivity = 4;
    public const uint RulePermanent = 5;

    private const int RefreshHour = 4;

    /// <summary>Returns the tab's stored period state, creating it on first use.</summary>
    public static ShopWindowData GetWindow(PlayerInstance player, uint shopId)
    {
        var list = player.Data.ShopWindows;
        var entry = list.FirstOrDefault(x => x.ShopId == shopId);
        if (entry != null)
            return entry;

        entry = new ShopWindowData { ShopId = shopId };
        list.Add(entry);
        return entry;
    }

    /// <summary>
    /// Rolls the tab over if either of its windows has expired. <paramref name="goodsRolled"/> is set
    /// when the stored goods list belongs to an older period (the caller has to re-send it) and
    /// <paramref name="limitsRolled"/> when the purchase counters belong to an older period (the
    /// caller has to zero them). <paramref name="changed"/> reports whether anything was persisted.
    /// </summary>
    public static void RollWindows(PlayerInstance player, ShopTabExcel tab, out bool goodsRolled,
        out bool limitsRolled, out bool changed)
    {
        changed = false;
        goodsRolled = false;
        limitsRolled = false;

        var window = GetWindow(player, tab.Id);
        var now = Extensions.GetUnixSec();

        var goodsStart = WindowStart(tab.RefreshRule, tab.RefreshTime, now);
        if (window.GoodsWindow != goodsStart)
        {
            // A zero GoodsWindow only means "first time we look at this tab", not "the shelf rolled",
            // so the caller is not asked to re-send a list it has never sent.
            goodsRolled = window.GoodsWindow != 0;
            window.GoodsWindow = goodsStart;
            window.RefreshNum = 0;
            changed = true;
        }

        var limitStart = LimitWindowStart(tab, now);
        if (window.LimitWindow != limitStart)
        {
            limitsRolled = window.LimitWindow != 0;
            window.LimitWindow = limitStart;
            changed = true;
        }
    }

    /// <summary>
    /// Start of the period that contains <paramref name="now"/> for a refresh rule. The window start
    /// is the most recent boundary at or before now, which is what makes "has the period changed?"
    /// a plain comparison of two longs.
    /// </summary>
    public static long WindowStart(uint rule, string? refreshTime, long now)
    {
        var stamp = DateTimeOffset.FromUnixTimeSeconds(now).ToLocalTime();
        var hour = ReadRefreshHour(refreshTime);

        switch (rule)
        {
            case RuleDaily:
            {
                var today = new DateTimeOffset(stamp.Year, stamp.Month, stamp.Day, hour, 0, 0, stamp.Offset);
                return today.ToUnixTimeSeconds() <= now
                    ? today.ToUnixTimeSeconds()
                    : today.AddDays(-1).ToUnixTimeSeconds();
            }

            case RuleWeekly:
            {
                // Weeks run Monday 04:00 -> next Monday 04:00, matching Shop.lua:419-428.
                var daysSinceMonday = ((int)stamp.DayOfWeek + 6) % 7;
                var monday = new DateTimeOffset(stamp.Year, stamp.Month, stamp.Day, hour, 0, 0, stamp.Offset)
                    .AddDays(-daysSinceMonday);
                return monday.ToUnixTimeSeconds() <= now
                    ? monday.ToUnixTimeSeconds()
                    : monday.AddDays(-7).ToUnixTimeSeconds();
            }

            case RuleMonthly:
            {
                var first = new DateTimeOffset(stamp.Year, stamp.Month, 1, hour, 0, 0, stamp.Offset);
                return first.ToUnixTimeSeconds() <= now
                    ? first.ToUnixTimeSeconds()
                    : first.AddMonths(-1).ToUnixTimeSeconds();
            }

            case RuleActivity:
            {
                // Shop.lua:443-448 asks ClimbTowerLogic.GetEndTime(); climb_tower_time.json is the
                // server-side equivalent. When no tower window is configured the tab never rolls,
                // which is also what the client shows.
                return LatestTowerEventEnd(now);
            }

            default:
                // 0 (no rule) and 5 (permanent) never roll over.
                return 0;
        }
    }

    /// <summary>
    /// The purchase-limit window for a tab. A tab's goods can carry several LimitTypes at once, so
    /// this returns the SHORTEST configured period: resetting counters more often than necessary is
    /// harmless, whereas resetting too rarely would wrongly keep goods sold out.
    /// </summary>
    private static long LimitWindowStart(ShopTabExcel tab, long now)
    {
        var shortest = 0L;
        var found = false;

        foreach (var goods in tab.Goods)
        {
            if (goods.LimitType == 0 || goods.LimitNum <= 0)
                continue;

            var start = WindowStart(goods.LimitType, tab.RefreshTime, now);
            if (start == 0)
                continue;

            if (!found || start > shortest)
            {
                shortest = start;
                found = true;
            }
        }

        return found ? shortest : 0;
    }

    /// <summary>
    /// Clears the purchase counters for a tab whose limit window has rolled over. Counters live in
    /// <c>(AttrIds.Shop.PurchaseGid, goodsId)</c>, which is also what the client reads back through
    /// the <c>{buynum}</c> string attribute, so both have to be zeroed together.
    /// </summary>
    public static int ResetPurchaseCounters(PlayerInstance player, ShopTabExcel tab)
    {
        var cleared = 0;
        foreach (var goods in tab.Goods)
        {
            var attr = player.Attributes.Get(AttrIds.Shop.PurchaseGid, goods.Id);
            if (attr == null || attr.Val == 0)
                continue;

            attr.Val = 0;
            cleared++;
        }

        return cleared;
    }

    /// <summary>
    /// Cost of the next manual refresh, or null when the tab cannot be refreshed by hand. Mirrors
    /// Shop.lua:385-400: the fee list is indexed by the refresh number already used, and a tab with
    /// <c>RefreshLimits == 0</c> has no manual refresh at all.
    /// </summary>
    public static (uint MoneyType, uint Cost)? NextRefreshFee(ShopTabExcel tab, uint usedRefreshes)
    {
        if (tab.RefreshLimits == 0)
            return null;

        var fees = ParseCsvUints(tab.RefreshFee);
        if (fees.Count == 0)
            return null;

        var index = (int)Math.Min(usedRefreshes, (uint)fees.Count - 1);
        var cost = fees[index];
        if (cost == 0)
            return null;

        return (tab.MoneyType, cost);
    }

    /// <summary>
    /// Re-rolls the tab's shelf for a manual refresh: charges the fee, bumps the refresh counter and
    /// reports the new goods order. The order itself is deterministic (sorted by PosIndex) because
    /// the converted table only ships the already-selected goods - there is no wider pool to draw
    /// from - so "re-rolling" here means re-reading the configured list for the new window.
    /// </summary>
    public static string? TryManualRefresh(PlayerInstance player, ShopTabExcel tab, NtfSyncPlayer sync)
    {
        var window = GetWindow(player, tab.Id);
        var now = Extensions.GetUnixSec();

        // A refresh after the window rolled over is free: the shelf already changed by itself.
        if (window.GoodsWindow != WindowStart(tab.RefreshRule, tab.RefreshTime, now))
            return null;

        if (tab.RefreshLimits > 0 && window.RefreshNum >= tab.RefreshLimits)
            return "ui.TxtLimitBuy";

        var fee = NextRefreshFee(tab, window.RefreshNum);
        if (fee == null)
            return "ui.TxtNotOpen";

        if (!CurrencyService.TrySpend(player, fee.Value.MoneyType, fee.Value.Cost, sync, out var error))
            return error;

        window.RefreshNum++;
        player.Attributes.SyncTo(sync, player.Attributes.Set(AttrIds.Shop.RedDotGid, tab.Id, window.RefreshNum));

        Log.Info(
            $"Shop {tab.Id} manually refreshed by uid={player.Uid}: " +
            $"{window.RefreshNum}/{tab.RefreshLimits}, paid {fee.Value.Cost}x " +
            $"{CurrencyService.Describe(fee.Value.MoneyType)}.");

        return null;
    }

    /// <summary>Refresh counter shown to the client as "used / limit".</summary>
    public static uint RefreshNum(PlayerInstance player, uint shopId) => GetWindow(player, shopId).RefreshNum;

    /// <summary>Parses the "{50,50,50,50,50}" / "[0400]" spellings the converted table keeps verbatim.</summary>
    private static List<uint> ParseCsvUints(string? text)
    {
        var result = new List<uint>();
        if (string.IsNullOrWhiteSpace(text))
            return result;

        foreach (var part in text.Trim().Trim('[', ']', '{', '}').Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (uint.TryParse(part.Trim(), out var value))
                result.Add(value);
        }

        return result;
    }

    /// <summary>Refresh hour from a "[0400]" style column; falls back to 04:00.</summary>
    private static int ReadRefreshHour(string? refreshTime)
    {
        var values = ParseCsvUints(refreshTime);
        if (values.Count == 0)
            return RefreshHour;

        // The column is HHMM without a separator ("0400").
        var raw = values[0];
        var hour = raw / 100;
        return hour is >= 0 and <= 23 ? (int)hour : RefreshHour;
    }

    /// <summary>
    /// End of the latest tower window that already started, used for RefreshRule 4. Returns 0 when no
    /// tower window is configured, which keeps the tab permanently in one window.
    /// </summary>
    private static long LatestTowerEventEnd(long now)
    {
        var best = 0L;
        foreach (var window in GameData.ClimbTowerTimeData.Values)
        {
            var end = ParseStamp(window.EndTime);
            if (end == 0 || end <= now)
                continue;

            var start = ParseStamp(window.StartTime);
            if (start > now)
                continue;

            if (end > best)
                best = end;
        }

        return best;
    }

    /// <summary>
    /// Parses the "202509010400" / "2025-09-01 04:00:00" spellings the tables use. Returns 0 when the
    /// text is empty or unrecognised, so callers can treat "no window" as a distinct case.
    /// </summary>
    private static long ParseStamp(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        var trimmed = text.Trim().Trim('"');
        if (trimmed.Length == 0)
            return 0;

        string[] formats = ["yyyyMMddHHmm", "yyyyMMddHHmmss", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm"];
        if (DateTime.TryParseExact(trimmed, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out var parsed))
        {
            return new DateTimeOffset(parsed, TimeZoneInfo.Local.GetUtcOffset(parsed)).ToUnixTimeSeconds();
        }

        return long.TryParse(trimmed, out var raw) && raw > 0 ? raw : 0;
    }
}
