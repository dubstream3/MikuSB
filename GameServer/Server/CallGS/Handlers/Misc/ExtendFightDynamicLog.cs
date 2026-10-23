namespace MikuSB.GameServer.Server.CallGS.Handlers.Misc;

[CallGSApi("ExtendFightDynamicLog")]
public class ExtendFightDynamicLog : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        return Task.FromResult(CallGSResult.NoResponse());
    }
}
