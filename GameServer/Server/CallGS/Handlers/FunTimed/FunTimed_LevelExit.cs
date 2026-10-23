using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.FunTimed;

// 抢滩登陆 (Sevendays / FunTimed) run exit report.
//
// Launch/FunTimed/FunTimed.lua:361-363 sends { nID, tbLog } from FunTimed.ExitLevel and registers no
// s2c callback for it. Misc/S2C.lua:29-35 raises a Lua error for every reply that has no consumer, so
// this handler must deliberately stay silent: answering would pop an error toast right when the player
// backs out of the level. The payload is only logged for diagnostics.
//
// Request:  { nID, tbLog }
// Response: none
[CallGSApi("FunTimed_LevelExit")]
public class FunTimed_LevelExit : CallGSHandler<FunTimedLevelExitParam>
{
    private static readonly Logger Log = new(nameof(FunTimed_LevelExit));

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, FunTimedLevelExitParam request)
    {
        var uid = context.Connection.Player?.Uid ?? 0;
        Log.Info($"FunTimed level {request.LevelId} exited by uid={uid}.");
        return Task.FromResult(CallGSResult.NoResponse());
    }
}

public sealed class FunTimedLevelExitParam
{
    [JsonPropertyName("nID")]
    public uint LevelId { get; set; }

    [JsonPropertyName("tbLog")]
    public JsonNode? Log { get; set; }
}
