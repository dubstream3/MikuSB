using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// Attribute layout of the blueprint island build layer (gid 161).
/// Building <c>id</c> owns <c>10000 + id*30 + offset</c>; the island itself uses
/// <c>1000 + mapIndex*100 + offset</c> for the operate counters.
/// </summary>
internal static class VirCaptureBuildState
{
    public const uint GroupId = AttrIds.VirCapture.BuildGroupId;
    public const uint BuildDataStart = 10000;
    public const uint BuildDataCount = 30;
    public const uint MapDataStart = 1000;
    public const uint MapDataCount = 100;

    public const uint OffLevel = 1;
    public const uint OffAttr1 = 6;
    public const uint OffWorker1 = 11;
    public const uint MaxAttr = 3;
    public const uint MaxWorker = 5;

    public const uint OffTotalOperate = 1;
    public const uint OffDailyOperate = 2;
    public const uint OffDailyOperateDate = 4;
    public const uint OffLevelOperate = 3;

    public static readonly Dictionary<uint, int> MapIndex = new() { [326] = 0 };

    private static readonly Logger Log = new("VirCapture.Build.State");

    public static bool HostsBuildings(uint mapId) => MapIndex.ContainsKey(mapId);

    public static uint BuildSid(uint buildId, uint offset) => BuildDataStart + buildId * BuildDataCount + offset;

    public static uint MapSid(uint mapId, uint offset) =>
        MapDataStart + (uint)(MapIndex.TryGetValue(mapId, out var index) ? index : 0) * MapDataCount + offset;

    public static uint GetBuildValue(PlayerInstance player, uint buildId, uint offset) =>
        player.Attributes.GetValue(GroupId, BuildSid(buildId, offset));

    public static uint GetMapValue(PlayerInstance player, uint mapId, uint offset) =>
        player.Attributes.GetValue(GroupId, MapSid(mapId, offset));

    public static void SetBuildValue(PlayerInstance player, uint buildId, uint offset, uint value, NtfSyncPlayer sync)
    {
        var attr = player.Attributes.GetOrCreate(GroupId, BuildSid(buildId, offset));
        attr.Val = value;
        player.Attributes.SyncTo(sync, attr);
    }

    public static void SetMapValue(PlayerInstance player, uint mapId, uint offset, uint value, NtfSyncPlayer sync)
    {
        var attr = player.Attributes.GetOrCreate(GroupId, MapSid(mapId, offset));
        attr.Val = value;
        player.Attributes.SyncTo(sync, attr);
    }

    public static uint GetMapLevel(PlayerInstance player, uint mapId) =>
        VirCaptureStateHelper.GetMapOffset(player, mapId, VirCaptureStateHelper.OffMapLevel);

    public static VirCaptureBuildingExcel? GetBuilding(uint buildId) =>
        GameData.VirCaptureBuildingData.TryGetValue(buildId, out var cfg) ? cfg : null;

    public static IEnumerable<VirCaptureBuildingExcel> BuildingsOf(uint mapId) =>
        GameData.VirCaptureBuildingData.Values.Where(cfg => cfg.MapId == mapId);

    public static uint AttributeLevel(PlayerInstance player, uint buildId, uint attributeIndex) =>
        GetBuildValue(player, buildId, OffAttr1 + attributeIndex - 1);

    public static uint Worker(PlayerInstance player, uint buildId, uint slot) =>
        GetBuildValue(player, buildId, OffWorker1 + slot - 1);

    /// <summary>
    /// Base revenue of one building, without the buff and staff-attribute layers.
    /// Attribute satisfaction is compared with <c>OrginNeed[level]</c>, and the earnings bonus
    /// applies once all three current attributes are satisfied (the client does the same).
    /// </summary>
    public static long BaseRevenue(PlayerInstance player, VirCaptureBuildingExcel cfg, uint level, long earningsBonus, out long cost)
    {
        cost = cfg.CostAt(level);
        var satisfied = 0;
        for (var i = 1u; i <= MaxAttr; i++)
        {
            var need = level < cfg.OrginNeed.Count && i <= cfg.OrginNeed[(int)level].Count
                ? cfg.OrginNeed[(int)level][(int)(i - 1)]
                : 0;
            if (need > 0 && cfg.AttributeValue(i, AttributeLevel(player, cfg.Id, i)) >= need)
                satisfied++;
        }

        var income = cfg.IncomeAt(level);
        if (satisfied >= MaxAttr)
            income += (long)(income * earningsBonus * 0.0001d);
        income += cfg.AttributeEarningAt(level) * satisfied;
        return income - cost;
    }

    public static long MapRevenue(PlayerInstance player, uint mapId, out long cost)
    {
        cost = 0;
        if (!GameData.VirCaptureOperatingData.TryGetValue(mapId, out var operate))
            return 0;

        long revenue = 0;
        foreach (var cfg in BuildingsOf(mapId))
        {
            var level = GetBuildValue(player, cfg.Id, OffLevel);
            if (level == 0)
                continue;
            revenue += BaseRevenue(player, cfg, level, operate.EarningsBonus, out var buildingCost);
            cost += buildingCost;
        }

        return revenue;
    }

    /// <summary>Clears the daily operate counter when the stored date stamp is not today.</summary>
    public static void EnsureDailyCounter(PlayerInstance player, uint mapId, NtfSyncPlayer sync)
    {
        var now = ServerClock.Now;
        var today = (uint)(now.Year * 10000 + now.Month * 100 + now.Day);
        var stamp = GetMapValue(player, mapId, OffDailyOperateDate);
        if (stamp == today)
            return;
        SetMapValue(player, mapId, OffDailyOperateDate, today, sync);
        SetMapValue(player, mapId, OffDailyOperate, 0, sync);
    }

    public static bool TryPay(PlayerInstance player, uint cashId, long amount, NtfSyncPlayer sync, out string error)
    {
        error = string.Empty;
        if (amount <= 0)
            return true;

        var sid = AttrIds.Currency.GetSid(cashId);
        var attr = player.Attributes.GetOrCreate(AttrIds.Currency.GroupId, sid);
        if (attr.Val < (ulong)amount)
        {
            error = "error.cost_not_enough";
            return false;
        }

        attr.Val = (uint)(attr.Val - (ulong)amount);
        player.Attributes.SyncTo(sync, attr);
        return true;
    }

    public static void AddCash(PlayerInstance player, uint cashId, long amount, NtfSyncPlayer sync)
    {
        if (cashId == 0 || amount <= 0)
            return;

        var sid = AttrIds.Currency.GetSid(cashId);
        var attr = player.Attributes.GetOrCreate(AttrIds.Currency.GroupId, sid);
        attr.Val = (uint)Math.Min(uint.MaxValue, attr.Val + (ulong)amount);
        player.Attributes.SyncTo(sync, attr);
    }

    public static void LogDenied(string scope, string reason) => Log.Info($"{scope} denied: {reason}");
}
