using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Achievement;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using System.Text.Json.Nodes;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

/// <summary>
/// Shared house-throwing (冰趣蜜弹) contract: attribute offsets and the reply-name fixes.
///
/// Client origin of every offset (House/HouseMiniGame/HouseThrow/HouseThrowSeverMessage.lua:8-37):
/// UnlockRikiNum = 1, ChallengePoints = 2, CurLevelID = 3, CurExpID = 4, Attr_LevelSeed = 6,
/// RoundID = 7, Tutorial = 10, LevelRewardGetState = 20 (20-50), UnlockDrinkStateStartId = 50
/// (50-100), QuestTypeOne/Two/Three = 110/140/170. The reads go through
/// HouseStorage.GetBeachAttr (HouseStorage.lua:357-366) = me:GetAttribute(101, 17000 + offset)
/// (HouseStorage.HouseBeachStart = 17000, :135), group AttrIds.House.Gid = 101.
///
/// Reply names — the client dispatcher (HouseMessageHandle.lua:26-31) looks the incoming FuncName up in
/// HouseMessageHandle.tbCallback / HouseMessageHandle and, when nothing matches, returns WITHOUT calling
/// UI.CloseConnection(). UI.ShowConnection (UMG/UI.lua:720-731) is a boolean latch, so a reply under a
/// name nobody dispatches locks the 联网中 spinner forever and no callback ever fires:
///   * the client sends 'ThrowGameTutorialFinish' (HouseThrowSeverMessage.lua:54) but the dispatch key is
///     'ThrowTutorialFinish' (HouseMessageHandle.lua:557 -> ThrowTutorialFinishRsp, :295-309);
///   * the client sends 'ThrowGameGetLevelReward' (:194) but the dispatch key is 'ThrowGameGetReward'
///     (HouseMessageHandle.lua:566 -> GetRewardRsp, :199-211);
///   * the client sends 'ThrowGameGetAchReward' (:221) and NO dispatch key of that name exists anywhere
///     in Script — the module has a single generic reward callback (GetRewardRsp, reached through
///     'ThrowGameGetReward'), which is what the achievement claim must answer under as well.
/// </summary>
internal static class ThrowAttr
{
    /// <summary>AttrIds.House.Gid is 101; sid = InfoStart + offset.</summary>
    internal const uint InfoStart = 17000;

    internal const uint ChallengePointsOffset = 2;
    internal const uint CurLevelOffset = 3;
    internal const uint CurExpOffset = 4;
    internal const uint TutorialOffset = 10;
    internal const uint LevelRewardGotStartOffset = 20;
    internal const uint UnlockDrinkStartOffset = 50;
    internal const int ModeChallenge = 2;

    internal static uint Sid(uint offset) => InfoStart + offset;

    /// <summary>HouseThrowSeverMessage:GetCurLevel (:89-92) — offset 3 with a floor of 1.</summary>
    internal static uint GetLevel(PlayerInstance player)
    {
        var level = HouseAttr.Read(player, Sid(CurLevelOffset));
        return level == 0 ? 1 : level;
    }

    /// <summary>
    /// Adds the experience a settlement is worth and rolls the explicit level up through the ladder.
    ///
    /// The client reads the level from offset 3 and the LEFT-OVER experience inside the level from
    /// offset 4 (:89-96) and derives the level requirement from
    /// HouseThrowMgr.tbConfig.tbAwardConfig[GetCurLevel()].Exp (:98-105) — house/Throw/ThrowAward.json,
    /// the server dump of the client's house/Throw/ThrowAward.txt (HouseThrowConfig.lua:224-237).
    /// Nothing in HouseThrowMgr (or anywhere else in Script) ever calls SetBeahAttr for offsets 3/4, so
    /// the level line is server-owned; without this write the level is stuck at 1
    /// (GetCurLevel -> 1) and GetLevelAwardRed (:140-150) keeps the red dot on forever.
    /// </summary>
    internal static async Task AddExpAsync(Connection connection, PlayerInstance player, uint addExp, NtfSyncPlayer sync)
    {
        var ladder = HouseMiniGameTables.LevelLadder(HouseMiniGameTables.ThrowRewardTable);
        if (ladder.Count == 0)
            return; // no ladder dumped: leave the level line untouched instead of inventing one

        var startLevel = GetLevel(player);
        var level = startLevel;
        var exp = HouseAttr.Read(player, Sid(CurExpOffset)) + addExp;
        var maxLevel = ladder.Keys.Max();
        while (level < maxLevel && ladder.TryGetValue(level, out var row) && row.Exp > 0 && exp >= row.Exp)
        {
            exp -= row.Exp;
            level++;
        }

        if (level != startLevel)
            await HouseAttr.SetAsync(connection, Sid(CurLevelOffset), level, sync);
        await HouseAttr.SetAsync(connection, Sid(CurExpOffset), exp, sync);
    }
}

[HouseFunc("GameEnterMainUI")]
public class ThrowGameEnterMainUI : IHouseFuncHandler
{
    public Task<CallGSResult> Handle(CallGSContext context, string param)
    {
        // tblockGirlList is a LOCK list: HouseThrowMgr:GirlIsUnlock (HouseThrowMgr.lua:135-146) returns
        // false only for ids found in it, so the empty list means every girl is unlocked. Not a bug.
        var rsp = new JsonObject
        {
            ["FuncName"] = "GameEnterMainUI",
            ["tblockGirlList"] = new JsonArray()
        };
        return Task.FromResult(CallGSResult.Ok(HouseRequestScript.Success(rsp)));
    }
}

[HouseFunc("ThrowGameTutorialFinish")]
public class ThrowGameTutorialFinish : IHouseFuncHandler
{
    public async Task<CallGSResult> Handle(CallGSContext context, string param)
    {
        var root = HouseJson.ParseObject(param);
        if (root == null) return CallGSResult.NoResponse();

        var index = Math.Max(0, HouseJson.NumField(root, "Index"));
        var sync = new NtfSyncPlayer();
        await HouseAttr.SetAsync(context.Connection, ThrowAttr.Sid(ThrowAttr.TutorialOffset) + (uint)index, 1, sync);

        // Client sends 'ThrowGameTutorialFinish' (HouseThrowSeverMessage.lua:52-71) but the dispatcher key
        // is 'ThrowTutorialFinish' (HouseMessageHandle.lua:557); the callback ThrowTutorialFinishRsp
        // (:295-309) reads tbParam.sErr and tbParam.Index, so the request is echoed.
        return CallGSResult.Ok(HouseRequestScript.Success(HouseJson.EchoReply(root, "ThrowTutorialFinish")), sync);
    }
}

[HouseFunc("ThrowGameEnter")]
public class ThrowGameEnter : IHouseFuncHandler
{
    private static readonly Random Random = new();

    public Task<CallGSResult> Handle(CallGSContext context, string param)
    {
        var rsp = new JsonObject
        {
            ["FuncName"] = "ThrowGameEnter",
            ["nSeed"] = Random.Next(1, 1_000_000_000)
        };
        return Task.FromResult(CallGSResult.Ok(HouseRequestScript.Success(rsp)));
    }
}

[HouseFunc("ThrowGameSettlement")]
public class ThrowGameSettlement : IHouseFuncHandler
{
    public async Task<CallGSResult> Handle(CallGSContext context, string param)
    {
        var root = HouseJson.ParseObject(param);
        if (root == null) return CallGSResult.NoResponse();

        var player = context.Connection.Player!;
        var modeType = HouseJson.NumField(root, "nModeType");
        var score = Math.Max(0, HouseJson.NumField(root, "nScore"));
        var sync = new NtfSyncPlayer();

        if (root["tbUnlockDrink"] is JsonObject unlockDrink && unlockDrink["nDrinkIDs"] is JsonArray drinks)
        {
            foreach (var drinkNode in drinks)
            {
                var drinkId = HouseJson.ToInt(drinkNode);
                if (drinkId <= 0) continue;
                var sid = ThrowAttr.Sid(ThrowAttr.UnlockDrinkStartOffset) + (uint)drinkId;
                var prev = HouseAttr.Read(player, sid);
                await HouseAttr.SetAsync(context.Connection, sid, prev + 1, sync);
            }
        }

        if (modeType == ThrowAttr.ModeChallenge && score > 0)
        {
            var sid = ThrowAttr.Sid(ThrowAttr.ChallengePointsOffset);
            var prev = HouseAttr.Read(player, sid);
            if ((uint)score > prev)
                await HouseAttr.SetAsync(context.Connection, sid, (uint)score, sync);
        }

        // Client: HouseThrowSeverMessage:ThrowGameSettlementRsp (:275-292) reads nAddExp as the experience
        // of this game and shows it in the settlement UI (HouseThrowMgr.CurGameExp ->
        // umg_throw_settlement.lua:34). The same amount is what advances the level line.
        await ThrowAttr.AddExpAsync(context.Connection, player, (uint)score, sync);

        var rsp = HouseJson.EchoReply(root, "ThrowGameSettlement");
        rsp["nAddExp"] = score;
        return CallGSResult.Ok(HouseRequestScript.Success(rsp), sync);
    }
}

[HouseFunc("ThrowGameGetLevelReward")]
public class ThrowGameGetLevelReward : IHouseFuncHandler
{
    // Client: HouseThrowSeverMessage:GetLevelRewards (:174-197) sends
    // { FuncName = 'ThrowGameGetLevelReward', bAll = true } from the one-key button
    // (umg_throw_level.lua:13-17) or { ..., bAll = false, Level = n } from a single row
    // (uw_throw_level_list.lua:76-85); it shows the spinner and pre-checks
    // GetBeachAttr(20 + Level) ~= 0. The reward callback is the module's only one,
    // GetRewardRsp (:199-211) = Item.Gain(tbParam.tbReward) + HouseThrowReward refresh, reached through
    // the dispatch key 'ThrowGameGetReward' (HouseMessageHandle.lua:566) — 'ThrowGameGetLevelReward' is
    // not dispatched anywhere in Script.
    //
    // Claim state lives in a whole slot per level at offset 20 + level
    // (ItemIsGet, :107-109, and the red dot scan GetLevelAwardRed, :140-150).
    public async Task<CallGSResult> Handle(CallGSContext context, string param)
    {
        var root = HouseJson.ParseObject(param);
        var player = context.Connection.Player!;
        var sync = new NtfSyncPlayer();

        var currentLevel = ThrowAttr.GetLevel(player);
        var ladder = HouseMiniGameTables.LevelLadder(HouseMiniGameTables.ThrowRewardTable);
        var levels = new List<uint>();

        if (HouseJson.BoolField(root, "bAll"))
        {
            for (var level = 1u; level <= currentLevel; level++)
            {
                if (level > 0 && IsClaimed(player, level))
                    continue;
                levels.Add(level);
            }
        }
        else
        {
            var level = (uint)Math.Max(0, HouseJson.NumField(root, "Level"));
            if (level == 0)
                level = (uint)Math.Max(0, HouseJson.NumField(root, "nLevel"));
            if (level > 0 && level <= currentLevel && !IsClaimed(player, level))
                levels.Add(level);
            else
                Logger.GetByClassName().Warn(
                    $"Throw level reward: level {level} not claimable (current {currentLevel}) for uid={player.Uid}.");
        }

        var granted = new JsonArray();
        var gotLevel = new JsonArray();
        foreach (var level in levels)
        {
            if (!ladder.TryGetValue(level, out var conf) || conf.Rewards.Count == 0)
                continue; // no dumped ladder row: nothing to grant, the client simply closes the spinner

            var rows = new List<IReadOnlyList<uint>>();
            foreach (var row in conf.Rewards)
                rows.Add(row);

            var levelGranted = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);
            foreach (var entry in levelGranted)
            {
                if (entry != null)
                    granted.Add(entry.DeepClone());
            }

            await HouseAttr.SetAsync(context.Connection, ThrowAttr.Sid(ThrowAttr.LevelRewardGotStartOffset) + level, 1, sync);
            gotLevel.Add(JsonValue.Create(level));
        }

        if (granted.Count > 0)
        {
            DatabaseHelper.SaveDatabaseType(player.Data);
            DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
            DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
        }

        var rsp = new JsonObject { ["FuncName"] = "ThrowGameGetReward", ["tbReward"] = granted };
        // The throwing client ignores tbGetLevel (GetRewardRsp :199-211 only reads sErr/tbReward); it is
        // reported for the level list to stay symmetric with the arcade and pub replies.
        rsp["tbGetLevel"] = gotLevel;

        Logger.GetByClassName().Info(
            $"Throw level reward: claimed {gotLevel.Count} level(s), {granted.Count} row(s) granted for uid={player.Uid}.");

        return CallGSResult.Ok(HouseRequestScript.Success(rsp), sync);
    }

    private static bool IsClaimed(PlayerInstance player, uint level)
        => HouseAttr.Read(player, ThrowAttr.Sid(ThrowAttr.LevelRewardGotStartOffset) + level) != 0;
}

[HouseFunc("ThrowGameGetAchReward")]
public class ThrowGameGetAchReward : IHouseFuncHandler
{
    // Client: HouseThrowSeverMessage:GetAchReward (:219-226) sends { FuncName = 'ThrowGameGetAchReward',
    // nAchId = n }; callers are the one-key button with -1 (umg_throw_task.lua:13-19) and every single
    // quest row with its own id (umg_throw_task.lua:64-75, via Achievement.CheckAchievementReward).
    // No dispatcher entry of that name exists, and the module's only reward callback is GetRewardRsp
    // (:199-211) behind the key 'ThrowGameGetReward', so the claim is answered under that key.
    //
    // The ids are quest-group rows (AttrIds.Achievement.QuestGroupId = 7) of
    // achievement/activity_quest.json, the same rows the client lists through Achievement.GetQuestConfig;
    // the throwing table that names them is house/Throw/ThrowAchiev.json
    // (client house/Throw/ThrowAchiev.txt, HouseThrowConfig.lua:239-252).
    public async Task<CallGSResult> Handle(CallGSContext context, string param)
    {
        var root = HouseJson.ParseObject(param);
        var player = context.Connection.Player!;
        var sync = new NtfSyncPlayer();

        var raw = root == null ? 0 : HouseJson.NumField(root, "nAchId");
        if (raw == 0 && root != null)
            raw = HouseJson.NumField(root, "nId");

        var achvIds = HouseMiniGameTables.Ids(HouseMiniGameTables.ThrowAchvTable);
        var granted = new JsonArray();
        if (raw < 0)
        {
            // -1 = everything claimable. The id list is required here: without the dumped table there is
            // no way to tell which quest-group rows belong to the throwing game.
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

        var rsp = new JsonObject { ["FuncName"] = "ThrowGameGetReward", ["tbReward"] = granted };
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
