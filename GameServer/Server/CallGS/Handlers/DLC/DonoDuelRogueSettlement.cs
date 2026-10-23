using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

/// <summary>
/// 多诺-巅峰对决 抓捕玩法 (rogue mode) settlements. Both go out over the shared reconnect channel
/// (Misc/Reconnect.lua Reconnect.Send_SettleInfo) as Chapter_DealLevelSettlement with
/// sCmd = "DonoDuelRogue_RoomSettlement" / "DonoDuelRogue_LevelSettlement"
/// (DLC/DonoDuel/DonoDuelRogue.lua:703 and :764), so they are handled from
/// Chapter_DealLevelSettlement.BuildSettlementPayloadAsync rather than as their own CallGS APIs.
///
/// Client expectations:
///   RoomSettlement  (DonoDuelRogue.lua:771-798): pushes every tbAward.tbHeroCard id into the in-run
///                   fight bag (AddFightBagItem) and opens the DonoduelDrop panel when tbAward exists.
///   LevelSettlement (DonoDuelRogue.lua:715-726): opens DonoDuelSettlement with tbAward, whose OnOpen
///                   reads tbAward.tbCash for the run currency.
/// The room card pools live in dlc/DonoDuel/donoduel_catch_room_heropool.json (matched by the
/// (Phase, RoomType) of the settled room via donoduel_catch_battle.json + DonoRogue/
/// donorogue_level.json): the client fills the room's draft from them, but nothing in the client
/// ever reports the caught cards back - tbCatchHeroPool is loaded and never used and the catch
/// Lua is C++-side - so the server rolls the catch itself. Without this a finished room granted
/// nothing (tbIdList only mirrors the deck the player entered with) and the drop panel stayed
/// closed. The pool's Currency column is credited for real through PlayerInstance.AddCurrency;
/// unfinished rooms (bailed mid-room) settle currency and cards at zero.
/// </summary>
internal static class DonoDuelRogueSettlement
{
    private static readonly Logger Logger = new("DonoDuelRogue");

    /// <summary>The exploration currency (money type 63, the type the drop panel falls back to).</summary>
    private const uint RoomCurrencyType = 63;

    public static ValueTask<(JsonNode Payload, NtfSyncPlayer? Sync)> HandleRoomSettlementAsync(
        PlayerInstance player,
        JsonNode? tbParam)
    {
        var roomId = ReadUint(tbParam, "nRoomId");
        var catchId = ReadUint(tbParam, "nCatchId");
        var unfinished = ReadBool(tbParam, "bUnfinished");
        var sync = new NtfSyncPlayer();

        // tbIdList is DonoDuelRogue:GetFightBagTmpIdList() (DLC/DonoDuel/DonoDuelRogue.lua:499-513):
        // the hero templates the in-run bag holds at this point. Every template the deck does not own
        // yet was picked up in this room, so it is allocated here and handed back as block ids - the
        // client pushes them straight back into the in-run bag and opens the drop panel with them
        // (:771-798). Without this the deck never grew: the mode kept its cards to itself and every
        // exploration ended with cards that no longer existed.
        var picked = new List<(uint TemplateId, uint Star, uint Quality)>();
        foreach (var templateId in ReadUintList(tbParam, "tbIdList"))
        {
            if (templateId != 0 && !DonoDuelBag.HasTemplate(player, templateId))
                picked.Add((templateId, 1, 1));
        }

        var granted = DonoDuelBag.GrantCards(player, picked, sync);
        var rolled = 0;

        // The room's own catch: nCatchId is set C++-side per room; the pool row is matched by the
        // catch's Phase and the room's RoomType exactly like the client's GetHeroPool. Rolls one
        // card (star/quality by the pool weights, template uniform over the 20 hero cards - bonds
        // are not card properties so BondWeight cannot select a card) plus the CatchID 2 guide
        // rewards, and credits the pool currency for real.
        var currencyType = RoomCurrencyType;
        var currencyAmount = 0u;
        if (!unfinished && catchId != 0 &&
            GameData.DonoDuelCatchBattleData.TryGetValue(catchId, out var battle))
        {
            var roomType = GameData.DonoRogueLevelData.TryGetValue(roomId, out var level)
                ? level.RoomType
                : 0;
            DonoDuelCatchRoomHeropoolExcel? pool = null;
            foreach (var row in GameData.DonoDuelHeropoolData.Values)
            {
                if (row.Phase == battle.Phase && row.RoomType == roomType)
                {
                    pool = row;
                    break;
                }
            }

            if (pool?.Currency.Count >= 2 && pool.Currency[1] != 0)
            {
                currencyType = pool.Currency[0] != 0 ? pool.Currency[0] : RoomCurrencyType;
                currencyAmount = pool.Currency[1];
                player.AddCurrency(currencyType, currencyAmount, sync);
            }

            if (pool != null && pool.Number > 0)
            {
                var roll = RollCatch(pool);
                if (roll.TemplateId != 0)
                {
                    var ids = DonoDuelBag.GrantCards(player, [roll], sync);
                    granted.AddRange(ids);
                    rolled = ids.Count;
                }
            }

            // Guide room (CatchID 2): Guide_Reward2 tops the deck up to three [1, 1, 1] cards -
            // star-up material must match template and star, so a deck that already holds three
            // gets nothing and the room is not farmable. The currency top-up rides along only
            // when a card was actually granted.
            var guideGranted = 0;
            foreach (var row in battle.GuideReward2)
            {
                if (row.Count < 1 || row[0] == 0)
                    continue;

                var star = row.Count > 1 && row[1] != 0 ? row[1] : 1;
                var quality = row.Count > 2 && row[2] != 0 ? row[2] : 1;
                var missing = 3 - (int)DonoDuelBag.CountTemplateStar(player, row[0], star);
                for (var i = 0; i < missing; i++)
                {
                    var ids = DonoDuelBag.GrantCards(player, [(row[0], star, quality)], sync);
                    if (ids.Count == 0)
                        break;

                    granted.AddRange(ids);
                    guideGranted += ids.Count;
                }
            }

            if (guideGranted > 0 && battle.GuideReward1.Count >= 2 && battle.GuideReward1[1] != 0)
            {
                var guideType = battle.GuideReward1[0] != 0 ? battle.GuideReward1[0] : RoomCurrencyType;
                player.AddCurrency(guideType, battle.GuideReward1[1], sync);
            }

            if (granted.Count > 0)
                DonoDuelBag.Save(player);
        }
        else if (granted.Count > 0)
        {
            DonoDuelBag.Save(player);
        }

        // Mission 103 ("前往获取多诺卡") only asks for the first exploration stage, so any settled
        // room satisfies it; the run-finish variants (108) are advanced by the level settlement.
        var advanced = DonoDuelTaskState.AdvanceRogueMissions(player, 1, 1, runFinished: false, sync);
        if (advanced > 0)
            DonoDuelTaskState.Save(player);

        // The drop panel indexes tbAward.tbCash[1]/[2] without a nil check
        // (UMG/DonoDuel/umg_donoduel_drop.lua:32-35), so the room currency is always sent - with
        // the amount actually credited (zero on bailed rooms).
        var cash = new JsonArray();
        cash.Add(currencyType);
        cash.Add(currencyAmount);

        var heroCard = new JsonArray();
        foreach (var uniqueId in granted)
            heroCard.Add(uniqueId);

        Logger.Info(
            $"Donoduel rogue room {roomId} settled for uid={player.Uid} (unfinished={unfinished}): " +
            $"{granted.Count} card(s) allocated from {picked.Count} reported template(s), " +
            $"{rolled} rolled from the pool, {currencyAmount} currency ({currencyType}), " +
            $"{advanced} rogue mission(s) advanced.");

        var payload = new JsonObject
        {
            ["tbAward"] = new JsonObject
            {
                ["tbCash"] = cash,
                ["tbHeroCard"] = heroCard,
            },
        };

        return ValueTask.FromResult<(JsonNode, NtfSyncPlayer?)>((payload, sync));
    }

    public static ValueTask<(JsonNode Payload, NtfSyncPlayer? Sync)> HandleLevelSettlementAsync(
        PlayerInstance player,
        JsonNode? tbParam)
    {
        var roomId = ReadUint(tbParam, "nRoomId");
        var levelId = ReadUint(tbParam, "nLevelID");
        var failed = ReadBool(tbParam, "bFail");

        var sync = new NtfSyncPlayer();

        // DonoDuel:GetRogueCount() -- attribute (185, 5), "肉鸽战斗次数"; it is echoed back as the
        // RoundID of the next run, so finished runs are counted here.
        if (!failed)
        {
            var attr = player.Attributes.GetOrCreate(AttrIds.DonoDuel.GroupId, AttrIds.DonoDuel.RogueCountSid);
            attr.Val += 1;
            player.Attributes.SyncTo(sync, attr);
            DatabaseHelper.SaveDatabaseType(player.Data);
        }

        // The run-finish objective of the rogue missions: mission 108 ("完成1次多诺卡探索") carries
        // Param [-1, 1], which is advanced only when a whole exploration was completed.
        var advanced = DonoDuelTaskState.AdvanceRogueMissions(player, 1, 1, runFinished: !failed, sync);
        if (advanced > 0)
            DonoDuelTaskState.Save(player);

        Logger.Info(
            $"Donoduel rogue level {levelId} room {roomId} settled for uid={player.Uid} " +
            $"(failed={failed}); run counter is now " +
            $"{player.Attributes.GetValue(AttrIds.DonoDuel.GroupId, AttrIds.DonoDuel.RogueCountSid)}.");

        // The panel always needs tbAward (it reads tbAward.tbCash and falls back to the in-run money
        // the client accumulated), so an empty table is sent rather than nothing at all.
        return ValueTask.FromResult<(JsonNode, NtfSyncPlayer?)>(
            (new JsonObject { ["tbAward"] = new JsonObject() }, sync));
    }

    /// <summary>
    /// Rolls the room's own catch from its heropool row: star and quality by weight (the list index
    /// is the value, so index + 1 wins), template uniform over the hero cards.
    /// </summary>
    private static (uint TemplateId, uint Star, uint Quality) RollCatch(DonoDuelCatchRoomHeropoolExcel pool)
    {
        var templates = GameData.DonoDuelHeroCardData.Keys.ToList();
        if (templates.Count == 0)
            return (0, 0, 0);

        return (
            templates[Random.Shared.Next(templates.Count)],
            RollWeight(pool.StarWeight),
            RollWeight(pool.QualityWeight));
    }

    private static uint RollWeight(IReadOnlyList<uint> weights)
    {
        var total = 0u;
        foreach (var weight in weights)
            total += weight;

        if (total == 0)
            return 1;

        var roll = (uint)Random.Shared.Next((int)Math.Min(total, int.MaxValue));
        for (var i = 0; i < weights.Count; i++)
        {
            if (roll < weights[i])
                return (uint)i + 1;

            roll -= weights[i];
        }

        return 1;
    }

    private static uint ReadUint(JsonNode? node, string key)
    {
        if (node is not JsonObject obj)
            return 0;

        var raw = obj[key]?.ToString().Trim().Trim('[', ']', '"');
        return uint.TryParse(raw, out var value) ? value : 0;
    }

    private static List<uint> ReadUintList(JsonNode? node, string key)
    {
        var result = new List<uint>();
        if (node is not JsonObject obj || obj[key] is not JsonArray array)
            return result;

        foreach (var entry in array)
        {
            if (uint.TryParse(entry?.ToString().Trim().Trim('[', ']', '"'), out var value) && value != 0)
                result.Add(value);
        }

        return result;
    }

    private static bool ReadBool(JsonNode? node, string key)
    {
        if (node is not JsonObject obj)
            return false;

        var raw = obj[key]?.ToString().Trim();
        return raw is "true" or "True" or "1";
    }
}
