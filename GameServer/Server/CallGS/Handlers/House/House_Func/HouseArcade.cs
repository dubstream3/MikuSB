using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Achievement;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using Newtonsoft.Json.Linq;
using System.Text.Json.Nodes;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("ArcadeGameEnterMainUI")]
public class ArcadeGameEnterMainUI : IHouseFuncHandler
{
    private static readonly int[] ArcadeUnlockedGirlList = [1, 5, 13, 23, 10];

    public Task<CallGSResult> Handle(CallGSContext context, string param)
    {
        var unlockGirls = new JsonArray();
        foreach (var girlId in ArcadeUnlockedGirlList)
            unlockGirls.Add(girlId);

        var rsp = new JsonObject
        {
            ["tbUnlockGirlList"] = unlockGirls,
            ["FuncName"] = "ArcadeGameEnterMainUI"
        };
        return Task.FromResult(CallGSResult.Ok(HouseRequestScript.Success(rsp)));
    }
}

[HouseFunc("ArcadeGameEnter")]
public class ArcadeGameEnter : IHouseFuncHandler
{
    private static readonly Random Random = new();

    public Task<CallGSResult> Handle(CallGSContext context, string param)
    {
        var rsp = new JsonObject
        {
            ["FuncName"] = "ArcadeGameEnter",
            ["nSeed"] = Random.Next(1, 1_000_000_000)
        };
        return Task.FromResult(CallGSResult.Ok(HouseRequestScript.Success(rsp)));
    }
}

[HouseFunc("ArcadeGameSettlement")]
public class ArcadeGameSettlement : IHouseFuncHandler
{
    private const uint HouseArcadeInfoStart = 18000;
    private const uint ArcadeAttrExpOffset = 2;
    private const uint ArcadeAttrEndlessScoreOffset = 4;
    private const uint ArcadeAttrGirlEndlessModeStateOffset = 5;
    // Offsets 6..9 are not used by the client (HouseArcadeToSeverMsg.lua:8-21 only declares 1-5,
    // 10-35, 36-55 and 56-250), so 6 is free for the server-side replay guard below.
    private const uint ArcadeAttrLastSettleSeedOffset = 6;
    private const uint ArcadeAttrGirlNormalModeStateOffset = 10;
    private const uint ArcadeAttrGirlNormalModeStateEndOffset = 35;
    private const uint ArcadeAttrConditionValStartOffset = 36;
    private const uint ArcadeAttrConditionValEndOffset = 55;
    private const uint ArcadeAttrPropUseStartOffset = 56;
    // The client declares this window up to offset 250 (HouseArcadeToSeverMsg.lua:20, Attr_PropUseTimeEnd)
    // but every read goes through HouseStorage.GetArcadeAttr (HouseStorage.lua:369-378), which returns 0 as
    // soon as HouseArcadeStart + offset > HouseArcadeEnd = 18200 (:139) — i.e. for any offset above 200.
    // Slots 201..250 (sids 18201..18250) would therefore be write-only: the prop counts stored there are
    // permanently read back as 0, and GetPropUseTime (:141-156) breaks out of its sequential scan at the
    // first zero, truncating every prop after the hole. The window is clamped to what the client can read.
    private const uint ArcadeAttrPropUseEndOffset = 200;
    private const int ArcadeGameModeNormal = 1;
    private const int ArcadeGameModeEndless = 2;

    public async Task<CallGSResult> Handle(CallGSContext context, string param)
    {
        var root = HouseJson.ParseObject(param);
        if (root == null) return CallGSResult.NoResponse();
        var player = context.Connection.Player!;
        var sync = new NtfSyncPlayer();
        var modeType = HouseJson.NumField(root, "nModeType");
        var finishRound = Math.Max(0, HouseJson.NumField(root, "nFinishRound"));
        var maxScore = Math.Max(0, HouseJson.NumField(root, "nMaxScore"));

        // The client can legitimately send the same settlement more than once:
        // HouseArcadeToSeverMsg:GameSettlement (HouseArcadeToSeverMsg.lua:332-351) caches the command
        // and replays it one second after Event.ReconnectSuccess, and that replay is independent of the
        // automatic send that follows the result cinematic. Everything below is additive (exp, per-girl
        // finish rounds, prop-use counters), so a replay would double the rewards. nSeed is the round
        // identity handed out by ArcadeGameEnter, so it is used as the replay key.
        var seed = HouseJson.NumField(root, "nSeed");
        var seedSid = HouseArcadeInfoStart + ArcadeAttrLastSettleSeedOffset;
        if (seed != 0 && HouseAttr.Read(player, seedSid) == (uint)seed)
        {
            Logger.GetByClassName().Warn(
                $"Arcade settlement: seed {seed} already settled for uid={player.Uid}, replay ignored.");
            var replay = new JsonObject { ["nAddExp"] = 0, ["FuncName"] = "ArcadeGameSettlement" };
            return CallGSResult.Ok(HouseRequestScript.Success(replay), sync);
        }
        if (seed != 0)
            await HouseAttr.SetAsync(context.Connection, seedSid, (uint)seed, sync);

        if (root["tbConditionVal"] is JsonArray conditions)
        {
            foreach (var conditionNode in conditions)
            {
                if (conditionNode is not JsonArray condition || condition.Count < 2) continue;
                var id = HouseJson.ToInt(condition[0]);
                var value = Math.Max(0, HouseJson.ToInt(condition[1]));
                if (id < 0) continue;
                var sid = HouseArcadeInfoStart + ArcadeAttrConditionValStartOffset + (uint)id;
                if (sid > HouseArcadeInfoStart + ArcadeAttrConditionValEndOffset) continue;
                var prev = HouseAttr.Read(player, sid);
                if ((uint)value > prev)
                    await HouseAttr.SetAsync(context.Connection, sid, (uint)value, sync);
            }
        }

        if (root["tbPropUse"] is JsonArray propUses)
        {
            var slotStart = HouseArcadeInfoStart + ArcadeAttrPropUseStartOffset;
            var slotEnd = HouseArcadeInfoStart + ArcadeAttrPropUseEndOffset;
            var slotState = new Dictionary<uint, ArcadePropUseSlot>();
            var emptySlots = new Queue<uint>();
            for (uint sid = slotStart; sid <= slotEnd; sid++)
            {
                var value = HouseAttr.Read(player, sid);
                if (value == 0)
                {
                    emptySlots.Enqueue(sid);
                    continue;
                }

                slotState[sid] = new ArcadePropUseSlot
                {
                    Type = (byte)(value & 0xff),
                    Id = (byte)((value >> 8) & 0xff),
                    Count = (ushort)((value >> 16) & 0xffff)
                };
            }

            foreach (var propUseNode in propUses)
            {
                if (propUseNode is not JsonArray propUse || propUse.Count < 3) continue;
                var type = HouseJson.ToInt(propUse[0]) & 0xff;
                var id = HouseJson.ToInt(propUse[1]) & 0xff;
                var count = HouseJson.ToInt(propUse[2]);
                if (count <= 0) continue;

                uint? foundSid = null;
                foreach (var (sid, slot) in slotState)
                {
                    if (slot.Type == type && slot.Id == id)
                    {
                        foundSid = sid;
                        break;
                    }
                }

                if (foundSid != null)
                {
                    var slot = slotState[foundSid.Value];
                    slot.Count = (ushort)Math.Min(0xffff, slot.Count + count);
                    slotState[foundSid.Value] = slot;
                    await HouseAttr.SetAsync(context.Connection, foundSid.Value, HouseAttr.PackArcadePropUse(slot.Type, slot.Id, slot.Count), sync);
                }
                else if (emptySlots.Count > 0)
                {
                    var sid = emptySlots.Dequeue();
                    var packedCount = (ushort)Math.Min(0xffff, count);
                    slotState[sid] = new ArcadePropUseSlot { Type = type, Id = id, Count = packedCount };
                    await HouseAttr.SetAsync(context.Connection, sid, HouseAttr.PackArcadePropUse(type, id, packedCount), sync);
                }
            }
        }

        var girlList = root["tbGirlDataList"] as JsonArray;
        var nAddExp = 0;
        if (modeType == ArcadeGameModeNormal)
        {
            if (girlList != null)
            {
                foreach (var girlNode in girlList)
                {
                    var girlId = HouseJson.ToInt(girlNode);
                    if (girlId <= 0) continue;
                    var sid = HouseArcadeInfoStart + ArcadeAttrGirlNormalModeStateOffset + (uint)girlId;
                    if (sid > HouseArcadeInfoStart + ArcadeAttrGirlNormalModeStateEndOffset) continue;
                    var prev = HouseAttr.Read(player, sid);
                    await HouseAttr.SetAsync(context.Connection, sid, prev + (uint)finishRound, sync);
                }
            }
            nAddExp = finishRound * 10;
            AdvanceCoinPusherAchievements(player, sync);
        }
        else if (modeType == ArcadeGameModeEndless)
        {
            var scoreSid = HouseArcadeInfoStart + ArcadeAttrEndlessScoreOffset;
            var prevScore = HouseAttr.Read(player, scoreSid);
            if ((uint)maxScore > prevScore)
                await HouseAttr.SetAsync(context.Connection, scoreSid, (uint)maxScore, sync);

            var bitsSid = HouseArcadeInfoStart + ArcadeAttrGirlEndlessModeStateOffset;
            var bits = HouseAttr.Read(player, bitsSid);
            var originalBits = bits;
            if (girlList != null)
            {
                foreach (var girlNode in girlList)
                {
                    var girlId = HouseJson.ToInt(girlNode);
                    if (girlId is > 0 and < 31)
                        bits |= 1u << girlId;
                }
            }
            if (bits != originalBits)
                await HouseAttr.SetAsync(context.Connection, bitsSid, bits, sync);

            nAddExp = maxScore / 10;
        }

        if (nAddExp > 0)
        {
            var expSid = HouseArcadeInfoStart + ArcadeAttrExpOffset;
            var prevExp = HouseAttr.Read(player, expSid);
            await HouseAttr.SetAsync(context.Connection, expSid, prevExp + (uint)nAddExp, sync);
        }

        var rsp = new JsonObject
        {
            ["nAddExp"] = nAddExp,
            ["FuncName"] = "ArcadeGameSettlement"
        };
        return CallGSResult.Ok(HouseRequestScript.Success(rsp), sync);
    }

    /// <summary>
    /// Feeds the coin pusher's own achievements from state this handler already keeps.
    ///
    /// house/coinpusher/coinpusher_achv.json lists 20 achievement rows; the only trigger type this
    /// server can evaluate is AchType 388 = "play N rounds in normal mode with girl X", whose Param is
    /// [girlId, rounds] (activity_quest.json 24001-24005: Param [1,5] [5,5] [10,5] [13,5] [23,5]; the
    /// same Param shape appears on the DLC-board twins 21272-21274, named
    /// achievement_dlc.Name35_8/9/10 = 「在普通模式中完成5回合」). The per-girl round counter is the
    /// (18000, 10 + girlId) slot written a few lines above.
    ///
    /// Progress is stored binary (0 / CheckValue) because the client renders progress / CheckValue and
    /// every coinpusher row has CheckValue = 1. Types 389-396 (cumulative score / rounds / per-mode
    /// counts) need trigger definitions this server has no evidence for yet, so they stay untouched.
    /// </summary>
    private static void AdvanceCoinPusherAchievements(PlayerInstance player, NtfSyncPlayer sync)
    {
        const uint girlRoundAchType = 388;

        foreach (var achv in GameData.CoinPusherAchvData.Values)
        {
            if (achv.AchType != girlRoundAchType)
                continue;

            if (!GameData.ActivityQuestData.TryGetValue(achv.Id, out var quest))
                continue;

            if (quest.Param is not JArray param || param.Count < 2)
                continue;

            var girlId = ToUint(param[0]);
            var rounds = ToUint(param[1]);
            if (girlId == 0 || rounds == 0)
                continue;

            var sid = HouseArcadeInfoStart + ArcadeAttrGirlNormalModeStateOffset + girlId;
            if (HouseAttr.Read(player, sid) < rounds)
                continue;

            AchievementClaimService.Complete(
                player, AchievementClaimService.QuestGroupId, quest.Id, quest.CheckValue, sync);
        }
    }

    /// <summary>
    /// Newtonsoft-side sibling of <see cref="HouseJson.ToInt"/> — the config tables are loaded through
    /// Newtonsoft, so their rows hand out <see cref="JToken"/> rather than System.Text.Json nodes.
    /// </summary>
    private static uint ToUint(JToken? token)
    {
        var text = token?.ToString().Trim().Trim('[', ']', '"');
        return uint.TryParse(text, out var value) ? value : 0;
    }

    private struct ArcadePropUseSlot
    {
        public int Type { get; set; }
        public int Id { get; set; }
        public ushort Count { get; set; }
    }
}

[HouseFunc("ArcadeGameLogSettlement")]
public class ArcadeGameLogSettlement : IHouseFuncHandler
{
    public Task<CallGSResult> Handle(CallGSContext context, string param)
    {
        var rsp = new JsonObject { ["FuncName"] = "ArcadeGameLogSettlement" };
        return Task.FromResult(CallGSResult.Ok(HouseRequestScript.Success(rsp)));
    }
}

[HouseFunc("ArcadeGameGetLevelReward")]
public class ArcadeGameGetLevelReward : IHouseFuncHandler
{
    private const uint HouseArcadeInfoStart = 18000;
    private const uint ArcadeAttrExpOffset = 2;
    private const uint ArcadeAttrLevelRewardGotOffset = 3;

    // Client side origin: HouseArcadeToSeverMsg:GetReward (HouseArcadeToSeverMsg.lua:268-275) sends
    // { FuncName = 'ArcadeGameGetLevelReward', nLevel = nLevel } and shows the spinner; the answer is
    // routed to GetRewardRsp (:493-502), which does Item.Gain(tbParam.tbReward) and then forwards the
    // whole reply to the HouseArcadeReward UI. Item.Gain (Item/Item.lua:639) needs a LIST of
    // [G,D,P,L,Count] rows, which is exactly what RewardManager.GrantConfiguredRewardsAsync returns,
    // so tbReward is passed straight through. A missing tbReward grants nothing; sErr surfaces as a tip.
    //
    // The claim is recorded in the bitfield the client reads back in
    // HouseArcadeToSeverMsg:CheckLevelRewardGot (:200-206): arcade offset 3, bit = level (1-based).
    //
    // nLevel <= 0 is the one-key button: umg_arcade_level.lua:12-14 / :55-61 GetRewardAll calls
    // HouseArcadeToSeverMsg:GetReward(-1) to claim every reached-but-still-unclaimed level at once, so it
    // must be treated as "claim all", not as an unknown level.
    //
    // tbGetLevel (the list of levels claimed by THIS reply) is mandatory: umg_arcade_level.lua:63-64
    // returns immediately when the reply carries no tbGetLevel, and the per-row badge only recomputes on
    // the ON_STATE_CHANGE fired by the loop at :71-77 (uw_arcade_levelitem.lua:51-64 RefreshState). Without
    // it the level list keeps showing rows as unclaimed after a successful claim.
    public async Task<CallGSResult> Handle(CallGSContext context, string param)
    {
        var root = HouseJson.ParseObject(param);
        if (root == null) return CallGSResult.NoResponse();

        var player = context.Connection.Player!;
        var requested = HouseJson.NumField(root, "nLevel");
        var rsp = new JsonObject { ["FuncName"] = "ArcadeGameGetLevelReward" };

        var sync = new NtfSyncPlayer();
        var bitsSid = HouseArcadeInfoStart + ArcadeAttrLevelRewardGotOffset;
        var claimedBits = HouseAttr.Read(player, bitsSid);

        // Mirror HouseArcadeToSeverMsg:GetLevel (:174-197): walk the ladder accumulating Exp so a
        // stale or forged nLevel cannot unlock rewards the player has not actually reached.
        var reachedLevel = ResolveReachedLevel(HouseAttr.Read(player, HouseArcadeInfoStart + ArcadeAttrExpOffset));

        var levels = new List<uint>();
        if (requested <= 0)
        {
            for (var level = 1u; level <= reachedLevel; level++)
            {
                if (GameData.CoinPusherRewardData.ContainsKey(level) && (claimedBits >> (int)level & 1u) == 0)
                    levels.Add(level);
            }
        }
        else if (GameData.CoinPusherRewardData.ContainsKey((uint)requested)
                 && (claimedBits >> requested & 1u) == 0
                 && (uint)requested <= reachedLevel)
        {
            levels.Add((uint)requested);
        }
        else
        {
            Logger.GetByClassName().Warn(
                $"Arcade level reward: level {requested} not claimable (reached {reachedLevel}) for uid={player.Uid}.");
        }

        var granted = new JsonArray();
        var gotLevel = new JsonArray();
        foreach (var level in levels)
        {
            var conf = GameData.CoinPusherRewardData[level];
            var rows = new List<IReadOnlyList<uint>>();
            if (conf.RewardRow != null)
                rows.Add(conf.RewardRow);

            var levelGranted = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);
            foreach (var entry in levelGranted)
            {
                if (entry != null)
                    granted.Add(entry.DeepClone());
            }

            claimedBits |= 1u << (int)level;
            gotLevel.Add(JsonValue.Create(level));
        }

        if (gotLevel.Count > 0)
        {
            await HouseAttr.SetAsync(context.Connection, bitsSid, claimedBits, sync);

            DatabaseHelper.SaveDatabaseType(player.Data);
            DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
            DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
        }

        rsp["tbReward"] = granted;
        rsp["tbGetLevel"] = gotLevel;

        Logger.GetByClassName().Info(
            $"Arcade level reward: requested {requested}, claimed {gotLevel.Count} level(s), {granted.Count} row(s) granted for uid={player.Uid}.");

        return CallGSResult.Ok(HouseRequestScript.Success(rsp), sync);
    }

    private static uint ResolveReachedLevel(uint exp)
    {
        // The shipped ladder is contiguous 1..15, but a gap would otherwise throw on the index
        // below, so walk only while the row exists (the client's ipairs stops at the first nil too).
        var maxLevel = 0u;
        while (GameData.CoinPusherRewardData.ContainsKey(maxLevel + 1))
            maxLevel++;

        if (maxLevel == 0)
            return 1;

        var level = 1u;
        var leftExp = exp;
        for (var i = 1u; i <= maxLevel; i++)
        {
            if (!GameData.CoinPusherRewardData.TryGetValue(i, out var conf))
                break;

            var needExp = i == maxLevel ? 0u : conf.Exp;
            if (leftExp < needExp)
                break;

            leftExp -= needExp;
            level = i == maxLevel ? maxLevel : i + 1;
        }

        return level;
    }
}

[HouseFunc("ArcadeGameGetAchReward")]
public class ArcadeGameGetAchReward : IHouseFuncHandler
{
    // Client: HouseArcadeToSeverMsg:GetAchReward(nId) (HouseArcadeToSeverMsg.lua:277-285) sends
    // {nAchId}; the one-key button (umg_arcade_task.lua:13-19) sends -1 to mean "everything
    // claimable". The callback (:505-514) does Item.Gain(tbParam.tbReward) and refreshes the panel.
    //
    // The ids it names are quest-group (AttrIds.Achievement.QuestGroupId = 7) rows of
    // achievement/activity_quest.json — the very rows umg_arcade_task.lua lists through
    // Achievement.GetQuestConfig (Achievement/Achievement.lua:191) and CheckAchievementReward.
    public async Task<CallGSResult> Handle(CallGSContext context, string param)
    {
        var root = HouseJson.ParseObject(param);
        var player = context.Connection.Player!;
        var sync = new NtfSyncPlayer();
        var raw = root == null ? 0 : HouseJson.NumField(root, "nAchId");
        if (raw == 0 && root != null)
            raw = HouseJson.NumField(root, "nId");

        var granted = new JsonArray();
        if (raw < 0)
        {
            foreach (var achv in GameData.CoinPusherAchvData.Values)
            {
                if (!GameData.ActivityQuestData.TryGetValue(achv.Id, out var row))
                    continue;

                var claimedRows = await AchievementClaimService.TryClaimAsync(
                    player,
                    AchievementClaimService.QuestGroupId,
                    row.Id,
                    row.CheckValue,
                    AchievementClaimService.ReadRewardRows(row.Rewards),
                    sync);
                if (claimedRows == null)
                    continue;

                foreach (var entry in claimedRows)
                    granted.Add(entry?.DeepClone());
            }
        }
        else if (raw > 0 && GameData.ActivityQuestData.TryGetValue((uint)raw, out var quest))
        {
            var claimedRows = await AchievementClaimService.TryClaimAsync(
                player,
                AchievementClaimService.QuestGroupId,
                quest.Id,
                quest.CheckValue,
                AchievementClaimService.ReadRewardRows(quest.Rewards),
                sync);
            if (claimedRows != null)
                granted = claimedRows;
        }

        var rsp = new JsonObject { ["FuncName"] = "ArcadeGameGetAchReward" };
        if (granted.Count > 0)
            rsp["tbReward"] = granted;

        return CallGSResult.Ok(HouseRequestScript.Success(rsp), sync);
    }
}
