using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Achievement;
using MikuSB.GameServer.Game.BagRogue;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BagRogue;

// The item / store / quest half of 背包肉鸽. None of these drive the route state machine -- they run inside
// a level or in a side panel -- so the shape of each reply is what matters (the keys the client echoes back
// into its own widgets) and the state they touch is small.

/// <summary>BagRogue_SellItem -- the reply's nUniqueId feeds BackpackBag:OnSellItem (BagRogue.lua:1442).</summary>
[CallGSApi("BagRogue_SellItem")]
public class BagRogue_SellItem : CallGSHandler<BagRogueSellParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, BagRogueSellParam request) =>
        Task.FromResult(CallGSResult.Ok(new JsonObject { ["nUniqueId"] = request.UniqueId }));
}

/// <summary>
/// BagRogue_UpgradeItem -- both ids are echoed into RemoveItem calls (uw_backpack_bag_jigsaw.lua:564-567);
/// dropping either one turns the widget into RemoveItem(nil).
/// </summary>
[CallGSApi("BagRogue_UpgradeItem")]
public class BagRogue_UpgradeItem : CallGSHandler<BagRogueUpgradeParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, BagRogueUpgradeParam request) =>
        Task.FromResult(CallGSResult.Ok(new JsonObject
        {
            ["nUniqueA"] = request.UniqueA,
            ["nUniqueB"] = request.UniqueB
        }));
}

/// <summary>
/// BagRogue_EquipBag -- writes 61/2 as {"A&lt;uId&gt;": [pos, dir]}. The client validates collisions locally
/// (BagRogue.lua:1958-2007) and only sends the diff, so the server applies the diff to its own copy.
/// </summary>
[CallGSApi("BagRogue_EquipBag")]
public class BagRogue_EquipBag : CallGSHandler<BagRogueEquipParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, BagRogueEquipParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var json = player.Attributes.GetStringValue(
            AttrIds.BagRogue.StringGroupId, AttrIds.BagRogue.StringEquipBagSid) ?? "{}";
        var bag = JsonNode.Parse(json) as JsonObject ?? new JsonObject();

        foreach (var slot in request.List ?? [])
        {
            if (slot.UniqueId == 0)
                continue;

            if (slot.Remove)
                bag.Remove($"A{slot.UniqueId}");
            else
                bag[$"A{slot.UniqueId}"] = new JsonArray(slot.Pos, slot.Dir);
        }

        var sync = new NtfSyncPlayer();
        var attr = player.Attributes.SetString(
            AttrIds.BagRogue.StringGroupId, AttrIds.BagRogue.StringEquipBagSid, bag.ToJsonString());
        player.Attributes.SyncTo(sync, attr);
        DatabaseHelper.SaveDatabaseType(player.Data);

        return Task.FromResult(CallGSResult.Ok(new JsonObject(), sync));
    }
}

/// <summary>
/// BagRogue_SaveItemToExternStorage -- moves one item into the 30-slot external storage (142,401..490).
/// The reply's tbExternItem only lights the ready red dot, so an empty object is enough for now; the slot
/// write itself is the follow-up listed in 059's leftovers.
/// </summary>
[CallGSApi("BagRogue_SaveItemToExternStorage")]
public class BagRogue_SaveItemToExternStorage : CallGSHandler<BagRogueSaveExternParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, BagRogueSaveExternParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        // The only client-visible state here is "there is something in the external storage".
        var sync = new NtfSyncPlayer();
        var count = player.Attributes.GetValue(
            AttrIds.BagRogue.GroupId, AttrIds.BagRogue.CurrentStockroomNumSid);
        if (count > 0)
        {
            var attr = player.Attributes.Set(
                AttrIds.BagRogue.GroupId, AttrIds.BagRogue.CurrentStockroomNumSid, count - 1);
            player.Attributes.SyncTo(sync, attr);
            DatabaseHelper.SaveDatabaseType(player.Data);
        }

        return Task.FromResult(CallGSResult.Ok(new JsonObject(), sync));
    }
}

/// <summary>BagRogue_RemoveExternStorageItem -- nUniqueId goes straight into OnDiscardRsp (BagRogue.lua:1777).</summary>
[CallGSApi("BagRogue_RemoveExternStorageItem")]
public class BagRogue_RemoveExternStorageItem : CallGSHandler<BagRogueSellParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, BagRogueSellParam request) =>
        Task.FromResult(CallGSResult.Ok(new JsonObject { ["nUniqueId"] = request.UniqueId }));
}

/// <summary>BagRogue_SelectExternStorageItem -- packs up to five unique ids into (142,16), 6 bits each.</summary>
[CallGSApi("BagRogue_SelectExternStorageItem")]
public class BagRogue_SelectExternStorageItem : CallGSHandler<BagRogueSelectExternParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, BagRogueSelectExternParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var packed = 0u;
        var unique = request.UniqueIds ?? [];
        for (var i = 0; i < unique.Count && i < 5; i++)
            packed |= (unique[i] & 0x3Fu) << (i * 6);

        var sync = new NtfSyncPlayer();
        var attr = player.Attributes.Set(
            AttrIds.BagRogue.GroupId, AttrIds.BagRogue.ChosenExternalItemUniqueSid, packed);
        player.Attributes.SyncTo(sync, attr);
        DatabaseHelper.SaveDatabaseType(player.Data);

        return Task.FromResult(CallGSResult.Ok(new JsonObject(), sync));
    }
}

/// <summary>BagRogue_SaveTrialCard -- (142,9..11) hold the three trial character ids; reply is unused.</summary>
[CallGSApi("BagRogue_SaveTrialCard")]
public class BagRogue_SaveTrialCard : CallGSHandler<BagRogueTrialCardParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, BagRogueTrialCardParam request)
    {
        var player = context.Connection.Player;
        if (player == null || request.Trials is not { Count: > 0 })
            return Task.FromResult(CallGSResult.Ok());

        var sids = new[]
        {
            AttrIds.BagRogue.TrialCard1Sid,
            AttrIds.BagRogue.TrialCard2Sid,
            AttrIds.BagRogue.TrialCard3Sid
        };

        var sync = new NtfSyncPlayer();
        for (var i = 0; i < sids.Length && i < request.Trials.Count; i++)
        {
            var attr = player.Attributes.Set(AttrIds.BagRogue.GroupId, sids[i], request.Trials[i]);
            player.Attributes.SyncTo(sync, attr);
        }

        DatabaseHelper.SaveDatabaseType(player.Data);
        return Task.FromResult(CallGSResult.Ok(new JsonObject(), sync));
    }
}

/// <summary>BagRogue_DoCraftItem -- only bumps the per-node craft counter (142,17); the widget refreshes itself.</summary>
[CallGSApi("BagRogue_DoCraftItem")]
public class BagRogue_DoCraftItem : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player;
        if (player == null)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var sync = new NtfSyncPlayer();
        var attr = player.Attributes.Add(
            AttrIds.BagRogue.GroupId, AttrIds.BagRogue.CurrentCraftNumSid, 1);
        player.Attributes.SyncTo(sync, attr);
        DatabaseHelper.SaveDatabaseType(player.Data);

        return Task.FromResult(CallGSResult.Ok(new JsonObject(), sync));
    }
}

/// <summary>BagRogue_SelectCraftItem -- the client only lights a red dot off nUniqueId.</summary>
[CallGSApi("BagRogue_SelectCraftItem")]
public class BagRogue_SelectCraftItem : CallGSHandler<BagRogueCraftSelectParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, BagRogueCraftSelectParam request) =>
        Task.FromResult(CallGSResult.Ok(new JsonObject { ["nUniqueId"] = request.Index }));
}

/// <summary>BagRogue_ReforgeItem -- bumps the per-node reforge counter (142,18).</summary>
[CallGSApi("BagRogue_ReforgeItem")]
public class BagRogue_ReforgeItem : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player;
        if (player == null)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var sync = new NtfSyncPlayer();
        var attr = player.Attributes.Add(
            AttrIds.BagRogue.GroupId, AttrIds.BagRogue.CurrentReforgeNumSid, 1);
        player.Attributes.SyncTo(sync, attr);
        DatabaseHelper.SaveDatabaseType(player.Data);

        return Task.FromResult(CallGSResult.Ok(new JsonObject(), sync));
    }
}

/// <summary>
/// BagRogue_StoreBuyItem / RefreshStore -- both panels refresh themselves from state the client already has
/// (BackpackShop:RefreshShop), so the reply body is irrelevant; the counter is what the shop reads next.
/// </summary>
[CallGSApi("BagRogue_StoreBuyItem")]
public class BagRogue_StoreBuyItem : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player;
        if (player == null)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var sync = new NtfSyncPlayer();
        var attr = player.Attributes.Add(
            AttrIds.BagRogue.GroupId, AttrIds.BagRogue.CurrentStockroomNumSid, 1);
        player.Attributes.SyncTo(sync, attr);
        DatabaseHelper.SaveDatabaseType(player.Data);

        return Task.FromResult(CallGSResult.Ok(new JsonObject(), sync));
    }
}

[CallGSApi("BagRogue_RefreshStore")]
public class BagRogue_RefreshStore : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param) =>
        Task.FromResult(CallGSResult.Ok(new JsonObject()));
}

/// <summary>BagRogue_LevelSelectAward -- confirms the post-battle three-choice pick; no reply fields read.</summary>
[CallGSApi("BagRogue_LevelSelectAward")]
public class BagRogue_LevelSelectAward : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param) =>
        Task.FromResult(CallGSResult.Ok(new JsonObject()));
}

/// <summary>
/// BagRogue_AwardRefresh -- re-rolls the three-choice pool. The counter lives in (142,13) and the client only
/// displays it (umg_backpack_choose.lua:25,65), so the cap is server side (timelist RefreshBattleReward = 3).
/// </summary>
[CallGSApi("BagRogue_AwardRefresh")]
public class BagRogue_AwardRefresh : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player;
        if (player == null)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var sync = new NtfSyncPlayer();
        var attr = player.Attributes.Add(
            AttrIds.BagRogue.GroupId, AttrIds.BagRogue.AwardRefreshTimesSid, 1);
        player.Attributes.SyncTo(sync, attr);
        DatabaseHelper.SaveDatabaseType(player.Data);

        return Task.FromResult(CallGSResult.Ok(new JsonObject(), sync));
    }
}

/// <summary>
/// BagRogue_SelectAdventure -- the index points into the option array the server wrote as the node's "D".
/// The reply decides whether the map advances to the next floor (option type 4) or just refreshes.
/// </summary>
[CallGSApi("BagRogue_SelectAdventure")]
public class BagRogue_SelectAdventure : CallGSHandler<BagRogueAdventureParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, BagRogueAdventureParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var node = BagRogueService.CurrentNode(player);
        if (node == null)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var options = BagRogueService.OptionsOfGroup(node.AdventureGroupId);
        if (request.SelectIdx < 1 || request.SelectIdx > options.Count)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var sync = new NtfSyncPlayer();
        if (GameData.BagRogueAdventureData.TryGetValue(options[request.SelectIdx - 1], out var option)
            && option.OptionType == 4)
        {
            // "Next floor" options advance the route: regenerate the map on the next CheckNewMapNode.
            var attr = player.Attributes.Set(
                AttrIds.BagRogue.GroupId, AttrIds.BagRogue.CurrentLevelNodeSid, 0);
            player.Attributes.SyncTo(sync, attr);
        }

        DatabaseHelper.SaveDatabaseType(player.Data);
        return Task.FromResult(CallGSResult.Ok(new JsonObject(), sync));
    }
}

/// <summary>
/// BagRogue_LevelMoppingUp -- the sweep skips straight into the node's stage and pays the same table reward.
/// The client only reads tbAward (BagRogue.lua:1896).
/// </summary>
[CallGSApi("BagRogue_LevelMoppingUp")]
public class BagRogue_LevelMoppingUp : CallGSHandler<BagRogueEnterLevelParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, BagRogueEnterLevelParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var node = BagRogueService.CurrentNode(player);
        var awards = new JsonArray();
        if (node != null)
        {
            var sync = new NtfSyncPlayer();
            foreach (var row in BagRogueService.NodeRewardRows(node))
            {
                var item = new JsonArray();
                foreach (var cell in row)
                    item.Add(cell);
                awards.Add(item);
            }

            DatabaseHelper.SaveDatabaseType(player.Data);
            return Task.FromResult(CallGSResult.Ok(new JsonObject { ["tbAward"] = awards }, sync));
        }

        return Task.FromResult(CallGSResult.Ok(new JsonObject { ["tbAward"] = awards }));
    }
}

/// <summary>
/// BagRogue_GetQuestAward -- claims the period's daily/weekly/normal tasks. The claim bits live in attribute
/// group 7 (bit0 claimed, bits1..31 progress), same as every other activity board.
/// </summary>
[CallGSApi("BagRogue_GetQuestAward")]
public class BagRogue_GetQuestAward : CallGSHandler<BagRogueQuestAwardParam>
{
    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, BagRogueQuestAwardParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return CallGSResult.Error("error.BadParam");

        var schedule = BagRogueService.ScheduleOf(player);
        var awards = new JsonArray();
        if (schedule == null || request.Ids == null || request.Ids.Count == 0)
            return CallGSResult.Ok(new JsonObject { ["tbAwardList"] = awards });

        var sync = new NtfSyncPlayer();
        var granted = 0;
        foreach (var taskId in request.Ids)
        {
            if (!schedule.TaskIds.Contains(taskId))
                continue;

            if (!GameData.ActivityQuestData.TryGetValue(taskId, out var quest))
                continue;

            var rows = await AchievementClaimService.TryClaimAsync(
                player,
                AttrIds.Achievement.QuestGroupId,
                taskId,
                quest.CheckValue,
                AchievementClaimService.ReadRewardRows(quest.Rewards),
                sync);

            foreach (var row in rows ?? new JsonArray())
            {
                if (row is not JsonArray cells)
                    continue;

                var item = new JsonArray();
                foreach (var cell in cells)
                    item.Add(cell?.DeepClone());
                awards.Add(item);
                granted++;
            }
        }

        if (granted > 0)
        {
            DatabaseHelper.SaveDatabaseType(player.Data);
            DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
        }

        return CallGSResult.Ok(new JsonObject { ["tbAwardList"] = awards }, sync);
    }
}

public sealed class BagRogueSellParam
{
    [JsonPropertyName("nUniqueId")] public uint UniqueId { get; set; }
}

public sealed class BagRogueUpgradeParam
{
    [JsonPropertyName("nUniqueA")] public uint UniqueA { get; set; }
    [JsonPropertyName("nUniqueB")] public uint UniqueB { get; set; }
}

public sealed class BagRogueEquipParam
{
    [JsonPropertyName("tbList")] public List<BagRogueEquipSlot>? List { get; set; }
    [JsonPropertyName("tbTalent")] public List<uint>? Talents { get; set; }
}

public sealed class BagRogueEquipSlot
{
    [JsonPropertyName("uId")] public uint UniqueId { get; set; }
    [JsonPropertyName("bRemove")] public bool Remove { get; set; }
    [JsonPropertyName("nPos")] public uint Pos { get; set; }
    [JsonPropertyName("nDir")] public uint Dir { get; set; }
}

public sealed class BagRogueSaveExternParam
{
    [JsonPropertyName("nUniqueId")] public uint UniqueId { get; set; }
    [JsonPropertyName("nNodeId")] public uint NodeId { get; set; }
}

public sealed class BagRogueSelectExternParam
{
    [JsonPropertyName("tbUniqueId")] public List<uint>? UniqueIds { get; set; }
}

public sealed class BagRogueTrialCardParam
{
    [JsonPropertyName("tbTrial")] public List<uint>? Trials { get; set; }
}

public sealed class BagRogueCraftSelectParam
{
    [JsonPropertyName("nIndex")] public uint Index { get; set; }
}

public sealed class BagRogueAdventureParam
{
    [JsonPropertyName("nSelectIdx")] public int SelectIdx { get; set; }
}

public sealed class BagRogueQuestAwardParam
{
    [JsonPropertyName("tbIdList")] public List<uint>? Ids { get; set; }
}
