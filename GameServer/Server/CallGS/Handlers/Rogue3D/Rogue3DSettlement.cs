using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;

/// <summary>
/// 悖论迷宫 (Rogue3D) level settlements. Both the normal run and the season one settle over the shared
/// reconnect channel (Misc/Reconnect.lua Reconnect.Send_SettleInfo) as Chapter_DealLevelSettlement with
/// sCmd = "Rogue3D_LevelSettlement" (DLC/Rogue3D/Rogue3DLogic.lua:1867) or
/// sCmd = "Rogue3D_SeasonLevelSettlement" (:1650), so they are handled from
/// Chapter_DealLevelSettlement.BuildSettlementPayloadAsync instead of as their own CallGS APIs. A room
/// inside a run is reported separately through Rogue3D_RoomSettlement / Rogue3D_SeasonRoomSettlement.
///
/// Client expectations (Rogue3DLaunchLogic.lua:26-45 -> Launch.Response -> UI "RogueSettement"):
///   * the payload is the settlement answer itself. umg_rogue_settlement.lua:146 reads tbdata.nDiffId for
///     the difficulty name/icon, :158 iterates tbdata.tbLevelAward and :171 tbdata.tbMonsterDropRiki, so
///     the run data is echoed back and both lists are always sent - an unanswered settlement leaves the
///     panel without a difficulty name, and after five failed retries (Reconnect.lua:166) the client
///     force-disconnects.
///   * the fight statistics (nodeId / fight_count / danger_value / infinity_point / tbBuffs) are read from
///     the client-side cache Rogue3DLogic.tbCacheSettlementData / tbCacheSeasonSettlementData (:125-130),
///     not from this payload.
///
/// Difficulty progression lives in the player attributes (GroupId=124):
///   Rogue3DLogic.GetLevelPassNum(nDiff) = GetAttribute(124, 20 + nDiff)   (Rogue3DLogic.lua:953-957,
///   LevelPassStart = 20 / LevelPassEnd = 30)
/// and dlc/rogue3d/server_01_difficult.json gates the later difficulties on it (DifficultID 3 ->
/// UnlockCondition 2, 4 -> 3, and the season difficulty 100 -> 2; :1 and :2 need no condition). Clearing a
/// difficulty therefore has to add one to (124, 20 + nDiffId) - that write was missing, which is why the
/// client's uw_rogue_difficulty.lua:71-79 kept every later difficulty locked. Only the reserved window
/// 21..30 may be written: from 100 up the same group holds the science tree levels
/// (server_02_science.json ids 101+, read through Rogue3DLogic.GetTechLevel at :960-963), so a settlement
/// whose nDiffId falls outside the window is echoed without a counter instead of corrupting a science
/// node. That is exactly what the season run does: mode 1 of server_11_gamemode.json lists the single
/// difficulty 100 (Rogue3DLogic.GetSeasonDiff -> :849-851), and row 100 of server_01_difficult.json
/// carries UnlockCondition 2 - the season is gated on the *normal* difficulty 2 clear counter, which the
/// ordinary Rogue3D_LevelSettlement branch writes. No row ever uses UnlockCondition 100, so nothing reads
/// a clear counter at sid 120.
/// </summary>
internal static class Rogue3DSettlement
{
    private static readonly Logger Logger = new("Rogue3D");

    /// <summary>Rogue3DLogic.LevelPassStart: difficulty n is counted at sid (LevelPassStart + n).</summary>
    private const uint LevelPassStartSid = AttrIds.Rogue3D.LevelPassStartSid;

    /// <summary>Number of difficulty counter slots the client reserves (LevelPassStart .. LevelPassEnd).</summary>
    private const uint LevelPassSlots = AttrIds.Rogue3D.LevelPassEndSid - LevelPassStartSid;

    public static ValueTask<(JsonNode Payload, NtfSyncPlayer? Sync)> HandleLevelSettlementAsync(
        PlayerInstance player,
        JsonNode? tbParam)
    {
        return SettleAsync(player, tbParam, season: false);
    }

    public static ValueTask<(JsonNode Payload, NtfSyncPlayer? Sync)> HandleSeasonLevelSettlementAsync(
        PlayerInstance player,
        JsonNode? tbParam)
    {
        return SettleAsync(player, tbParam, season: true);
    }

    private static ValueTask<(JsonNode Payload, NtfSyncPlayer? Sync)> SettleAsync(
        PlayerInstance player,
        JsonNode? tbParam,
        bool season)
    {
        var diffId = ReadUint(tbParam, "nDiffId");

        // tbFightData is only attached when the whole difficulty was finished:
        // Rogue3DLaunchLogic.lua:27 sets bEndDiff = nResult == ELevelFinishResult.Success and
        // Rogue3DLogic.lua:1861-1865 sends tbFightData when bEndDiff, a failure/manual exit sends tbLog.
        var cleared = tbParam is JsonObject source &&
                      source.TryGetPropertyValue("tbFightData", out var fightData) &&
                      fightData is not null;

        // Echo the run back so the settlement panel finds nDiffId, and always carry the (empty for now)
        // award lists umg_rogue_settlement.lua expects to iterate.
        var payload = tbParam?.DeepClone() as JsonObject ?? new JsonObject();
        payload["tbLevelAward"] = new JsonArray();
        payload["tbMonsterDropRiki"] = new JsonArray();

        var mode = season ? "season" : "normal";

        if (!cleared || diffId == 0 || diffId > LevelPassSlots)
        {
            Logger.Info(
                $"Rogue3D {mode} settlement for uid={player.Uid} diffId={diffId} cleared={cleared}: " +
                "no difficulty clear counter written.");
            return ValueTask.FromResult<(JsonNode, NtfSyncPlayer?)>((payload, null));
        }

        var attr = player.Attributes.Add(AttrIds.Rogue3D.Gid, LevelPassStartSid + diffId, 1);
        var sync = new NtfSyncPlayer();
        player.Attributes.SyncTo(sync, attr);
        DatabaseHelper.SaveDatabaseType(player.Data);

        Logger.Info(
            $"Rogue3D {mode} difficulty {diffId} cleared for uid={player.Uid}; " +
            $"the clear counter (gid={AttrIds.Rogue3D.Gid} sid={LevelPassStartSid + diffId}) is now {attr.Val}.");

        return ValueTask.FromResult<(JsonNode, NtfSyncPlayer?)>((payload, sync));
    }

    private static uint ReadUint(JsonNode? node, string key)
    {
        if (node is not JsonObject obj)
            return 0;

        var raw = obj[key]?.ToString().Trim().Trim('[', ']', '"');
        return uint.TryParse(raw, out var value) ? value : 0;
    }
}
