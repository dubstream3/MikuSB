namespace MikuSB.GameServer.Server.CallGS.Handlers.Misc;

// Client notifies the server of its language setting. No response required.
// param: {nType, sLan, sEx}
[CallGSApi("LanguageChange")]
public class LanguageChange : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
        => Task.FromResult(CallGSResult.NoResponse());
}
