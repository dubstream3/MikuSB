using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.GameServer.Game.WorldBoss;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.WorldBoss;

// 世界BOSS progress task refresh (Challenge/WorldBoss/WorldBoss.lua:800-834). The client only sends
// this when both acts are open at once (it needs two global counter names), and it has no s2c
// callback, so the reply is a plain empty object. Quest progress is pushed by the settlement instead.
[CallGSApi("WorldBossLogic_UpdateProgressTask")]
public class WorldBossLogic_UpdateProgressTask : CallGSHandler<WorldBossUpdateProgressParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, WorldBossUpdateProgressParam request)
    {
        Logger.GetByClassName().Debug(
            $"WorldBoss UpdateProgressTask act={request.ActivityId} for uid={context.Connection.Player?.Uid ?? 0}.");
        return Task.FromResult(CallGSResult.Ok("{}"));
    }
}

public sealed class WorldBossUpdateProgressParam
{
    [JsonPropertyName("nID")] public uint ActivityId { get; set; }
}

// 世界BOSS mail reward check (Challenge/WorldBoss/WorldBoss.lua:837-853). The client never registers
// an s2c callback for this api, so ANY reply hits Misc/S2C.lua:29-35 "No command named" and throws;
// answering nothing is the contract.
[CallGSApi("WorldBossLogic_CheckReward")]
public class WorldBossLogic_CheckReward : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        Logger.GetByClassName().Debug($"WorldBoss CheckReward (no response) for uid={context.Connection.Player?.Uid ?? 0}.");
        return Task.FromResult(CallGSResult.NoResponse());
    }
}
