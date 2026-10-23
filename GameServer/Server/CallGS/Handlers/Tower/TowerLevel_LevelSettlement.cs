using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Tower;

[CallGSApi("TowerLevel_LevelSettlement")]
public class TowerLevel_LevelSettlement : CallGSHandler
{
    private static readonly Logger Logger = new("Tower");
    private const uint TowerGroupId = AttrIds.Tower.Gid;
    private const uint LaunchPassGroupId = AttrIds.Tower.PassGid;
    private const uint BasicProgressSid = AttrIds.Tower.BasicProgressSid;
    private const uint AdvancedProgressSid = AttrIds.Tower.AdvancedProgressSid;
    private const uint LevelStateSidBase = AttrIds.Tower.LevelStateSidBase;
    private const int FinalArea = 3;

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var (response, sync) = HandleSettlement(context.Connection.Player!, JsonNode.Parse(param));
        return Task.FromResult(CallGSResult.Ok(response.ToJsonString(), sync));
    }

    public static (JsonNode Response, NtfSyncPlayer Sync) HandleSettlement(PlayerInstance player, JsonNode? tbParam)
    {
        var req = tbParam?.Deserialize<TowerLevelSettlementParam>();
        if (req == null || req.TowerId == 0 || req.LevelId == 0)
        {
            Logger.Error($"Invalid tower settlement payload: {tbParam?.ToJsonString() ?? "null"}");
            return (new JsonObject { ["sErr"] = "error.BadParam" }, new NtfSyncPlayer());
        }

        var cycle = ResolveCurrentCycle(GameData.ClimbTowerTimeData.Values, DateTime.Now);
        if (cycle == null)
            return (new JsonObject { ["sErr"] = "error.BadParam" }, new NtfSyncPlayer());

        var towerType = ResolveTowerType(cycle, (uint)req.TowerId);
        if (towerType == 0)
            return (new JsonObject { ["sErr"] = "error.BadParam" }, new NtfSyncPlayer());

        var sync = new NtfSyncPlayer();
        var levelStateSid = LevelStateSidBase + (uint)req.TowerId;
        var levelState = player.Attributes.GetOrCreate(TowerGroupId, levelStateSid);
        levelState.Val = MergeAreaStars(levelState.Val, FinalArea, req.StarMask);
        player.Attributes.SyncTo(sync, levelState);

        var progressSid = towerType == 1 ? BasicProgressSid : AdvancedProgressSid;
        var progressAttr = player.Attributes.GetOrCreate(TowerGroupId, progressSid);
        progressAttr.Val = 0;
        player.Attributes.SyncTo(sync, progressAttr);

        var passAttr = player.Attributes.GetOrCreate(LaunchPassGroupId, (uint)req.LevelId);
        passAttr.Val = Math.Max(1u, passAttr.Val + 1);
        player.Attributes.SyncTo(sync, passAttr);

        Logger.Info(
            $"Tower settlement saved. uid={player.Uid} towerId={req.TowerId} levelId={req.LevelId} starMask={req.StarMask} " +
            $"towerStateSid={levelStateSid} towerStateVal={levelState.Val} progressSid={progressSid} passVal={passAttr.Val}");

        DatabaseHelper.SaveDatabaseType(player.Data);
        return (new JsonObject(), sync);
    }

    private static uint MergeAreaStars(uint currentValue, int area, int starMask)
    {
        var areaIndex = Math.Clamp(area, 1, 3) - 1;
        var result = currentValue;
        for (var i = 0; i < 3; i++)
        {
            if (((starMask >> i) & 1) == 0)
                continue;

            var bitIndex = areaIndex * 3 + i;
            result |= 1u << bitIndex;
        }

        return result;
    }

    private static int ResolveTowerType(ClimbTowerTimeExcel cycle, uint levelId)
    {
        if (ContainsLevel(cycle.GetLevelGroups(1), levelId))
            return 1;

        if (ContainsLevel(cycle.GetLevelGroups(2), levelId))
            return 2;

        return 0;
    }

    private static bool ContainsLevel(IEnumerable<IReadOnlyList<uint>> groups, uint levelId)
    {
        return groups.Any(group => group.Any(id => id == levelId));
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

}

internal sealed class TowerLevelSettlementParam
{
    [JsonPropertyName("nID")]
    public int LevelId { get; set; }

    [JsonPropertyName("nTowerID")]
    public int TowerId { get; set; }

    [JsonPropertyName("nStar")]
    public int StarMask { get; set; }
}
