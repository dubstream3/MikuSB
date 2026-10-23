using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

/// <summary>
/// Settlement for 多诺-巅峰对决 arena challenge runs.
///
/// The client finishes a run with DLC/DonoDuel/DonoDuelArena.lua:637
/// DonoDuelArena:Req_LevelSettlement, which goes out over the shared reconnect settlement channel
/// (Misc/Reconnect.lua:144 Reconnect.Send_SettleInfo) as
/// Chapter_DealLevelSettlement { sCmd = "DonoDuelArena_LevelSettlement", tbParam = { nID, nArenaId,
/// nSeed, nStar, tbLog, tbKill } }, so the answer comes back through
/// Launch.Response(DonoDuelArena.REQ_LEVEL_SETTLEMENT) and
/// Launch/Classes/DonoDuelArenaLogic.lua:OnSettlement opens the DonoArenaSettlement panel.
///
/// The panel takes its rows from the client's own copy of dlc/DonoDuel/donoduel_arena_battle.json
/// (DonoDuelArena:GetArenaBattleConfig(levelId).tbReward) and only when GetPassTime() == 1, i.e. on
/// the first clear, so the server has to bump (Launch.GPASSID = 22, levelId) and grant those rows.
/// </summary>
internal static class DonoDuelArenaSettlement
{
    private static readonly Logger Logger = new("DonoDuelArena");

    private const uint StarGroupId = AttrIds.Launch.LevelStateGid;  // Launch.GID = 21, bits 0-7 stars
    private const uint PassGroupId = AttrIds.Launch.LevelPassGid;   // Launch.GPASSID = 22, clear count
    private const uint StarMask = 0xFFu;

    public static async ValueTask<(JsonNode Payload, NtfSyncPlayer? Sync)> HandleSettlementAsync(
        PlayerInstance player,
        JsonNode? tbParam)
    {
        var levelId = ReadUint(tbParam, "nID");
        var starMask = ReadUint(tbParam, "nStar");

        if (levelId == 0 || !GameData.DonoDuelArenaBattleData.TryGetValue(levelId, out var battleConfig))
        {
            Logger.Warn($"Donoduel arena settlement for unknown level {levelId}; echoing the request back.");
            return (tbParam?.DeepClone() ?? new JsonObject(), null);
        }

        var sync = new NtfSyncPlayer();
        var passAttr = player.Attributes.GetOrCreate(PassGroupId, levelId);
        var isFirstClear = passAttr.Val == 0;

        var rewards = await player.RewardManager.GrantConfiguredRewardsAsync(
            DlcRuntimeOverrides.ReadRewardRows(battleConfig.Reward), sync);

        passAttr.Val += 1;
        player.Attributes.SyncTo(sync, passAttr);

        var starAttr = player.Attributes.GetOrCreate(StarGroupId, levelId);
        starAttr.Val = (starAttr.Val & ~StarMask) | (starMask & StarMask);
        player.Attributes.SyncTo(sync, starAttr);

        // The story missions gate the whole Donoduel season: donoduel_function.json row 101 (the
        // catch/rogue function the player actually plays) opens on condition 36 = "mission 102
        // rewarded", and 102 is objective 1 with Param {5505}, i.e. "clear arena level 5505"
        // (Condition/Condition.lua:607-615 + :43-44). Nothing in the client can report that
        // objective, so the clear we just recorded has to push it forward here.
        var advanced = DonoDuelTaskState.AdvanceArenaClearMissions(player, levelId, sync);

        DatabaseHelper.SaveDatabaseType(player.Data);
        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
        DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);

        Logger.Info(
            $"Donoduel arena level {levelId} settled (firstClear={isFirstClear}, stars={starMask & StarMask}, " +
            $"{rewards.Count} reward row(s), {advanced} mission(s) advanced) for uid={player.Uid}: {rewards.ToJsonString()}");

        return (rewards, sync);
    }

    private static uint ReadUint(JsonNode? node, string key)
    {
        if (node is not JsonObject obj)
            return 0;

        var raw = obj[key]?.ToString().Trim().Trim('[', ']', '"');
        return uint.TryParse(raw, out var value) ? value : 0;
    }
}
