using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.GameServer.Game.FarmWar;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.FarmWar;

// FarmWar clear packet, routed through the settle wrapper: FarmWarActivity.Req_LevelSettlement
// (FarmWarActivity.lua:812-858) builds Reconnect.Send_SettleInfo("FarmWarLogic_LevelSettlement", cmd),
// so the packet lands on Chapter_DealLevelSettlement with { sCmd, tbParam }. The wrapper retries every
// 2s five times and then drops the connection, therefore the branch must always answer.
//
// cmd: { nID, nSeed, nTime, tbLog, H (defend health %), F (buffs), K (kills), P (plants),
//        U (upgrades), B (buffs bought), W (wave), I (item id), C (item uses), S (school) }
//
// The s2c callback (FarmWarActivity.lua:861-876) only reads tbNewPlant (planting animation) and sErr,
// so the reply body is irrelevant; the rewards come from farmwarout_level.json and are granted here.
public static class FarmWarSettlement
{
    private static readonly Logger Log = new(nameof(FarmWarSettlement));

    public static (JsonNode Payload, NtfSyncPlayer? Sync) HandleSettlement(
        PlayerInstance player,
        JsonNode? tbParam)
    {
        var param = tbParam as JsonObject ?? new JsonObject();

        var levelId = ReadUint(param["nID"]);
        var health = ReadUint(param["H"]);
        var wave = ReadUint(param["W"]);
        var seed = ReadUint(param["nSeed"]);

        if (!GameData.FarmWarLevelData.TryGetValue(levelId, out var level))
        {
            Log.Warn($"FarmWar settlement: unknown stage {levelId} for uid={player.Uid}.");
            return (new JsonObject(), null);
        }

        var sync = new NtfSyncPlayer();
        var act = FarmWarActivityService.CurrentAct(player);

        if (health == 0)
        {
            // Defense destroyed: the client already showed the fail flow, keep the stage uncleared.
            Log.Info($"FarmWar settlement: stage={levelId} failed (health 0), seed={seed} uid={player.Uid}.");
            return (new JsonObject(), null);
        }

        var first = FarmWarActivityService.RecordClear(player, levelId, health, wave, sync);

        if (first)
            player.RewardManager.GrantConfiguredRewardsAsync(level.Rewards, sync).GetAwaiter().GetResult();

        // F is the list of buff rows the run touched; the client reads them back from (169, id) and
        // treats 0/nil as "not owned", so a touched row is at least tier 1.
        if (param["F"] is JsonArray buffs)
        {
            foreach (var buff in buffs)
            {
                var (buffId, tier) = ReadPair(buff);
                if (buffId == 0)
                    continue;

                var attr = player.Attributes.Set(AttrIds.FarmWar.BuffGid, buffId, tier == 0 ? 1u : tier);
                player.Attributes.SyncTo(sync, attr);
            }
        }

        if (act != null)
            FarmWarActivityService.MarkQuestProgress(player, act, levelId, sync);

        Log.Info($"FarmWar settlement: stage={levelId} health={health} wave={wave} first={first} " +
                 $"seed={seed} uid={player.Uid}.");

        return (new JsonObject(), sync);
    }

    private static uint ReadUint(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<uint>(out var parsed))
            return parsed;

        return uint.TryParse(node?.ToString(), out var text) ? text : 0;
    }

    private static (uint Id, uint Value) ReadPair(JsonNode? node)
    {
        if (node is JsonArray pair && pair.Count >= 2)
            return (ReadUint(pair[0]), ReadUint(pair[1]));

        if (node is JsonObject row)
        {
            var id = ReadUint(row["nId"] ?? row["Id"] ?? row["id"]);
            var value = ReadUint(row["nLevel"] ?? row["nVal"] ?? row["Val"] ?? row["level"]);
            return (id, value);
        }

        return (ReadUint(node), 0);
    }
}
