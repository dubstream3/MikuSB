using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.FunTimed;

// 抢滩登陆 (Sevendays / FunTimed) level entry.
//
// Launch/FunTimed/FunTimed.lua:344-347 sends { nID, nSession, nTeamID, tbLog } right after
// FunTimedLaunchLogic.lua:20 raised the full screen connection overlay, so a missing handler leaves the
// player on a spinner forever. The s2c callback (:350-356) only needs nSeed and hands it back to
// OnStart, which opens the map locally (Map.Open(tbLevel.nMapID)) -- there is no dedicated server and
// no Chapter_EnterLevel hop for this mode, the seed is echoed back in the settlement payload.
//
// Request:  { nID, nSession, nTeamID, tbLog }
// Response: { nSeed }
[CallGSApi("FunTimed_EnterLevel")]
public class FunTimed_EnterLevel : CallGSHandler<FunTimedEnterLevelParam>
{
    private static readonly Logger Log = new(nameof(FunTimed_EnterLevel));

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, FunTimedEnterLevelParam request)
    {
        var player = context.Connection.Player;
        var uid = player?.Uid ?? 0;

        if (request.LevelId == 0 || !GameData.FunTimedLevelData.TryGetValue(request.LevelId, out var level))
        {
            Log.Warn($"Unknown FunTimed level {request.LevelId} (session {request.Session}) requested by uid={uid}.");
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        // Any non-zero seed works: the client only stores it and echoes it in the settlement payload.
        var seed = (uint)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() & 0x7FFFFFFF);

        Log.Info($"FunTimed level {request.LevelId} (type={level.Type}, session={request.Session}) " +
                 $"entered by uid={uid} (seed={seed}).");

        var response = new JsonObject { ["nSeed"] = seed };
        return Task.FromResult(CallGSResult.Ok(response.ToJsonString()));
    }
}

public sealed class FunTimedEnterLevelParam
{
    [JsonPropertyName("nID")]
    public uint LevelId { get; set; }

    [JsonPropertyName("nSession")]
    public uint Session { get; set; }

    [JsonPropertyName("nTeamID")]
    public uint TeamId { get; set; }

    [JsonPropertyName("tbLog")]
    public JsonNode? Log { get; set; }
}
