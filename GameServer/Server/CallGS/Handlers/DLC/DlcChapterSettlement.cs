using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

/// <summary>
/// Settlement for DLC story chapters (the levels reached from Dlc35Main -> 前往主线). These levels
/// live in dlc/dlc1/chapter/level.json and are not part of QuestManager's Chapter/Daily/Role model,
/// so the reward handling is done here directly on top of RewardManager.
/// Client contract: Misc/Reconnect.lua:144 sends Chapter_DealLevelSettlement {sCmd, tbParam} and
/// Misc/Reconnect.lua:186 dispatches the answer back to s2c 'DLC_Chapter_LevelSettlement', which
/// DLC_Chapter.lua:572 forwards to Launch.Response. Launch/Classes/DlcChapterLogic.lua:30 stores the
/// payload in Launch.tbAward, so the payload must be a list of [G,D,P,L,Count] row lists.
/// </summary>
internal static class DlcChapterSettlement
{
    private static readonly Logger Logger = new("DLC_Chapter");

    private const uint StarGroupId = AttrIds.Launch.LevelStateGid;  // Launch.GID = 21, bits 0-7 stars
    private const uint PassGroupId = AttrIds.Launch.LevelPassGid;   // Launch.GPASSID = 22, clear count
    private const uint StarMask = 0xFFu;

    public static async ValueTask<(JsonNode Payload, NtfSyncPlayer? Sync)> HandleSettlementAsync(
        PlayerInstance player,
        JsonNode? tbParam)
    {
        var levelId = ReadUint(tbParam, "nID");
        var starMask = ReadUint(tbParam, "nStar");
        var seed = ReadUint(tbParam, "nSeed");

        if (levelId == 0 || !GameData.DlcChapterLevelData.TryGetValue(levelId, out var levelConfig))
        {
            Logger.Warn($"DLC chapter settlement for unknown level {levelId}; echoing the request back.");
            return (tbParam?.DeepClone() ?? new JsonObject(), null);
        }

        var sync = new NtfSyncPlayer();
        var passAttr = player.Attributes.GetOrCreate(PassGroupId, levelId);
        var isFirstClear = passAttr.Val == 0;

        // GrantLevelRewardsAsync handles FirstDropID (first clear only), BaseDropID (repeat only)
        // and RandomDropID, then the player/role experience, exactly like a normal chapter level.
        var rewards = await player.RewardManager.GrantLevelRewardsAsync(levelConfig, isFirstClear, seed, sync);

        passAttr.Val += 1;
        player.Attributes.SyncTo(sync, passAttr);

        var starAttr = player.Attributes.GetOrCreate(StarGroupId, levelId);
        starAttr.Val = (starAttr.Val & ~StarMask) | (starMask & StarMask);
        player.Attributes.SyncTo(sync, starAttr);

        DatabaseHelper.SaveDatabaseType(player.Data);
        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
        DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);

        Logger.Info(
            $"DLC chapter level {levelId} settled (firstClear={isFirstClear}, stars={starMask & StarMask}, " +
            $"{rewards.Count} reward group(s)) for uid={player.Uid}: {rewards.ToJsonString()}");

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
