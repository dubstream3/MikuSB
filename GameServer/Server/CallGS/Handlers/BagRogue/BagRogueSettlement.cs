using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.BagRogue;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BagRogue;

// BagRogue_LevelSettlement arrives wrapped by Reconnect.Send_SettleInfo, i.e. as
// Chapter_DealLevelSettlement { sCmd: "BagRogue_LevelSettlement", tbParam: cmd }. The client resends every
// two seconds and drops the connection after five silent tries (Misc/Reconnect.lua:160-182), so this must
// answer quickly and be idempotent per (node, seed).
//
// Two different replies live behind the same cmd:
//   * ordinary node: Launch.tbAward = {tbAward} -> the Success panel iterates it, so the key must exist;
//   * ending node:   the Success panel ignores tbAward and the next panel reads 61/5 instead
//                    (umg_backpack_settlement2.lua:13), so {} is the correct body there.
public static class BagRogueSettlement
{
    private static readonly Logger Log = new(nameof(BagRogueSettlement));

    public static async Task<(JsonNode Payload, NtfSyncPlayer? Sync)> HandleSettlementAsync(
        PlayerInstance player, JsonNode? tbParam)
    {
        var sync = new NtfSyncPlayer();
        var stageId = BagRogueService.ReadUint(tbParam?["nID"]);
        var score = BagRogueService.ReadUint(tbParam?["nScore"]);
        var time = BagRogueService.ReadUint(tbParam?["nTime"]);
        var node = BagRogueService.CurrentNode(player);

        if (node == null)
        {
            Log.Warn($"BagRogue settlement for stage {stageId} without a walked node (uid={player.Uid}).");
            return (new JsonObject { ["tbAward"] = new JsonArray() }, sync);
        }

        if (node.IsEnding)
        {
            BagRogueService.RecordEnding(player, BagRogueService.EndingTypeFor(node), score, time, sync);
            BagRogueService.FinishRoute(player, sync);
            BagRogueService.RecordClear(player, sync);
            DatabaseHelper.SaveDatabaseType(player.Data);

            Log.Info($"BagRogue ending {BagRogueService.EndingTypeFor(node)} at map {node.MapId}, " +
                     $"score={score}, time={time} for uid={player.Uid}.");
            return (new JsonObject(), sync);
        }

        var awards = new JsonArray();
        foreach (var row in BagRogueService.NodeRewardRows(node))
        {
            var item = new JsonArray();
            foreach (var cell in row)
                item.Add(cell);
            awards.Add(item);
        }

        // The walked node stays on (142,6) until NodeFinish closes the run, so the award is accumulated for
        // the settlement panel instead of being paid twice if the client retries the same stage.
        var rows = new List<IReadOnlyList<uint>>();
        foreach (var row in BagRogueService.NodeRewardRows(node))
            rows.Add(row);

        if (rows.Count > 0)
            await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);

        BagRogueService.AccumulateRewards(player, rows, sync);
        DatabaseHelper.SaveDatabaseType(player.Data);
        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);

        Log.Info($"BagRogue stage {stageId} cleared at node {node.NodeId} (map {node.MapId}), " +
                 $"{rows.Count} reward row(s) for uid={player.Uid}.");

        return (new JsonObject { ["tbAward"] = awards }, sync);
    }
}
