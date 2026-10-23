using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.Quest;
using MikuSB.GameServer.Game.Riki;
using MikuSB.GameServer.Game.BossPvp;
using MikuSB.Proto;
using MikuSB.GameServer.Server.CallGS.Handlers.Dlc;
using MikuSB.GameServer.Server.CallGS.Handlers.DreamCard;
using MikuSB.GameServer.Server.CallGS.Handlers.FunTimed;
using MikuSB.GameServer.Server.CallGS.Handlers.GirlRush;
using MikuSB.GameServer.Server.CallGS.Handlers.BagRogue;
using MikuSB.GameServer.Server.CallGS.Handlers.Rogue;
using MikuSB.GameServer.Server.CallGS.Handlers.SeasonBoss;
using MikuSB.GameServer.Server.CallGS.Handlers.AlienDefense;
using MikuSB.GameServer.Server.CallGS.Handlers.Activity;
using MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;
using MikuSB.GameServer.Server.CallGS.Handlers.Tower;
using MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;
using MikuSB.GameServer.Server.CallGS.Handlers.Defend;
using MikuSB.GameServer.Server.CallGS.Handlers.Online;
using MikuSB.GameServer.Server.CallGS.Handlers.Repression;
using MikuSB.GameServer.Server.CallGS.Handlers.WorldBoss;
using MikuSB.GameServer.Server.CallGS.Handlers.FarmWar;
using MikuSB.Util;
using MikuSB.GameServer.Server.CallGS;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chapter;

[CallGSApi("Chapter_DealLevelSettlement")]
public class Chapter_DealLevelSettlement : CallGSHandler<DealLevelSettlementParam>
{
    private static readonly Logger Logger = new("Chapter");

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, DealLevelSettlementParam request)
    {
        var (payload, extraSync) = await BuildSettlementPayloadAsync(context.Player, request.SCmd, request.TbParam);
        var response = new JsonObject
        {
            ["sCmd"] = request.SCmd ?? "Chapter_LevelSettlement",
            ["tbParam"] = payload
        };

        // 图鉴怪物: every settlement payload carries tbMonster (RikiLogic:GetMonsterData at 30 call
        // sites), but no handler ever read it and attribute group 103 had no writer, so the monster page
        // of the 图鉴 could never unlock. Unlocking here covers every sCmd branch at once, and the new
        // bits ride back on the reply's sync so the client repaints the page immediately.
        var candidateSync = extraSync ?? new NtfSyncPlayer();
        if (RikiUnlockService.UnlockMonsters(context.Player, request.TbParam, candidateSync) > 0 &&
            extraSync == null)
        {
            extraSync = candidateSync;
        }

        return CallGSResult.Ok(response, extraSync);
    }

    private static async ValueTask<(JsonNode Payload, NtfSyncPlayer? Sync)> BuildSettlementPayloadAsync(
        PlayerInstance player,
        string? sCmd,
        JsonNode? tbParam)
    {
        if (string.Equals(sCmd, "Chapter_LevelSettlement", StringComparison.Ordinal))
            return await HandleLevelSettlementAsync(player, QuestLevelType.Chapter, tbParam);

        if (string.Equals(sCmd, "Daily_LevelSettlement", StringComparison.Ordinal) ||
            string.Equals(sCmd, "Role_LevelSettlement", StringComparison.Ordinal))
        {
            var levelType = string.Equals(sCmd, "Daily_LevelSettlement", StringComparison.Ordinal)
                ? QuestLevelType.Daily
                : QuestLevelType.Role;
            return await HandleLevelSettlementAsync(player, levelType, tbParam);
        }

        if (string.Equals(sCmd, "Chapter_NewPrologueSettlement", StringComparison.Ordinal))
        {
            return await HandleNewPrologueSettlementAsync(player, tbParam);
        }

        if (string.Equals(sCmd, "BossPvpLogic_LevelSettlement", StringComparison.Ordinal))
        {
            var normalized = NormalizeBossPvpSettlement(tbParam);
            var (response, sync) = BossPvpService.HandleSettlement(player, normalized);
            return (response, sync);
        }

        if (string.Equals(sCmd, "BossPvpLogic_LevelFail", StringComparison.Ordinal))
        {
            var (response, sync) = BossPvpService.HandleFail(player, tbParam);
            return (response, sync);
        }

        if (string.Equals(sCmd, "TowerLevel_LevelSettlement", StringComparison.Ordinal))
        {
            var (response, sync) = TowerLevel_LevelSettlement.HandleSettlement(player, tbParam);
            return (response, sync);
        }

        if (string.Equals(sCmd, "TowerEventChapter_LevelSettlement", StringComparison.Ordinal))
        {
            var (response, sync) = TowerEventChapter_LevelSettlement.HandleSettlement(player, tbParam);
            return (response, sync);
        }

        if (string.Equals(sCmd, "VirCaptureTower_LevelSettlement", StringComparison.Ordinal))
        {
            var (response, sync) = VirCaptureTower_LevelSettlement.HandleSettlement(player, tbParam);
            return (response, sync);
        }

        if (string.Equals(sCmd, "DreamCard_LevelSettlement", StringComparison.Ordinal))
        {
            var (response, sync) = DreamCard_LevelSettlement.HandleSettlement(player, tbParam);
            return (response, sync);
        }

        // DLC chapters (dlc/dlc1/chapter/level.json) are not part of the generic chapter tables and
        // have no QuestLevelType member, so they carry their own reward/settlement path.
        if (string.Equals(sCmd, "DLC_Chapter_LevelSettlement", StringComparison.Ordinal))
        {
            return await DlcChapterSettlement.HandleSettlementAsync(player, tbParam);
        }

        // 多诺-巅峰对决 arena challenge runs use the same settlement channel but their reward rows
        // live in dlc/DonoDuel/donoduel_arena_battle.json.
        if (string.Equals(sCmd, "DonoDuelArena_LevelSettlement", StringComparison.Ordinal))
        {
            return await DonoDuelArenaSettlement.HandleSettlementAsync(player, tbParam);
        }

        // 多诺抓捕玩法 (rogue) settles twice per run: once per room and once for the whole level; both
        // arrive through Reconnect.Send_SettleInfo instead of a dedicated CallGS API.
        if (string.Equals(sCmd, "DonoDuelRogue_RoomSettlement", StringComparison.Ordinal))
        {
            return await DonoDuelRogueSettlement.HandleRoomSettlementAsync(player, tbParam);
        }

        if (string.Equals(sCmd, "DonoDuelRogue_LevelSettlement", StringComparison.Ordinal))
        {
            return await DonoDuelRogueSettlement.HandleLevelSettlementAsync(player, tbParam);
        }

        // 弑神斗场 (season boss) runs go through Reconnect.Send_SettleInfo, so their settlement arrives
        // wrapped in this channel with sCmd = "SeasonBoss_LevelSettlement" rather than as its own API.
        if (string.Equals(sCmd, "SeasonBoss_LevelSettlement", StringComparison.Ordinal))
        {
            return SeasonBoss_LevelSettlement.HandleSettlement(player, tbParam);
        }

        // 异星守护 (alien defense) runs use the same wrapped channel with
        // sCmd = "AlienDefense_LevelSettlement" (AlienDefense.LevelSettlement sends it through
        // Reconnect.Send_SettleInfo); the direct CallGS API is never hit by the live client.
        if (string.Equals(sCmd, "AlienDefense_LevelSettlement", StringComparison.Ordinal))
        {
            return AlienDefense_LevelSettlement.HandleSettlement(player, tbParam);
        }

        // 先行试用 / 角色试玩 (gacha try) trial runs use the same wrapped channel with
        // sCmd = "GachaTry_LevelSettlement" (GachaTry.Req_LevelSettlement -> Reconnect.Send_SettleInfo);
        // the direct CallGS API is never hit by the live client. Without an answer here the client's
        // settlement retry gives up after five attempts and force-disconnects it.
        if (string.Equals(sCmd, "GachaTry_LevelSettlement", StringComparison.Ordinal))
        {
            return await GachaTry_LevelSettlement.HandleSettlementAsync(player, tbParam);
        }

        // 抢滩登陆 (Sevendays / FunTimed) settles over the same wrapped channel with
        // sCmd = "FunTimed_LevelSettlement" (Launch/FunTimed/FunTimed.lua:372 ->
        // Reconnect.Send_SettleInfo). Its levels are not part of the chapter tables, and the callback
        // only reads tbAwards, so the per star reward tiers have to come from fun_timed/level.json.
        if (string.Equals(sCmd, "FunTimed_LevelSettlement", StringComparison.Ordinal))
        {
            return await FunTimedSettlement.HandleSettlementAsync(player, tbParam);
        }

        // 异绪消解 (GirlRush) settles through the same wrapper with sCmd = "GirlRush_LevelSettlement"
        // (GirlRush/GirlRush.lua:539-574). The callback drops the payload, but the run's nScoreLevel is
        // the only place the stage rank ever arrives, and the reward board derives its state from the
        // kept best rank plus the clear counter written here.
        if (string.Equals(sCmd, "GirlRush_LevelSettlement", StringComparison.Ordinal))
        {
            return await GirlRushSettlement.HandleSettlementAsync(player, tbParam);
        }

        // 世界BOSS (WorldBoss) settles over the same wrapped channel:
        //   sCmd = "WorldBossLogic_LevelSettlement"  (Challenge/WorldBoss/WorldBoss.lua:699)
        //   sCmd = "WorldBossLogic_LevelFail"        (:707, with nManualExit = 1)
        // Both callbacks ignore the payload, but the run's nDamageNum only arrives here - it feeds the
        // (179, base+2) score board and the 21001..21020 quest progress (percent / raw damage /
        // recorded attempts). The wrapper force-disconnects after five two-second retries.
        if (string.Equals(sCmd, "WorldBossLogic_LevelSettlement", StringComparison.Ordinal))
        {
            return WorldBossSettlement.HandleSettlement(player, tbParam, false);
        }

        if (string.Equals(sCmd, "WorldBossLogic_LevelFail", StringComparison.Ordinal))
        {
            return WorldBossSettlement.HandleSettlement(player, tbParam, true);
        }

        // 蔬菜大战 (FarmWar) settles over the same wrapped channel with
        // sCmd = "FarmWarLogic_LevelSettlement" (FarmWarActivity/FarmWarActivity.lua:853). The callback
        // only reads tbNewPlant / sErr; the clear counters (H defend health, W wave) go into the packed
        // (21, stage) record and the first clear grants farmwarout_level.json Rewards.
        if (string.Equals(sCmd, "FarmWarLogic_LevelSettlement", StringComparison.Ordinal))
        {
            return FarmWarSettlement.HandleSettlement(player, tbParam);
        }

        // 背包肉鸽 (BagRogue) settles over the same wrapped channel with
        // sCmd = "BagRogue_LevelSettlement" (DLC/BagRogue/BagRogue.lua:1672). Ordinary nodes read
        // tbAward from the reply (Launch/Classes/BagRogueLogic.lua:57-58); the ending node ignores it and
        // reads the best-record string (61,5) instead, so the payload differs by node type.
        if (string.Equals(sCmd, "BagRogue_LevelSettlement", StringComparison.Ordinal))
        {
            return await BagRogueSettlement.HandleSettlementAsync(player, tbParam);
        }

        // 大富翁棋盘战斗 settles over the same wrapped channel with sCmd = "Chess_LevelSettlement"
        // (Chess/ChessClient.lua:504). The callback just forwards tbAward to Launch.Response, so an empty
        // award list is enough to close the loading mask; without a branch here the request would hit the
        // generic echo fallback and never be answered as a settlement at all.
        if (string.Equals(sCmd, "Chess_LevelSettlement", StringComparison.Ordinal))
        {
            return (new JsonObject { ["tbAward"] = new JsonArray() }, null);
        }

        // 2D 肉鸽 (DLC1 Roguelike) settles over the same wrapped channel with
        // sCmd = "Rogue_LevelSettlement" (DLC/Rogue/Rogue.lua:998). The client re-encodes the inner
        // payload and its s2c handler indexes tbParam[1].Awards (Rogue.lua:1006-1013), so the payload
        // must be an array; an sError there would abort the run via ExpirationExit.
        if (string.Equals(sCmd, "Rogue_LevelSettlement", StringComparison.Ordinal))
        {
            return await RogueSettlement.HandleSettlementAsync(player, tbParam);
        }

        // 悖论迷宫 (Rogue3D) settles over the same wrapped channel with
        // sCmd = "Rogue3D_LevelSettlement" (DLC/Rogue3D/Rogue3DLogic.lua:1867) and
        // sCmd = "Rogue3D_SeasonLevelSettlement" (:1650), both via Reconnect.Send_SettleInfo. The client
        // unlocks the later difficulties from the clear counters these settlements write, so answering
        // here (instead of falling through to the echo below) is what unlocks them.
        if (string.Equals(sCmd, "Rogue3D_LevelSettlement", StringComparison.Ordinal))
        {
            return await Rogue3DSettlement.HandleLevelSettlementAsync(player, tbParam);
        }

        if (string.Equals(sCmd, "Rogue3D_SeasonLevelSettlement", StringComparison.Ordinal))
        {
            return await Rogue3DSettlement.HandleSeasonLevelSettlementAsync(player, tbParam);
        }

        // 悖论迷宫积分模式 (Rogue3DScoreModel) settles over the same wrapped channel too:
        //   sCmd = "Rogue3DScoreModel_BuffLevelSettlement"  (DLC/Rogue3D/Rogue3DScoreModel.lua:1169)
        //   sCmd = "Rogue3DScoreModel_ScoreLevelSettlement" (:1303)
        // Both panels read fields the echo cannot supply - tbMonsterDropRiki without a nil guard
        // (umg_rogue25_settlement.lua:49, umg_rogue_settlement.lua:87) and nNewScore/nOldScore compared
        // directly (umg_rogue_settlement.lua:97) - so an echoed payload made them throw.
        if (string.Equals(sCmd, "Rogue3DScoreModel_BuffLevelSettlement", StringComparison.Ordinal))
        {
            return await Rogue3DScoreSettlement.HandleBuffLevelSettlementAsync(player, tbParam);
        }

        if (string.Equals(sCmd, "Rogue3DScoreModel_ScoreLevelSettlement", StringComparison.Ordinal))
        {
            return await Rogue3DScoreSettlement.HandleScoreLevelSettlementAsync(player, tbParam);
        }

        // 防御活动 (defend) settles over the same wrapped channel with
        // sCmd = "DefendLogic_LevelSettlement" (Defend.lua:1020 -> Reconnect.Send_SettleInfo); the
        // difficulty clear counters it writes are what the next difficulties unlock on.
        if (string.Equals(sCmd, "DefendLogic_LevelSettlement", StringComparison.Ordinal))
        {
            return await DefendSettlement.HandleLevelSettlementAsync(player, tbParam);
        }

        // 压抑区 (repression) settles over the same wrapped channel with
        // sCmd = "Repression_Chapter_LevelSettlement" (Repression_Chapter.lua:1008 ->
        // Reconnect.Send_SettleInfo); its levels are not in the chapter tables, so it writes the pass
        // counter itself instead of going through QuestManager.
        if (string.Equals(sCmd, "Repression_Chapter_LevelSettlement", StringComparison.Ordinal))
        {
            return await RepressionSettlement.HandleLevelSettlementAsync(player, tbParam);
        }

        // DLC 爬塔 (Runaway / climb tower) settles over the same wrapped channel with
        // sCmd = "ClimbTower_LevelSettlement" (Launch/Classes/ClimbTowerBattleLogic.lua:41 ->
        // DLC/ClimbTower/ClimbTower.lua:632 -> Reconnect.Send_SettleInfo); the direct CallGS API is never
        // hit by the live client. This is a different mode from 逆境之塔 (TowerLevel_LevelSettlement), and
        // its per-level slots are keyed by battle id, not level id.
        if (string.Equals(sCmd, "ClimbTower_LevelSettlement", StringComparison.Ordinal))
        {
            return await ClimbTowerSettlement.HandleSettlementAsync(player, tbParam);
        }

        // 幸运卡牌 (DreamCard) reports a lost run through the same wrapped channel
        // (DLC/DreamCard/DreamCard.lua:2318 -> Reconnect.Send_SettleInfo). Its callback resolves the level
        // config from the echoed nID/nType/nDiff and refreshes the score record on NewRecord, so the echo
        // fallback below was not enough: it answered without NewRecord and never recorded the challenge
        // score, which is why a failed run skipped its own settlement panel.
        if (string.Equals(sCmd, "DreamCard_LevelFail", StringComparison.Ordinal))
        {
            var (response, sync) = DreamCard_LevelFail.HandleFail(player, tbParam);
            return (response, sync);
        }

        // 防御活动 (defend) mid-run checkpoint, sent by the "save defend info" game task
        // (Defend.lua:793 -> Reconnect.Send_SettleInfo). The callback only closes the connection overlay,
        // but the api used to fall through to the echo below, so checkpoints were invisible in the log.
        if (string.Equals(sCmd, "DefendLogic_SaveFightData", StringComparison.Ordinal))
        {
            return await DefendFightSave.HandleSaveFightDataAsync(player, tbParam);
        }

        // 虚境探索 (vir capture) reports the end of an exploration over the same wrapped channel with
        // sCmd = "VirCaptureLevel_LevelSettlement" (DLC/VirCapture/VirCapture.lua:2318-2334 ->
        // Reconnect.Send_SettleInfo). The run state is persisted while playing, so this branch validates
        // and logs the report instead of letting it fall through to the echo below.
        if (string.Equals(sCmd, "VirCaptureLevel_LevelSettlement", StringComparison.Ordinal))
        {
            return await VirCaptureLevelSettlement.HandleSettlementAsync(player, tbParam);
        }

        // 联机-单机训练关卡 (online training) settles over the same wrapped channel with
        // sCmd = "OnlineTraining_LevelSettlement" (Launch/Online/OnlineTraining.lua:172 ->
        // Launch/Classes/OnlineTrainingLogic.lua:32); the direct CallGS API is never hit by the live client.
        // Its cleared-run counter lives at (gid 24, level id) and nothing else ever writes it.
        if (string.Equals(sCmd, "OnlineTraining_LevelSettlement", StringComparison.Ordinal))
        {
            return await OnlineTrainingLevelSettlement.HandleSettlementAsync(player, tbParam);
        }

        return (tbParam?.DeepClone() ?? new JsonObject(), null);
    }

    private static async ValueTask<(JsonNode Payload, NtfSyncPlayer? Sync)> HandleNewPrologueSettlementAsync(
        PlayerInstance player,
        JsonNode? tbParam)
    {
        var request = tbParam?.Deserialize<NewPrologueSettlementParam>(CallGSJson.Options);
        if (request == null || request.LevelId == 0)
        {
            Logger.Error($"Invalid plot settlement payload: {tbParam?.ToJsonString() ?? "null"}");
            return (new JsonObject { ["sErr"] = "error.BadParam" }, new NtfSyncPlayer());
        }

        if (!player.QuestManager.IsPlotLevel(request.LevelId))
            return BuildEmptyNewPrologueResponse(tbParam);

        var result = await player.QuestManager.SettlePlotLevelAsync(request.LevelId);
        if (result == null)
        {
            Logger.Error($"Rejected plot settlement: levelId={request.LevelId}");
            return (new JsonObject { ["sErr"] = "error.BadParam" }, new NtfSyncPlayer());
        }

        var response = new JsonObject();
        if (tbParam is JsonObject source && source.TryGetPropertyValue("bWaitServer", out var bWaitServer))
            response["bWaitServer"] = bWaitServer?.DeepClone();
        response["tbShowAward"] = FlattenRewardCategories(result.Value.Rewards);
        return (response, result.Value.Sync);
    }

    private static (JsonNode Payload, NtfSyncPlayer? Sync) BuildEmptyNewPrologueResponse(JsonNode? tbParam)
    {
        var response = new JsonObject();
        if (tbParam is JsonObject source && source.TryGetPropertyValue("bWaitServer", out var bWaitServer))
            response["bWaitServer"] = bWaitServer?.DeepClone();
        response["tbShowAward"] = new JsonArray();
        return (response, null);
    }

    private static JsonArray FlattenRewardCategories(JsonArray categories)
    {
        var rewards = new JsonArray();
        foreach (var category in categories)
        {
            if (category is not JsonArray categoryRewards)
                continue;

            foreach (var reward in categoryRewards)
                rewards.Add(reward?.DeepClone());
        }

        return rewards;
    }

    private static async ValueTask<(JsonNode Payload, NtfSyncPlayer Sync)> HandleLevelSettlementAsync(
        PlayerInstance player,
        QuestLevelType levelType,
        JsonNode? tbParam)
    {
        var req = tbParam?.Deserialize<LevelSettlementParam>(CallGSJson.Options);
        if (req == null || req.LevelId == 0)
        {
            Logger.Error($"Invalid level settlement payload: {tbParam?.ToJsonString() ?? "null"}");
            return (new JsonObject { ["sErr"] = "error.BadParam" }, new NtfSyncPlayer());
        }

        if (levelType == QuestLevelType.Chapter && player.QuestManager.IsPlotLevel(req.LevelId))
        {
            var plotResult = await player.QuestManager.SettlePlotLevelAsync(req.LevelId);
            if (plotResult == null)
            {
                Logger.Error($"Rejected plot level settlement: levelId={req.LevelId}");
                return (new JsonObject { ["sErr"] = "error.BadParam" }, new NtfSyncPlayer());
            }

            return (plotResult.Value.Rewards, plotResult.Value.Sync);
        }

        var result = await player.QuestManager.SettleLevelAsync(levelType, req.LevelId, req.StarMask, req.Seed);
        if (result == null)
        {
            Logger.Error($"Rejected level settlement: type={levelType} levelId={req.LevelId} seed={req.Seed}");
            return (new JsonObject { ["sErr"] = "error.BadParam" }, new NtfSyncPlayer());
        }

        return (result.Value.Rewards, result.Value.Sync);
    }

    private static JsonNode? NormalizeBossPvpSettlement(JsonNode? tbParam)
    {
        if (tbParam is not JsonObject obj)
            return tbParam;

        var clone = obj.DeepClone() as JsonObject ?? obj;
        if (clone.TryGetPropertyValue("ResidueTime", out var residueNode) &&
            residueNode is JsonValue residueValue &&
            residueValue.TryGetValue<double>(out var residueTime))
        {
            clone["ResidueTime"] = (int)Math.Max(0, Math.Round(residueTime, MidpointRounding.AwayFromZero));
        }

        return clone;
    }
}

public sealed class DealLevelSettlementParam
{
    [JsonPropertyName("sCmd")]
    public string? SCmd { get; set; }

    [JsonPropertyName("tbParam")]
    public JsonNode? TbParam { get; set; }
}

public sealed class LevelSettlementParam
{
    [JsonPropertyName("nID")]
    public uint LevelId { get; set; }

    [JsonPropertyName("nStar")]
    public int StarMask { get; set; }

    [JsonPropertyName("nSeed")]
    public uint Seed { get; set; }
}

public sealed class NewPrologueSettlementParam
{
    [JsonPropertyName("nID")]
    public uint LevelId { get; set; }
}
