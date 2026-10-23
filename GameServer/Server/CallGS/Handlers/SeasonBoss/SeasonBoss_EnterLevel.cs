using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.GameServer.Game.SeasonBoss;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.SeasonBoss;

// 弑神斗场 (season boss) level entry. SeasonBossLogic.Req_EnterLevel
// (Challenge/SeasonBoss/SeasonBoss.lua:1177-1193) sends the chosen level plus the zone/difficulty the
// UI had selected:
//
//     { nID, nTeamID, nDifficulty, nZoneID, tbEntrie, tbLog }
//
// and the callback (:1195-1203) stores SeasonBossLogic.nSeed and calls Launch.Response, which resumes
// Launch/Classes/SeasonBossLogic.lua:OnStart -- its registered handler opens the map itself
// (SeasonBossLogic.lua:62-63 -> Map.Open(GetMapID(GetBossLevelID()))). No dedicated server is involved:
// the level row's MapID (seasonlevel.json) points at a normal co-op style map the client loads standalone.
//
// nID is the seasonleveldes.json ID column (3401..3435 for ActID 10), which is what
// SeasonBossLogic.tbLevelCfg is keyed by (SeasonBoss.lua:109/:134), so it validates against
// SeasonBossLevelData.
//
// Request:  { nID, nTeamID, nDifficulty, nZoneID, tbEntrie, tbLog }
// Response: { nSeed }
//
// Side effect: bumps the level's challenge counter (26, 10*nID + 100 + 7) and remembers nTeamID for the
// best run lineup snapshot. See SeasonBossRunService for why both live here and not in the settlement.
[CallGSApi("SeasonBoss_EnterLevel")]
public class SeasonBoss_EnterLevel : CallGSHandler<SeasonBossEnterLevelParam>
{
    private static readonly Logger Log = new(nameof(SeasonBoss_EnterLevel));

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, SeasonBossEnterLevelParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var uid = player.Uid;

        if (request.LevelId == 0 || !GameData.SeasonBossLevelData.ContainsKey(request.LevelId))
        {
            Log.Warn($"Unknown season boss level {request.LevelId} requested by uid={uid}.");
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        // The client only stores the seed and echoes it back inside the settlement/fail payloads, so any
        // non-zero value works. It doubles as the replay key for the settlement bookkeeping.
        var seed = (uint)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() & 0x7FFFFFFF);

        // Every entry counts as one challenge, including runs that end in SeasonBoss_LevelFail -- the
        // zone UI sums (26, 10*nID+100+7) over a zone's levels to show how many tries were spent
        // (SeasonBoss.lua:760-775). The same call remembers nTeamID, because neither the settlement nor
        // the record payload carries the lineup the player entered with.
        var sync = new NtfSyncPlayer();
        SeasonBossRunService.RecordEntry(player, request.LevelId, request.TeamId, sync);

        Log.Info($"SeasonBoss level {request.LevelId} entered by uid={uid} " +
                 $"(team={request.TeamId}, zone={request.ZoneId}, diff={request.Difficulty}, seed={seed}).");

        return Task.FromResult(CallGSResult.Ok(new JsonObject { ["nSeed"] = seed }, sync));
    }
}

public sealed class SeasonBossEnterLevelParam
{
    [JsonPropertyName("nID")] public uint LevelId { get; set; }
    [JsonPropertyName("nTeamID")] public uint TeamId { get; set; }
    [JsonPropertyName("nDifficulty")] public uint Difficulty { get; set; }
    [JsonPropertyName("nZoneID")] public uint ZoneId { get; set; }
    [JsonPropertyName("tbEntrie")] public JsonNode? Entries { get; set; }
    [JsonPropertyName("tbLog")] public JsonNode? Log { get; set; }
}
