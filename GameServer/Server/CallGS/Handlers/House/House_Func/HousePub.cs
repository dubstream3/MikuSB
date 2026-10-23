using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Achievement;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using System.Text.Json.Nodes;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

/// <summary>
/// Shared tavern / GuessPoker (酒馆) contract: attribute offsets and the persistence the client reads back.
///
/// Every offset below is client-side A-level evidence from House/HousePub.lua:8-31 — attribute group
/// AttrIds.House.Gid = 101, base sid HouseStorage.HousePubStart = 16000 (HouseStorage.lua:132-133), read
/// through HouseStorage.GetPubAttr (:339-344):
///   Attr_Exp = 1, Attr_LevelRewardGot1 = 2, Attr_LevelRewardGot2 = 3, Attr_BlindRiki1 = 4,
///   Attr_BlindRiki2 = 5, Attr_LevelSeed = 6, Attr_HistoryDataStart = 10, Attr_HistoryDataOffset = 10,
///   MaxHistoryDataType = 9, and inside each history record
///   Data_TotalNum = 1, Data_ContinueNum = 2, Data_MaxScore = 3, Data_AverageRank = 4, Data_DoubtRate = 5,
///   Data_BeDoubtRate = 6, Data_AveragePlayNum = 7, Data_AveDrinkTrueRate = 8.
///
/// Before this change the pub handlers wrote NOTHING: the level was stuck at 1 (GetLevel, :104-121, over
/// the experience at offset 1), CheckRed (:230-232 — level reward bits, blind album, achievements) was
/// permanently on, and the history / blind album tables (:141-164, :124-138) were always empty.
/// </summary>
internal static class PubAttr
{
    internal const uint InfoStart = 16000;

    internal const uint ExpOffset = 1;
    internal const uint LevelRewardGot1Offset = 2;
    internal const uint LevelRewardGot2Offset = 3;
    internal const uint BlindRiki1Offset = 4;
    internal const uint BlindRiki2Offset = 5;
    internal const uint HistoryDataStartOffset = 10;
    internal const uint HistoryDataOffset = 10;
    internal const uint MaxHistoryDataType = 9;

    internal const uint DataTotalNum = 1;
    internal const uint DataContinueNum = 2;
    internal const uint DataMaxScore = 3;
    internal const uint DataAverageRank = 4;
    internal const uint DataDoubtRate = 5;
    internal const uint DataBeDoubtRate = 6;
    internal const uint DataAveragePlayNum = 7;
    internal const uint DataAveDrinkTrueRate = 8;

    internal static uint Sid(uint offset) => InfoStart + offset;

    /// <summary>
    /// HousePub:GetLevel (:104-121) — walks the level ladder over the TOTAL experience at offset 1,
    /// subtracting each level's requirement. Mirrored exactly (including the max level's 0 requirement) so
    /// the server and the client agree on the current level.
    /// </summary>
    internal static uint ResolveLevel(IReadOnlyDictionary<uint, HouseLevelRow> ladder, uint exp)
    {
        if (ladder.Count == 0)
            return 1;

        var maxLevel = ladder.Keys.Max();
        var level = 1u;
        var leftExp = exp;
        for (var i = 1u; i <= maxLevel; i++)
        {
            if (!ladder.TryGetValue(i, out var row))
                break; // the client's ipairs stops at the first gap too

            var needExp = i == maxLevel ? 0u : row.Exp;
            if (leftExp < needExp)
                break;

            leftExp -= needExp;
            level = i == maxLevel ? maxLevel : i + 1;
        }

        return level;
    }

    /// <summary>HousePub:CheckLevelRewardGot (:87-101) — bit nLevel of offset 2, or bit nLevel-30 of offset 3.</summary>
    internal static bool CheckLevelRewardGot(PlayerInstance player, uint level)
    {
        if (level == 0)
            return false;

        var (offset, bit) = level <= 30 ? (LevelRewardGot1Offset, level) : (LevelRewardGot2Offset, level - 30);
        return (HouseAttr.Read(player, Sid(offset)) >> (int)bit & 1u) != 0;
    }

    internal static async Task SetLevelRewardGotAsync(Connection connection, PlayerInstance player, uint level, NtfSyncPlayer sync)
    {
        var (offset, bit) = level <= 30 ? (LevelRewardGot1Offset, level) : (LevelRewardGot2Offset, level - 30);
        var sid = Sid(offset);
        var value = HouseAttr.Read(player, sid);
        await HouseAttr.SetAsync(connection, sid, value | (1u << (int)bit), sync);
    }

    /// <summary>
    /// The experience one settled game is worth.
    ///
    /// Confidence B (inference, belongs on the in-game checklist): the client loads
    /// house/GuessPoker/GuessPokerMode.json into tb.tbExp = Eval(tbLine.RankExp) / tb.tbTrialExp =
    /// Eval(tbLine.TrialExp) (HousePub.lua:47-65) but never reads either table back anywhere in Script, so
    /// the per-game experience is server-authoritative and the column names are the only contract: a
    /// per-rank list indexed by the final rank, with the trial list used for a guide game. A missing table
    /// simply awards 0 (the pre-existing behaviour) instead of guessing further.
    /// </summary>
    internal static uint ResolveExp(JsonObject request, bool guide)
    {
        var modeType = (uint)Math.Max(0, HouseJson.NumField(request, "nModeType"));
        var playerNum = (uint)Math.Max(0, HouseJson.NumField(request, "nPlayerNum"));
        var rank = Math.Max(0, HouseJson.NumField(request, "nRank"));
        if (modeType == 0 || playerNum == 0 || rank == 0)
            return 0;

        var row = HouseMiniGameTables.PubModeRow(modeType, playerNum);
        if (row == null)
            return 0;

        var table = HouseMiniGameTables.ReadUintList(row[guide ? "TrialExp" : "RankExp"]);
        if (table.Count == 0)
            return 0;

        return table[Math.Min(rank, table.Count) - 1];
    }

    /// <summary>
    /// Folds one settled game into the per (game mode, player count) record the client reads back in
    /// HousePub:GetHistoryData (:141-164). The record TYPE is the client's own
    /// GetHistoryType (:167-175) = (modeType - 1) * 3 + playerNum - 1, and the slot layout is quoted above.
    /// The aggregation rule itself (increment the counters, keep the best score, fold the per-game values
    /// into running averages) is inferred from the field names and from the per-game numbers the client
    /// sends in tbData (GuessPokerManager:GetSettlementData, GuessPokerManager.lua:1815-1912) — the layout
    /// is confidence A, the aggregation is confidence B.
    /// </summary>
    internal static async Task UpdateHistoryAsync(
        Connection connection,
        PlayerInstance player,
        JsonObject request,
        bool isWin,
        uint rank,
        NtfSyncPlayer sync)
    {
        var modeType = (uint)Math.Max(0, HouseJson.NumField(request, "nModeType"));
        var playerNum = (uint)Math.Max(0, HouseJson.NumField(request, "nPlayerNum"));
        if (modeType == 0 || playerNum == 0)
            return;

        var type = (modeType - 1) * 3 + playerNum - 1;
        if (type == 0 || type > MaxHistoryDataType)
            return; // the client refuses to read such a record as well

        var infoBase = Sid(HistoryDataStartOffset + (type - 1) * HistoryDataOffset);
        var data = request["tbData"] as JsonObject;

        // Total games (bits 0-15) and wins (bits 16-31).
        var totalSid = infoBase + DataTotalNum;
        var totalValue = HouseAttr.Read(player, totalSid);
        var total = Math.Min(totalValue & 0xffffu, 0xfffeu) + 1;
        var wins = totalValue >> 16 & 0xffffu;
        if (isWin)
            wins = Math.Min(wins, 0xfffeu) + 1;
        await HouseAttr.SetAsync(connection, totalSid, (wins << 16) | total, sync);

        // Consecutive wins (bits 0-15) and consecutive losses (bits 16-31).
        var continueSid = infoBase + DataContinueNum;
        var continueValue = HouseAttr.Read(player, continueSid);
        var conWin = continueValue & 0xffffu;
        var conLose = continueValue >> 16 & 0xffffu;
        if (isWin)
        {
            conWin = Math.Min(conWin, 0xfffeu) + 1;
            conLose = 0;
        }
        else
        {
            conLose = Math.Min(conLose, 0xfffeu) + 1;
            conWin = 0;
        }

        await HouseAttr.SetAsync(connection, continueSid, (conLose << 16) | conWin, sync);

        // Best score.
        var score = data == null ? 0u : (uint)Math.Max(0, HouseJson.NumField(data, "nScore"));
        var maxScoreSid = infoBase + DataMaxScore;
        if (score > HouseAttr.Read(player, maxScoreSid))
            await HouseAttr.SetAsync(connection, maxScoreSid, score, sync);

        // Running averages over the games counted so far (the pre-increment total is exactly that count).
        // Stored scale: average rank and average play count are x100 (uw_guesspoker_riki2.lua:69-70
        // divides them by 100 for display), the three rates are plain percentages.
        await FoldAverageAsync(connection, player, infoBase + DataAverageRank, rank * 100u, totalValue & 0xffffu, sync);
        await FoldAverageAsync(connection, player, infoBase + DataDoubtRate,
            data == null ? 0u : (uint)Math.Max(0, HouseJson.NumField(data, "nDoubt")), totalValue & 0xffffu, sync);
        await FoldAverageAsync(connection, player, infoBase + DataBeDoubtRate,
            data == null ? 0u : (uint)Math.Max(0, HouseJson.NumField(data, "nBeDoubt")), totalValue & 0xffffu, sync);
        await FoldAverageAsync(connection, player, infoBase + DataAveragePlayNum,
            data == null ? 0u : (uint)Math.Max(0, HouseJson.NumField(data, "nAveragePlayNum")), totalValue & 0xffffu, sync);
        await FoldAverageAsync(connection, player, infoBase + DataAveDrinkTrueRate,
            data == null ? 0u : (uint)Math.Max(0, HouseJson.NumField(data, "nDrinkTrueRate")), totalValue & 0xffffu, sync);
    }

    private static async Task FoldAverageAsync(
        Connection connection,
        PlayerInstance player,
        uint sid,
        uint value,
        uint previousGames,
        NtfSyncPlayer sync)
    {
        var previous = HouseAttr.Read(player, sid);
        var average = previousGames == 0
            ? value
            : (uint)(((ulong)previous * previousGames + value) / (previousGames + 1));
        await HouseAttr.SetAsync(connection, sid, average, sync);
    }

    /// <summary>
    /// Unlocks the blind-box album entries (offsets 4 and 5) for the effect ids of the settled game.
    ///
    /// Limits, both documented rather than papered over:
    ///  * the client only reports the LAST round's pair — tbLog.BlindId = "hoaxId-eggId"
    ///    (GuessPokerManager:GetSettlementData, GuessPokerManager.lua:1815-1912), so a game that rolled
    ///    several bl/effects only advances the album for its final pair (partial progress, never wrong
    ///    entries);
    ///  * the id -> album index mapping is the row ordinal of house/GuessPoker/GuessPokerBlind.json
    ///    (GuessPokerConfig:LoadBlindConfig, GuessPokerConfig.lua:168-192, tb.nIndex = i), confidence B
    ///    because it assumes the JSON dump keeps the CSV row order.
    /// </summary>
    internal static async Task UpdateBlindAlbumAsync(
        Connection connection,
        PlayerInstance player,
        JsonObject request,
        NtfSyncPlayer sync)
    {
        if (request["tbLog"] is not JsonObject log)
            return;

        var blindId = log["BlindId"]?.ToString().Trim().Trim('"');
        if (string.IsNullOrEmpty(blindId) || blindId == "NULL")
            return;

        var map = HouseMiniGameTables.BlindIndexMap(HouseMiniGameTables.PubBlindTable);
        if (map.Count == 0)
            return;

        foreach (var part in blindId.Split('-'))
        {
            if (!uint.TryParse(part.Trim(), out var id) || id == 0)
                continue;
            if (!map.TryGetValue(id, out var index) || index == 0)
                continue;

            var (offset, bit) = index <= 30 ? (BlindRiki1Offset, index) : (BlindRiki2Offset, index - 30);
            if (bit == 0 || bit > 31)
                continue;

            var sid = Sid(offset);
            var mask = 1u << (int)bit;
            var value = HouseAttr.Read(player, sid);
            if ((value & mask) != 0)
                continue;

            await HouseAttr.SetAsync(connection, sid, value | mask, sync);
        }
    }
}

[HouseFunc("PubGameEnter")]
public class PubGameEnter : IHouseFuncHandler
{
    private static readonly Random Random = new();

    // Client: HousePub:GameEnter (:234-250) sends { nModeType, nPlayerNum } and HousePub:GameEnterRsp
    // (:252-270) reads nSeed, nModeType, bIsGuide and bHasTry — nModeType picks the game mode for
    // GuessPokerManager:OnEnter / PubGameScene:EnterGameState, so it must be echoed instead of hardcoded
    // (it used to be forced to 1, silently starting a 2-player game for every other mode).
    public Task<CallGSResult> Handle(CallGSContext context, string param)
    {
        var root = HouseJson.ParseObject(param);
        var rsp = new JsonObject
        {
            ["FuncName"] = "PubGameEnter",
            ["nSeed"] = Random.Next(1, 1_000_000_000),
            ["nModeType"] = HouseJson.NumField(root, "nModeType"),
            ["nPlayerNum"] = HouseJson.NumField(root, "nPlayerNum"),
            ["bIsGuide"] = false,
            ["bHasTry"] = false
        };
        return Task.FromResult(CallGSResult.Ok(HouseRequestScript.Success(rsp)));
    }
}

[HouseFunc("PubGameMulExit")]
public class PubGameMulExit : IHouseFuncHandler
{
    // Client: HousePub:GameMulExit (:272-275) never shows the connection spinner, so this reply has no
    // dispatcher entry and is harmless either way — the request is still echoed for symmetry.
    public Task<CallGSResult> Handle(CallGSContext context, string param)
    {
        var root = HouseJson.ParseObject(param);
        var rsp = root == null
            ? new JsonObject { ["FuncName"] = "PubGameMulExit" }
            : HouseJson.EchoReply(root, "PubGameMulExit");
        return Task.FromResult(CallGSResult.Ok(HouseRequestScript.Success(rsp)));
    }
}

[HouseFunc("PubGameSettlement")]
public class PubGameSettlement : IHouseFuncHandler
{
    // Client: HousePub:GameSettlement (:277-297) sends the full report — nModeType, nPlayerNum, bWin,
    // nRank, bGuide, tbData, tbLog (GuessPokerManager:EndGame, GuessPokerManager.lua:1764-1799) — and
    // re-sends it after a reconnect, which is why a second settle must stay idempotent enough not to
    // double count badly (the counter increments are the only non-idempotent part; the client only
    // re-sends when the previous attempt never got answered).
    //
    // HousePub:GameSettlementRsp (:299-309) builds { IsWin = bWin, rank = nRank, nExp = nExp } for the
    // settlement screen, so the reply must echo bWin/nRank and report the experience as nExp — the old
    // code answered only nAddExp = 0, leaving all three fields nil.
    public async Task<CallGSResult> Handle(CallGSContext context, string param)
    {
        var root = HouseJson.ParseObject(param);
        if (root == null) return CallGSResult.NoResponse();

        var player = context.Connection.Player!;
        var sync = new NtfSyncPlayer();
        var isWin = HouseJson.BoolField(root, "bWin");
        var guide = HouseJson.BoolField(root, "bGuide");
        var rank = (uint)Math.Max(0, HouseJson.NumField(root, "nRank"));

        var addExp = PubAttr.ResolveExp(root, guide);
        if (addExp > 0)
        {
            var expSid = PubAttr.Sid(PubAttr.ExpOffset);
            await HouseAttr.SetAsync(context.Connection, expSid, HouseAttr.Read(player, expSid) + addExp, sync);
        }

        await PubAttr.UpdateHistoryAsync(context.Connection, player, root, isWin, rank, sync);
        await PubAttr.UpdateBlindAlbumAsync(context.Connection, player, root, sync);

        var rsp = HouseJson.EchoReply(root, "PubGameSettlement");
        rsp["bWin"] = isWin;
        rsp["nRank"] = rank;
        rsp["nExp"] = addExp;
        return CallGSResult.Ok(HouseRequestScript.Success(rsp), sync);
    }
}

[HouseFunc("PubGameGetReward")]
public class PubGameGetReward : IHouseFuncHandler
{
    // Client: HousePub:GetReward(nLevel) (:311-318); the one-key button passes -1
    // (umg_guesspoker_level.lua:49-55, guarded by CheckCanGetLevelReward :205-213) and each row passes its
    // own level. HousePub:GetRewardRsp (:320-328) grants tbReward and forwards the reply to the reward UI,
    // whose level list requires tbGetLevel (umg_guesspoker_level.lua:57-67 bails out without it and pokes
    // ON_STATE_CHANGE on every claimed level). The old handler answered {"FuncName"} only, so nothing was
    // ever granted and the badges never refreshed.
    public async Task<CallGSResult> Handle(CallGSContext context, string param)
    {
        var root = HouseJson.ParseObject(param);
        var player = context.Connection.Player!;
        var sync = new NtfSyncPlayer();

        var ladder = HouseMiniGameTables.LevelLadder(HouseMiniGameTables.PubRewardTable);
        var currentLevel = PubAttr.ResolveLevel(ladder, HouseAttr.Read(player, PubAttr.Sid(PubAttr.ExpOffset)));
        var selected = HouseJson.NumField(root, "nLevel");

        var levels = new List<uint>();
        if (selected <= 0)
        {
            for (var level = 1u; level <= currentLevel; level++)
            {
                if (!PubAttr.CheckLevelRewardGot(player, level))
                    levels.Add(level);
            }
        }
        else if ((uint)selected <= currentLevel && !PubAttr.CheckLevelRewardGot(player, (uint)selected))
        {
            levels.Add((uint)selected);
        }
        else
        {
            Logger.GetByClassName().Warn(
                $"Pub level reward: level {selected} not claimable (current {currentLevel}) for uid={player.Uid}.");
        }

        var granted = new JsonArray();
        var gotLevel = new JsonArray();
        foreach (var level in levels)
        {
            if (!ladder.TryGetValue(level, out var conf) || conf.Rewards.Count == 0)
                continue; // no dumped ladder row: nothing to grant, the client just closes the spinner

            var rows = new List<IReadOnlyList<uint>>();
            foreach (var row in conf.Rewards)
                rows.Add(row);

            var levelGranted = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);
            foreach (var entry in levelGranted)
            {
                if (entry != null)
                    granted.Add(entry.DeepClone());
            }

            await PubAttr.SetLevelRewardGotAsync(context.Connection, player, level, sync);
            gotLevel.Add(JsonValue.Create(level));
        }

        if (granted.Count > 0)
        {
            DatabaseHelper.SaveDatabaseType(player.Data);
            DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
            DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
        }

        var rsp = new JsonObject
        {
            ["FuncName"] = "PubGameGetReward",
            ["tbReward"] = granted,
            ["tbGetLevel"] = gotLevel
        };

        Logger.GetByClassName().Info(
            $"Pub level reward: claimed {gotLevel.Count} level(s), {granted.Count} row(s) granted for uid={player.Uid}.");

        return CallGSResult.Ok(HouseRequestScript.Success(rsp), sync);
    }
}

[HouseFunc("PubGameGetAchReward")]
public class PubGameGetAchReward : IHouseFuncHandler
{
    // Client: HousePub:GetAchReward(nAchId) (:330-337); the one-key button passes -1
    // (umg_guesspoker_task.lua:14-15) and each row its own id (:59). The ids are quest-group rows
    // (AttrIds.Achievement.QuestGroupId = 7) of achievement/activity_quest.json, which the client lists
    // through Achievement.GetQuestConfig over house/GuessPoker/GuessPokerAchv.json
    // (HousePub.lua:67-78). GetAchRewardRsp (:339-347) grants tbReward and refreshes the reward UI.
    public async Task<CallGSResult> Handle(CallGSContext context, string param)
    {
        var root = HouseJson.ParseObject(param);
        var player = context.Connection.Player!;
        var sync = new NtfSyncPlayer();

        var raw = root == null ? 0 : HouseJson.NumField(root, "nAchId");
        if (raw == 0 && root != null)
            raw = HouseJson.NumField(root, "nId");

        var achvIds = HouseMiniGameTables.Ids(HouseMiniGameTables.PubAchvTable);
        var granted = new JsonArray();
        if (raw < 0)
        {
            // -1 = everything claimable; the id list is required to tell the pub achievements apart.
            foreach (var achvId in achvIds)
            {
                var claimedRows = await ClaimAsync(player, achvId, sync);
                if (claimedRows == null)
                    continue;

                foreach (var entry in claimedRows)
                {
                    if (entry != null)
                        granted.Add(entry.DeepClone());
                }
            }
        }
        else if (raw > 0 && (achvIds.Count == 0 || achvIds.Contains((uint)raw)))
        {
            var claimedRows = await ClaimAsync(player, (uint)raw, sync);
            if (claimedRows != null)
            {
                foreach (var entry in claimedRows)
                {
                    if (entry != null)
                        granted.Add(entry.DeepClone());
                }
            }
        }

        var rsp = new JsonObject { ["FuncName"] = "PubGameGetAchReward", ["tbReward"] = granted };
        return CallGSResult.Ok(HouseRequestScript.Success(rsp), sync);
    }

    private static async Task<JsonArray?> ClaimAsync(PlayerInstance player, uint questId, NtfSyncPlayer sync)
    {
        if (!GameData.ActivityQuestData.TryGetValue(questId, out var quest))
            return null;

        return await AchievementClaimService.TryClaimAsync(
            player,
            AchievementClaimService.QuestGroupId,
            quest.Id,
            quest.CheckValue,
            AchievementClaimService.ReadRewardRows(quest.Rewards),
            sync);
    }
}

[HouseFunc("PubGameAchievementFinish")]
public class PubGameAchievementFinish : IHouseFuncHandler
{
    // Client: HousePub:AchievementFinish(nId) (:349-369) reports a finished achievement quest and
    // deliberately does NOT show the spinner. HousePub:AchievementFinishRsp (:371-384) returns early when
    // the reply has no nId ("AchievementFinishRsp error,nil nId"), which is what the old handler caused —
    // so the achievement tip and its sound never played; the quest progress itself was never written
    // either, which is why the pub achievement rewards could never become claimable.
    public Task<CallGSResult> Handle(CallGSContext context, string param)
    {
        var root = HouseJson.ParseObject(param);
        var player = context.Connection.Player!;
        var sync = new NtfSyncPlayer();

        var id = (uint)Math.Max(0, HouseJson.NumField(root, "nId"));
        if (id == 0)
            id = (uint)Math.Max(0, HouseJson.NumField(root, "nAchId"));

        if (id > 0 && GameData.ActivityQuestData.TryGetValue(id, out var quest))
        {
            AchievementClaimService.Complete(
                player,
                AchievementClaimService.QuestGroupId,
                quest.Id,
                quest.CheckValue,
                sync);
        }

        var rsp = root == null
            ? new JsonObject { ["FuncName"] = "PubGameAchievementFinish" }
            : HouseJson.EchoReply(root, "PubGameAchievementFinish");
        rsp["nId"] = id;
        return Task.FromResult(CallGSResult.Ok(HouseRequestScript.Success(rsp), sync));
    }
}
