using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.Rogue;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue;

// Rogue_LevelSettlement arrives through Reconnect.Send_SettleInfo (Rogue.lua:998), so the client
// retries every 2 seconds and calls BreakConnect() after six misses (Misc/Reconnect.lua:160-182).
// The inner payload is re-encoded and dispatched to the s2c handler at Rogue.lua:1006, which reads
// type(tbParam[1]) == "table" and then tbParam[1].Awards -- an object reply would silently skip the
// reward branch, and an sError would end the whole run (ExpirationExit).
public static class RogueSettlement
{
    private static readonly Logger Log = new(nameof(RogueSettlement));

    public static async Task<(JsonNode Payload, NtfSyncPlayer? Sync)> HandleSettlementAsync(
        PlayerInstance player,
        JsonNode? tbParam)
    {
        var sync = new NtfSyncPlayer();
        var nodeId = ReadUInt(tbParam, "nID");
        var state = ReadUInt(tbParam, "nState");
        var levelId = ReadUInt(tbParam, "nLevelID");

        if (state == 0)
            state = 2;

        if (nodeId == 0)
        {
            Log.Warn($"Rogue settlement without node id for uid={player.Uid}.");
            return (new JsonArray { new JsonObject { ["Awards"] = new JsonArray() } }, sync);
        }

        RogueService.SetNodeState(player, nodeId, state, sync);
        RogueService.ApplyHealth(player, tbParam?["tbHP"], sync);

        var awards = new JsonArray();
        if (state == 2)
        {
            var level = levelId != 0 ? RogueService.FindLevel(levelId) : null;
            var node = RogueService.FindNode(RogueService.CurrentMap(player), nodeId);
            if (node != null)
            {
                if (node.Node == 1 && node.LevelId != 0 && level == null)
                    level = RogueService.FindLevel(node.LevelId);
            }

            if (level != null && level.AwardRows.Count > 0)
            {
                IReadOnlyList<IReadOnlyList<uint>> rows = level.AwardRows
                    .Select(row => (IReadOnlyList<uint>)row)
                    .ToList();
                var granted = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);
                foreach (var row in granted)
                    awards.Add(row?.DeepClone());
            }
        }

        DatabaseHelper.SaveDatabaseType(player.Data);

        var payload = new JsonArray
        {
            new JsonObject { ["Awards"] = awards }
        };

        Log.Info($"Rogue settlement: uid={player.Uid}, node={nodeId}, state={state}, " +
                 $"level={levelId}, awards={awards.Count}.");
        return (payload, sync);
    }

    private static uint ReadUInt(JsonNode? node, string key)
    {
        if (node?[key] is not JsonValue value)
            return 0;

        if (value.TryGetValue<uint>(out var number))
            return number;
        if (value.TryGetValue<int>(out var signed) && signed > 0)
            return (uint)signed;
        if (value.TryGetValue<string>(out var text) && uint.TryParse(text, out var parsed))
            return parsed;

        return 0;
    }
}
