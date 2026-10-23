using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Tower;

[CallGSApi("ClimbTowerLogic_RecordProgres")]
public class ClimbTowerLogic_RecordProgres : CallGSHandler<ClimbTowerRecordProgressParam>
{
    private const uint TowerGroupId = AttrIds.Tower.Gid;
    private const uint BasicProgressSid = AttrIds.Tower.BasicProgressSid;
    private const uint AdvancedProgressSid = AttrIds.Tower.AdvancedProgressSid;
    private const uint LevelStateSidBase = AttrIds.Tower.LevelStateSidBase;

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, ClimbTowerRecordProgressParam req)
    {
        var player = context.Connection.Player!;

        if (req == null || req.LevelId == 0 || req.Area <= 0)
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var cycle = ResolveCurrentCycle(GameData.ClimbTowerTimeData.Values, DateTime.Now);
        if (cycle == null)
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var towerType = ResolveTowerType(cycle, (uint)req.LevelId);
        if (towerType == 0)
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var sync = new NtfSyncPlayer();

        var levelStateSid = LevelStateSidBase + (uint)req.LevelId;
        var levelState = player.Attributes.GetOrCreate(TowerGroupId, levelStateSid);
        levelState.Val = MergeAreaStars(levelState.Val, req.Area, req.StarMask);
        player.Attributes.SyncTo(sync, levelState);

        var progressSid = towerType == 1 ? BasicProgressSid : AdvancedProgressSid;
        var progressAttr = player.Attributes.GetOrCreate(TowerGroupId, progressSid);
        progressAttr.Val = req.Area >= 3 ? 0u : PackProgress((uint)req.LevelId, (uint)(req.Area + 1));
        player.Attributes.SyncTo(sync, progressAttr);

        if (req.RoleHP.Count > 0 || req.TeamEnergy.HasValue)
        {
            SaveRoleState(player, sync, towerType, req.RoleHP, req.TeamEnergy.GetValueOrDefault());
        }

        DatabaseHelper.SaveDatabaseType(player.Data);
        return Task.FromResult(CallGSResult.Ok("{}", sync));
    }

    private static void SaveRoleState(
        PlayerInstance player,
        NtfSyncPlayer sync,
        int towerType,
        List<List<int>> roleHp,
        int teamEnergy)
    {
        var slotStart = towerType == 2 ? 4u : 1u;

        for (var slot = slotStart; slot < slotStart + 3; slot++)
        {
            var templateAttr = player.Attributes.GetOrCreate(TowerGroupId, slot * 10);
            var hpAttr = player.Attributes.GetOrCreate(TowerGroupId, slot * 10 + 1);
            templateAttr.Val = 0;
            hpAttr.Val = 0;
            player.Attributes.SyncTo(sync, templateAttr);
            player.Attributes.SyncTo(sync, hpAttr);
        }

        for (var i = 0; i < Math.Min(roleHp.Count, 3); i++)
        {
            var row = roleHp[i];
            if (row == null || row.Count < 2)
                continue;

            var slot = slotStart + (uint)i;
            var templateAttr = player.Attributes.GetOrCreate(TowerGroupId, slot * 10);
            var hpAttr = player.Attributes.GetOrCreate(TowerGroupId, slot * 10 + 1);
            templateAttr.Val = (uint)Math.Max(0, row[0]);
            hpAttr.Val = (uint)Math.Max(0, row[1]);
            player.Attributes.SyncTo(sync, templateAttr);
            player.Attributes.SyncTo(sync, hpAttr);
        }

        var energyAttr = player.Attributes.GetOrCreate(TowerGroupId, slotStart * 10 + 2);
        energyAttr.Val = (uint)Math.Max(0, teamEnergy);
        player.Attributes.SyncTo(sync, energyAttr);
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

    private static uint PackProgress(uint levelId, uint area) => (area << 24) | (levelId & 0x00FF_FFFF);

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

public sealed class ClimbTowerRecordProgressParam
{
    [JsonPropertyName("nID")]
    public int LevelId { get; set; }

    [JsonPropertyName("nArea")]
    public int Area { get; set; }

    [JsonPropertyName("nStar")]
    public int StarMask { get; set; }

    [JsonPropertyName("tbRoleHP")]
    public List<List<int>> RoleHP { get; set; } = [];

    [JsonPropertyName("nTeamEnergy")]
    public int? TeamEnergy { get; set; }
}
