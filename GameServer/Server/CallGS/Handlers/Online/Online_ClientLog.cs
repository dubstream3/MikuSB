namespace MikuSB.GameServer.Server.CallGS.Handlers.Online;

// Fire-and-forget client telemetry: the client uploads performance / feature logs with
// me:CallGS("Online_ClientLog", json.encode({...})) (Launch/Online/Online.lua:3042 level
// performance, AR/ArLogic.lua:73,87, Chat/Chat.lua:345,348, Activity/GiftboxSuggest.lua:540,
// UMG/riki/Widgets/umg_riki_bgmset.lua:157). It registers no s2c callback, so the router's
// fallback echo makes the client throw "No command named: Online_ClientLog" and pop ShowLuaError.
[CallGSApi("Online_ClientLog")]
public class Online_ClientLog : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        return Task.FromResult(CallGSResult.NoResponse());
    }
}
