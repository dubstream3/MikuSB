namespace MikuSB.GameServer.Server.CallGS.Handlers.Online;

// 联机-训练关卡 关卡失败上报.
//
// Client origin: OnlineTraining:Req_LevelFail (Launch/Online/OnlineTraining.lua:185-196) only calls
// me:CallGS(...) and registers no s2c callback for the API, so an answer would be dispatched into a
// command that does not exist. Answering nothing keeps the fail flow as the shipped server has it
// while silencing the router's "No handler for CallGS API" error.
[CallGSApi("OnlineTraining_LevelFail")]
public class OnlineTraining_LevelFail : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        return Task.FromResult(CallGSResult.NoResponse());
    }
}
