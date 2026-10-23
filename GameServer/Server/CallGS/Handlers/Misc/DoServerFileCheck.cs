namespace MikuSB.GameServer.Server.CallGS.Handlers.Misc;

// The CN client asks the game server to verify client files (no param, fire and forget).
// The client has no s2c command with this name, so the generic "no handler" fallback
// (which echoes the request api name back as a script) makes the client Lua layer raise
// "s2c.Dispatch error. No command named: do_server_file_check". Accepting the request
// silently is enough for the client to continue.
[CallGSApi("do_server_file_check")]
public class DoServerFileCheck : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        return Task.FromResult(CallGSResult.NoResponse());
    }
}
