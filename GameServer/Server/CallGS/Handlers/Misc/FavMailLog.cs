namespace MikuSB.GameServer.Server.CallGS.Handlers.Misc;

// Client side origin: FightLevel:WriteSequenceLog (Level/FightLevel.lua:74-84) sends animation
// play logs as fire-and-forget; the client has no s2c callback for this api. Without a handler the
// router fallback echoes the api name back and the client raises
// "S2C.lua:32 s2c.Dispatch error. No command named: FavMailLog".
[CallGSApi("FavMailLog")]
public class FavMailLog : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        return Task.FromResult(CallGSResult.NoResponse());
    }
}
