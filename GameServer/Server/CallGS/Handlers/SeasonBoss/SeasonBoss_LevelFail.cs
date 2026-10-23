using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.SeasonBoss;

// 弑神斗场 (season boss) failure report. SeasonBossLogic.Req_LevelFail
// (Challenge/SeasonBoss/SeasonBoss.lua:1423-1506) fires for every non-success ending -- dead (nReason 3),
// overtime (4), manual exit (2) or disconnect (5) -- and the callback (:1510-1519) only runs the UI
// follow-up and surfaces an "err" key if the server rejects the report.
//
// Nothing is persisted: the mode has no attempt limit, no stamina cost and no penalty, and the client's
// own best score/time bookkeeping is only fed by SeasonBoss_Record and SeasonBoss_LevelSettlement.
//
// Request:  { nID, nSeed, nDiff, nZoneID, nDamage, nTime, ResidueTime, nReason, tbLog }
// Response: {}
[CallGSApi("SeasonBoss_LevelFail")]
public class SeasonBoss_LevelFail : CallGSHandler<SeasonBossLevelFailParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, SeasonBossLevelFailParam request)
    {
        return Task.FromResult(CallGSResult.Ok(new JsonObject()));
    }
}

public sealed class SeasonBossLevelFailParam
{
    [JsonPropertyName("nID")] public uint LevelId { get; set; }
    [JsonPropertyName("nSeed")] public uint Seed { get; set; }
    [JsonPropertyName("nDiff")] public uint Difficulty { get; set; }
    [JsonPropertyName("nZoneID")] public uint ZoneId { get; set; }
    [JsonPropertyName("nDamage")] public uint Damage { get; set; }
    // nTime is SeasonBossLogic.FinishTime and ResidueTime is SeasonBossLogic.ResidueTime -- both are
    // Lua floats (e.g. 29.8), so they must not be declared as integers or System.Text.Json rejects the
    // whole request with "The JSON value could not be converted to System.UInt32".
    [JsonPropertyName("nTime")] public float Time { get; set; }
    [JsonPropertyName("ResidueTime")] public float ResidueTime { get; set; }
    [JsonPropertyName("nReason")] public uint Reason { get; set; }
    [JsonPropertyName("tbLog")] public JsonNode? Log { get; set; }
}
