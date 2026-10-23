namespace MikuSB.GameServer.Server.CallGS.Handlers.Online;

/// <summary>
/// 海姆优化作战 (Rush) polls this every 30 s to re-sync the online activity windows
/// (Launch/Online/Online.lua:3879). The matching s2c callback on the client is an empty
/// function, so the answer only has to exist: without a handler the client gets the
/// no-handler fallback (the api name echoed back with sErr) and keeps polling.
/// </summary>
[CallGSApi("OnlineActivity_Request")]
public class OnlineActivity_Request : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        return Task.FromResult(CallGSResult.Ok("{}"));
    }
}
