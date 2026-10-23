using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

[CallGSApi("VirCaptureLevel_EnterLevel")]
public class VirCaptureLevel_EnterLevel : CallGSHandler<VirCaptureEnterLevelParam>
{
    private const uint GroupId = AttrIds.VirCapture.Gid;
    private const uint MapDataStart = AttrIds.VirCapture.MapDataStartSid;
    private const uint MaxMapCount = 3;
    private const uint MaxMapDataLen = AttrIds.VirCapture.MaxMapDataLength;
    private const uint OffMapId = 1;
    private const uint OffDayNight = 7;
    private const uint OffMapLevel = 8;
    private static readonly Random Random = new();

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, VirCaptureEnterLevelParam req)
    {

        if (req == null || req.LevelId == 0 || req.TeamId <= 0)
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var now = DateTime.Now;
        var act = ResolveCurrent(GameData.VirCaptureTimeData.Values, now);
        if (act == null || !act.CaptureRegionId.Contains((uint)req.LevelId))
        {
            return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));
        }

        if (!GameData.VirCaptureCaptureRegionData.TryGetValue((uint)req.LevelId, out var region))
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var regionStart = ParseConfigTime(region.StartTime);
        var regionEnd = ParseConfigTime(region.EndTime);
        if (!regionStart.HasValue || !regionEnd.HasValue || now < regionStart.Value || now >= regionEnd.Value)
        {
            return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));
        }

        var player = context.Connection.Player!;
        var sync = new NtfSyncPlayer();
        EnsureMapState(player, (uint)req.LevelId, sync);

        var rsp = $"{{\"nSeed\":{Random.Next(1, 1_000_000_000)}}}";
        return Task.FromResult(CallGSResult.Ok(rsp, sync));
    }

    private static void EnsureMapState(PlayerInstance player, uint levelId, NtfSyncPlayer sync)
    {
        var slotStart = FindOrAllocateMapSlot(player, levelId);
        if (slotStart == 0)
            return;

        EnsureMapAttr(player, slotStart + OffMapId, levelId, sync);
        EnsureMapAttr(player, slotStart + OffDayNight, 1, sync);
        EnsureMapAttr(player, slotStart + OffMapLevel, 1, sync);
    }

    private static uint FindOrAllocateMapSlot(PlayerInstance player, uint levelId)
    {
        uint? emptySlot = null;
        for (uint i = 0; i < MaxMapCount; i++)
        {
            var slotStart = MapDataStart + (i * MaxMapDataLen);
            var mapIdAttr = player.Attributes.Get(GroupId, slotStart + OffMapId);
            if (mapIdAttr?.Val == levelId)
                return slotStart;

            if (emptySlot == null && (mapIdAttr == null || mapIdAttr.Val == 0))
                emptySlot = slotStart;
        }

        return emptySlot ?? 0;
    }

    private static void EnsureMapAttr(PlayerInstance player, uint sid, uint minValue, NtfSyncPlayer sync)
    {
        var attr = player.Attributes.Get(GroupId, sid);
        if (attr == null)
        {
            attr = player.Attributes.Set(GroupId, sid, minValue);
            player.Attributes.SyncTo(sync, attr);
            return;
        }

        if (attr.Val < minValue)
        {
            attr.Val = minValue;
            player.Attributes.SyncTo(sync, attr);
        }
    }

    private static VirCaptureTimeExcel? ResolveCurrent(IEnumerable<VirCaptureTimeExcel> configs, DateTime now)
    {
        var parsed = configs
            .Select(x => new
            {
                Config = x,
                Start = ParseConfigTime(x.StartTime),
                End = ParseConfigTime(x.EndTime)
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
}

public sealed class VirCaptureEnterLevelParam
{
    [JsonPropertyName("nLevelID")]
    public int LevelId { get; set; }

    [JsonPropertyName("nTeamID")]
    public int TeamId { get; set; }
}
