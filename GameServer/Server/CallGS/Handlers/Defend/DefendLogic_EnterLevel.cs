using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Defend;

// Client side origin: DefendLogic request path (Challenge/Defend/Defend.lua:740-751) sends
// {nID, nDiff, nTeamID, tbLog}; the callback (Defend.lua:754-760) reads tbRet.nSeed, stores it as
// DefendLogic.nSeed and then calls Launch.Response('DefendLogic_EnterLevel') to start the fight, so
// the seed is the only field the server has to supply.
// Request: { "nID": <levelId>, "nDiff": <uint>, "nTeamID": <uint>, "tbLog": {...} }
// Response: { "nSeed": <uint> }
[CallGSApi("DefendLogic_EnterLevel")]
public class DefendLogic_EnterLevel : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        // Any value works for the client: it only feeds the wave/drop randomisation.
        var seed = (uint)Random.Shared.NextInt64(1, uint.MaxValue);
        Logger.GetByClassName().Info($"Entering defend level with seed {seed} (param='{param}').");
        return Task.FromResult(CallGSResult.Ok($"{{\"nSeed\":{seed}}}"));
    }
}
