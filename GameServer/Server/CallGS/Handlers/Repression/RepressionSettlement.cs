using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Repression;

/// <summary>
/// 压抑区 / 镇压 (Repression) level settlements. They go out over the shared reconnect channel
/// (Misc/Reconnect.lua Reconnect.Send_SettleInfo) as Chapter_DealLevelSettlement with
/// sCmd = "Repression_Chapter_LevelSettlement" (Launch/Repression/Repression_Chapter.lua:1008), so they
/// are handled from Chapter_DealLevelSettlement.BuildSettlementPayloadAsync instead of as their own
/// CallGS API.
///
/// Repression cannot reuse the generic chapter settlement path: its levels are loaded from
/// dlc/Repression/level.txt (Repression_Level.tbLevel) and have no ChapterLevelData row, so
/// QuestManager.SettleLevelAsync cannot resolve them.
///
/// Client expectations (Launch/Classes/RepressionLogic.lua:27-61): a settlement is requested only for a
/// successful run (:29 nResult == ELevelFinishResult.Success; failures and manual exits go to
/// Req_LevelFail / sCmd "Repression_Chapter_LevelFail"), and the answer is assigned to Launch.tbAward
/// (:44) before the Success/Settlement panel opens. Repression rewards are collected through separate
/// star-award requests, so an empty answer is enough - but it must arrive, because Reconnect.lua:160-166
/// force-disconnects the client after five unanswered retries.
///
/// Difficulty progression lives in the shared level attributes:
///   Repression_Level:GetPassTime() = GetAttribute(Launch.GPASSID = 22, levelId) (Repression_Level.lua:52-54)
///   -> IsPass() &gt; 0 (:57-59) -> Repression_Chapter.IsChapterPass (:487-497) ->
///   GetMaxUnlockDiff (:499-513), which returns the first difficulty that still has an uncleared
///   chapter, i.e. the highest difficulty the UI opens. Nothing ever wrote that counter for Repression
///   (QuestManager only knows the chapter/daily/role/tower style tables), so everything past the first
///   difficulty stayed locked. The same slot is what the star/flag attributes of Repression levels do
///   not use: the per-level flag reader Repression_Level.lua:41 has no caller, and the star display is
///   score-based (Repression_Chapter.GetChapterScoreInfo).
/// </summary>
internal static class RepressionSettlement
{
    private static readonly Logger Logger = new("Repression");

    public static ValueTask<(JsonNode Payload, NtfSyncPlayer? Sync)> HandleLevelSettlementAsync(
        PlayerInstance player,
        JsonNode? tbParam)
    {
        var levelId = ReadUint(tbParam, "nID");
        var chapterId = ReadUint(tbParam, "nChapterID");
        var star = ReadUint(tbParam, "nStar");
        var sync = new NtfSyncPlayer();

        if (levelId == 0)
        {
            Logger.Info($"Repression settlement for uid={player.Uid} carried no level id; nothing written.");
            return ValueTask.FromResult<(JsonNode, NtfSyncPlayer?)>((new JsonArray(), null));
        }

        var attr = player.Attributes.Add(AttrIds.Launch.LevelPassGid, levelId, 1);
        player.Attributes.SyncTo(sync, attr);
        DatabaseHelper.SaveDatabaseType(player.Data);

        Logger.Info(
            $"Repression level {levelId} cleared for uid={player.Uid} (chapter={chapterId}, star={star}); " +
            $"the clear counter (gid={AttrIds.Launch.LevelPassGid} sid={levelId}) is now {attr.Val}.");

        // RepressionLogic.lua:44 assigns this payload to Launch.tbAward and umg_success.lua:158 ipairs
        // over it, so an empty award list is the honest "the star awards come from their own request"
        // answer.
        return ValueTask.FromResult<(JsonNode, NtfSyncPlayer?)>((new JsonArray(), sync));
    }

    private static uint ReadUint(JsonNode? node, string key)
    {
        if (node is not JsonObject obj)
            return 0;

        var raw = obj[key]?.ToString().Trim().Trim('[', ']', '"');
        return uint.TryParse(raw, out var value) ? value : 0;
    }
}
