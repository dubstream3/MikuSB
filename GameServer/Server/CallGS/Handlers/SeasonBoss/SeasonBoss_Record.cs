using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.SeasonBoss;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.SeasonBoss;

// 弑神斗场 (season boss) "record this run" confirmation. The settlement UI asks the player whether to
// keep the run (UMG/Settlement/umg_settlement.lua:1738 opens a MessageBox whose buttons call
// SeasonBossLogic.Record(true) / Record(false)), and SeasonBossLogic.Record
// (Challenge/SeasonBoss/SeasonBoss.lua:1522-1556) forwards the answer plus the run stats:
//
//     { nID, nSeed, nDiff, nZoneID, nTime, ResidueTime, nDamage, bRecord }
//
// The callback (:1558-1567) only shows the "recorded" tip when the response carries bRecord, so the
// reply echoes the player's choice after the best score / clear time have been folded in.
//
// Request:  { nID, nSeed, nDiff, nZoneID, nTime, ResidueTime, nDamage, bRecord }
// Response: { bRecord } -- true shows Text("bossentries.record")
[CallGSApi("SeasonBoss_Record")]
public class SeasonBoss_Record : CallGSHandler<SeasonBossRecordParam>
{
    private static readonly Logger Log = new(nameof(SeasonBoss_Record));

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, SeasonBossRecordParam request)
    {
        var player = context.Connection.Player;
        if (player == null || request.LevelId == 0)
            return Task.FromResult(CallGSResult.Ok(new JsonObject()));

        var sync = new NtfSyncPlayer();
        var timeSid = SeasonBossRunService.StatSid(request.LevelId, AttrIds.SeasonBoss.ClearTimeOffset);

        // Shares the best score slot (and the best run lineup snapshot) with the settlement: the
        // settlement normally got there first, so this is a no-op unless the run was only reported here.
        var improved = SeasonBossRunService.RecordBestRun(player, request.LevelId, request.Damage, sync);

        var clearTime = (uint)MathF.Round(request.Time);
        if (clearTime > 0)
        {
            var best = SeasonBossRunService.ReadStat(player, timeSid);
            if (best == 0 || clearTime < best)
                SeasonBossRunService.SetStat(player, timeSid, clearTime, sync);
        }

        DatabaseHelper.SaveDatabaseType(player.Data);

        Log.Info($"SeasonBoss level {request.LevelId} record for uid={player.Uid}: " +
                 $"bRecord={request.Record}, damage={request.Damage}, time={request.Time}, " +
                 $"bestScoreImproved={improved}.");

        return Task.FromResult(CallGSResult.Ok(new JsonObject { ["bRecord"] = request.Record }, sync));
    }
}

public sealed class SeasonBossRecordParam
{
    [JsonPropertyName("nID")] public uint LevelId { get; set; }
    [JsonPropertyName("nSeed")] public uint Seed { get; set; }
    [JsonPropertyName("nDiff")] public uint Difficulty { get; set; }
    [JsonPropertyName("nZoneID")] public uint ZoneId { get; set; }
    // nTime is SeasonBossLogic.FinishTime and ResidueTime is SeasonBossLogic.ResidueTime -- Lua floats
    // (e.g. 29.8), so they must not be declared as integers or System.Text.Json rejects the request.
    [JsonPropertyName("nTime")] public float Time { get; set; }
    [JsonPropertyName("ResidueTime")] public float ResidueTime { get; set; }
    [JsonPropertyName("nDamage")] public uint Damage { get; set; }
    [JsonPropertyName("bRecord")] public bool Record { get; set; }
}
