using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Tower;

[CallGSApi("ClimbTowerLogic_CheckCycleLevel")]
public class ClimbTowerLogic_CheckCycleLevel : CallGSHandler
{
    private const uint TowerGroupId = AttrIds.Tower.Gid;
    private const uint TimeSubId = AttrIds.Tower.TimeSid;

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player!;
        var current = ResolveCurrentCycle(GameData.ClimbTowerTimeData.Values, DateTime.Now);
        if (current == null)
        {
            return Task.FromResult(CallGSResult.Ok("{}"));
        }

        var currentTimeId = player.Attributes.GetValue(TowerGroupId, TimeSubId);
        var sync = new NtfSyncPlayer();
        if (currentTimeId != current.ID)
        {
            ResetTowerAttrs(player, sync);
            var timeAttr = player.Attributes.Set(TowerGroupId, TimeSubId, current.ID);
            player.Attributes.SyncTo(sync, timeAttr);
            DatabaseHelper.SaveDatabaseType(player.Data);
        }

        return Task.FromResult(CallGSResult.Ok($$"""{"timeID":{{current.ID}}}""", sync));
    }

    private static ClimbTowerTimeExcel? ResolveCurrentCycle(IEnumerable<ClimbTowerTimeExcel> configs, DateTime now)
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
        if (latestStarted != null)
            return latestStarted.Config;

        return parsed.FirstOrDefault()?.Config;
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
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out var value)
            ? value
            : null;
    }

    private static void ResetTowerAttrs(PlayerInstance player, NtfSyncPlayer sync)
    {
        var towerAttrs = player.Attributes.All
            .Where(x => x.Gid == TowerGroupId)
            .ToList();

        foreach (var attr in towerAttrs)
            player.Attributes.SyncTo(sync, attr.Gid, attr.Sid, 0);

        player.Attributes.RemoveWhere(x => x.Gid == TowerGroupId);
    }
}
