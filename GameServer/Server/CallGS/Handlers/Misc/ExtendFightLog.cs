namespace MikuSB.GameServer.Server.CallGS.Handlers.Misc;

[CallGSApi("ExtendFightLog")]
public class ExtendFightLog : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        return Task.FromResult(CallGSResult.NoResponse());
    }
}
