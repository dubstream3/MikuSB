namespace MikuSB.GameServer.Server.CallGS.Handlers.Online;

// The CN client (3.6) asks for the assault ops battle pass state right after login.
// MikuSB has no online/assault-ops schedule table for it yet, so answer with an empty
// success payload instead of letting the router fall back to the ui.TxtNotOpen error.
[CallGSApi("OnlineAssaultOpsBattlePass_ClientRefresh")]
public class OnlineAssaultOpsBattlePass_ClientRefresh : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        return Task.FromResult(CallGSResult.Ok("{}"));
    }
}
