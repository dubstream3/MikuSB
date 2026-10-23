using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.GameServer.Game.WorldBoss;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.WorldBoss;

// 世界BOSS "I saw my score" ack (Challenge/WorldBoss/WorldBoss.lua:757-777). The client raises a
// connection overlay, sends { nID, nActID, nLevelID, nSeed, bRecord } and only drops it on the reply,
// reading tbParam.bRecord to know the score was accepted. Marking the attempt block recorded here is
// harmless when the settlement already did the same.
[CallGSApi("WorldBossLogic_Record")]
public class WorldBossLogic_Record : CallGSHandler<WorldBossRecordParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, WorldBossRecordParam request)
    {
        var player = context.Connection.Player;
        var act = WorldBossService.FindAct(request.ActivityId, request.ActId) ??
                  WorldBossService.SelectAct(DateTime.Now);

        if (player != null && act != null)
            WorldBossService.MarkRecorded(player, act);

        Logger.GetByClassName().Info(
            $"WorldBoss Record act={request.ActivityId}/{request.ActId} stage={request.LevelId} " +
            $"bRecord={request.Record} for uid={player?.Uid ?? 0}.");

        return Task.FromResult(CallGSResult.Ok(new JsonObject
        {
            ["bRecord"] = true
        }.ToJsonString()));
    }
}

public sealed class WorldBossRecordParam
{
    [JsonPropertyName("nID")] public uint ActivityId { get; set; }
    [JsonPropertyName("nActID")] public uint ActId { get; set; }
    [JsonPropertyName("nLevelID")] public uint LevelId { get; set; }
    [JsonPropertyName("nSeed")] public uint Seed { get; set; }
    [JsonPropertyName("bRecord")] public bool Record { get; set; }
}
