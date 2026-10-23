using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Tower;

/// <summary>
/// DLC 爬塔 (Runaway / climb tower) level settlements. They go out over the shared reconnect channel
/// (Misc/Reconnect.lua Reconnect.Send_SettleInfo) as Chapter_DealLevelSettlement with
/// sCmd = "ClimbTower_LevelSettlement" - the client builds the payload in
/// Launch/Classes/ClimbTowerBattleLogic.lua:41 and DLC/ClimbTower/ClimbTower.lua:632 forwards it, so the
/// direct CallGS API is never hit by the live client.
///
/// This is a different mode from 逆境之塔 (Challenge/Tower, sCmd = "TowerLevel_LevelSettlement" and the
/// ClimbTowerLogic_* handlers): it shares the tower attribute groups but its levels are keyed by *battle*
/// id (climb_tower/climbtower_battle.json, 1..24), not by level id (8861..). Both writes below therefore
/// use nId as it arrives, and the same slots the generic chapter/DLC path uses:
///   AttrIds.Launch.LevelPassGid  = 22, GetAttribute(22, nId)      - clear counter,
///     ClimbTower.lua's IsUnlock/IsPass reads it through Launch.GPASSID (Launch/Launch.lua:20).
///   AttrIds.Launch.LevelStateGid = 21, GetAttribute(21, nId)      - star bits 0-7 (Launch.GID,
///     Launch/Launch.lua:18); the mask mirrors DlcChapterSettlement.
///
/// Client reply contract (ClimbTowerBattleLogic.lua:50-58 OnSettleLevelRsp) reads exactly two fields:
/// <c>tbAward</c> (a list of [g,d,p,l,count] rows - empty is fine here, DLC climb tower pays out from the
/// cumulative star milestones in attribute group 174 via ClimbTowerLogic_GetReward instead) and
/// <c>sErr</c>. Without this branch the settlement fell through to the echo fallback, which answered
/// without sErr but never wrote the pass counter or the stars, so cleared battles stayed locked.
/// </summary>
internal static class ClimbTowerSettlement
{
    private static readonly Logger Logger = new("ClimbTower");

    /// <summary>Launch.GID: star bits, low 8 bits of the per-level slot.</summary>
    private const uint StarGroupId = AttrIds.Launch.LevelStateGid;

    /// <summary>Launch.GPASSID: clear counter, one slot per battle id.</summary>
    private const uint PassGroupId = AttrIds.Launch.LevelPassGid;

    private const uint StarMask = 0xFFu;

    public static ValueTask<(JsonNode Payload, NtfSyncPlayer? Sync)> HandleSettlementAsync(
        PlayerInstance player,
        JsonNode? tbParam)
    {
        var battleId = ReadUint(tbParam, "nId");
        var starMask = ReadUint(tbParam, "nStar") & StarMask;
        var success = ReadBool(tbParam, "bSuccess");
        var sync = new NtfSyncPlayer();

        if (battleId == 0)
            return ValueTask.FromResult<(JsonNode, NtfSyncPlayer?)>(
                (new JsonObject { ["sErr"] = "error.BadParam" }, sync));

        if (!success)
        {
            Logger.Info(
                $"Climb tower battle {battleId} settled for uid={player.Uid} without a clear; no progress " +
                "written.");
            return ValueTask.FromResult<(JsonNode, NtfSyncPlayer?)>((new JsonObject(), null));
        }

        var passAttr = player.Attributes.Add(PassGroupId, battleId, 1);
        player.Attributes.SyncTo(sync, passAttr);

        var starAttr = player.Attributes.GetOrCreate(StarGroupId, battleId);
        var mergedStars = (starAttr.Val & ~StarMask) | starMask;
        if (starAttr.Val != mergedStars)
        {
            starAttr.Val = mergedStars;
            player.Attributes.SyncTo(sync, starAttr);
        }

        DatabaseHelper.SaveDatabaseType(player.Data);

        Logger.Info(
            $"Climb tower battle {battleId} cleared for uid={player.Uid}: pass counter " +
            $"(gid={PassGroupId} sid={battleId}) is now {passAttr.Val}, stars " +
            $"(gid={StarGroupId} sid={battleId}) are now 0x{mergedStars:X2}.");

        return ValueTask.FromResult<(JsonNode, NtfSyncPlayer?)>((new JsonObject(), sync));
    }

    private static uint ReadUint(JsonNode? node, string key)
    {
        if (node is not JsonObject obj)
            return 0;

        var raw = obj[key]?.ToString().Trim().Trim('[', ']', '"');
        return uint.TryParse(raw, out var value) ? value : 0;
    }

    private static bool ReadBool(JsonNode? node, string key)
    {
        if (node is not JsonObject obj)
            return false;

        var raw = obj[key]?.ToString().Trim();
        return raw is "true" or "True" or "1";
    }
}
