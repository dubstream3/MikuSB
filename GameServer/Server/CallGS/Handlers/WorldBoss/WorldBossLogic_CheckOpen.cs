using System.Text.Json.Nodes;
using MikuSB.GameServer.Game.WorldBoss;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.WorldBoss;

// 世界BOSS open check (Challenge/WorldBoss/WorldBoss.lua:605-632). The button shows a connection
// overlay first, so an unanswered request freezes it. nOpen > 0 makes the client read (179,0)/(179,1)
// and look the act up in its own table; the pair is seeded at login by WorldBossService.Seed, and a
// zero pair would show "ui.TxtNotOpen" even with nOpen = 1.
[CallGSApi("WorldBossLogic_CheckOpen")]
public class WorldBossLogic_CheckOpen : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player;
        var act = WorldBossService.SelectAct(DateTime.Now);

        var isOpen = player != null && act != null;

        Logger.GetByClassName().Info(
            $"WorldBoss CheckOpen -> {isOpen} (act={act?.ActivityId}/{act?.ActId}) for uid={player?.Uid ?? 0}.");

        return Task.FromResult(CallGSResult.Ok(new JsonObject
        {
            ["nOpen"] = isOpen ? 1 : 0
        }.ToJsonString()));
    }
}
