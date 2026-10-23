using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

// Client side origin: DonoDuelArena:Req_LevelFail (DLC/DonoDuel/DonoDuelArena.lua:673-687). The client
// sends it fire and forget - there is no s2c.Register for the api and no connection overlay around it -
// so the server must not answer: a reply would only make Misc/S2C.lua:29-36 log
// "s2c.Dispatch error. No command named: DonoDuelArena_LevelFail" while the player is looking at the
// defeat screen. Before this handler existed the framework answered with a "No handler" error instead
// (see Config/Logs/Server.log, the only two such entries), and the arena defeat was never recorded.
//
// Request:  { "nID": <arena level id>, "tbLog": {...}, "tbKill": {...} }
// Response: none.
[CallGSApi("DonoDuelArena_LevelFail")]
public class DonoDuelArena_LevelFail : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player!;
        Logger.GetByClassName().Info($"Donoduel arena run failed for uid={player.Uid}: {param}");
        return Task.FromResult(CallGSResult.NoResponse());
    }
}
