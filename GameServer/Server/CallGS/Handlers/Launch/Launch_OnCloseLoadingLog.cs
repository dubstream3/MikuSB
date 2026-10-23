namespace MikuSB.GameServer.Server.CallGS.Handlers.Launch;

// Fire-and-forget telemetry the client sends after a loading screen closes
// (Launch/Launch.lua:1068). The client registers no s2c callback for it, so answering with
// the router's fallback echo makes the client throw "No command named: Launch_OnCloseLoadingLog".
[CallGSApi("Launch_OnCloseLoadingLog")]
public class Launch_OnCloseLoadingLog : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        return Task.FromResult(CallGSResult.NoResponse());
    }
}
