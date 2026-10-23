namespace MikuSB.GameServer.Server.CallGS.Handlers.Online;

// Client side origin: Online.CheckPopInfo / Online.CheckNoPopInfo (Launch/Online/Online.lua:3546-3564)
// mark the "first popup already shown" flag after showing the activity help image. The client keeps
// that flag locally (Online.GetFirstPop), there is no s2c callback for this api, and the router
// fallback would echo the api name back as an unknown s2c command.
[CallGSApi("Online_FirstPop")]
public class Online_FirstPop : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        return Task.FromResult(CallGSResult.NoResponse());
    }
}
