using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.Rogue;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue;

// DLC1 2D rogue entry chain (C/UMG/Dlc1/umg_dlc1_main.lua:370-394): the panel only opens when
// RogueLogic.GetActivitieID() > 0 (period table window, all 2023) and the timestamp faked into
// (25,9999) falls inside the pinned DLC's window. Everything the client shows comes from gid 25,
// which is pure-read on the client side, so these handlers persist state and answer with the shape
// the callback indexes.
[CallGSApi("RogueLogic_EnterRogue")]
public class RogueLogic_EnterRogue : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player;
        if (player == null)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var sync = new NtfSyncPlayer();
        if (RogueService.EnsureStarted(player, sync) is not { } started)
            return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));

        // EnterRogue's real product is the attribute set; the reply is only checked for sError.
        RogueService.SeedOpenStory(player, started.Act, sync);
        DatabaseHelper.SaveDatabaseType(player.Data);

        return Task.FromResult(CallGSResult.Ok(new JsonObject(), sync));
    }
}

// Daily trial/buff slots. The server has no day-up buff table loaded, so both lists stay empty --
// the client loops over them, and an empty array simply renders nothing.
[CallGSApi("RogueLogic_FetchDailyBuff")]
public class RogueLogic_FetchDailyBuff : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        return Task.FromResult(CallGSResult.Ok(new JsonObject
        {
            ["tbDailyBuffRole"] = new JsonArray(),
            ["tbDailyBuff"] = new JsonArray()
        }));
    }
}

[CallGSApi("RogueLogic_ChangeFormation")]
public class RogueLogic_ChangeFormation : CallGSHandler<RogueFormationParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, RogueFormationParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var sync = new NtfSyncPlayer();
        RogueService.Write(player, AttrIds.Rogue.FormationInfoSid,
            RogueService.ConvertFormation(request.Formation), sync);
        DatabaseHelper.SaveDatabaseType(player.Data);

        return Task.FromResult(CallGSResult.Ok(new JsonObject(), sync));
    }
}

[CallGSApi("RogueLogic_ClientMoveNext")]
public class RogueLogic_ClientMoveNext : CallGSHandler<RogueNodeParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, RogueNodeParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        if (RogueService.FindNode(RogueService.CurrentMap(player), request.Node) == null)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var sync = new NtfSyncPlayer();
        RogueService.MoveNext(player, request.Node, sync);
        DatabaseHelper.SaveDatabaseType(player.Data);

        return Task.FromResult(CallGSResult.Ok(new JsonObject(), sync));
    }
}

// FinishNode is two calls in one api: entering a battle node (nState=1) asks for a seed, while a
// shop/random node sends only {nID,nType} and closes the interaction. The client tests
// type(tbParam[1]) == "table", so the reply must be an ARRAY, never an object (Rogue.lua:973-980).
[CallGSApi("RogueLogic_FinishNode")]
public class RogueLogic_FinishNode : CallGSHandler<RogueFinishNodeParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, RogueFinishNodeParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var node = RogueService.FindNode(RogueService.CurrentMap(player), request.Node);
        if (node == null)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var sync = new NtfSyncPlayer();
        if (request.State == 1)
        {
            // Entering a level: mark the node occupied and hand back the seed the settlement echoes.
            RogueService.SetNodeState(player, request.Node, 1, sync);
            var seed = RogueService.NewSeed();
            DatabaseHelper.SaveDatabaseType(player.Data);

            var enter = new JsonArray { new JsonObject { ["nSeed"] = seed } };
            return Task.FromResult(CallGSResult.Ok(enter, sync));
        }

        RogueService.SetNodeState(player, request.Node, 2, sync);
        if (node.Node == 3)
        {
            var act = RogueService.SelectActivity(DateTime.Now);
            if (act != null)
                RogueService.EnsureShopGoods(player, act, request.Node, sync);
        }

        DatabaseHelper.SaveDatabaseType(player.Data);

        var done = new JsonArray
        {
            new JsonObject { ["nSeed"] = 0, ["Awards"] = new JsonArray() }
        };
        return Task.FromResult(CallGSResult.Ok(done, sync));
    }
}

// Revive costs activities.Price[nReviveTimes]; the price table is 1-based on the client
// (umg_dlcrogue_shop.lua:103-106), so the first revive reads Price[1].
[CallGSApi("RogueLogic_ReviveRole")]
public class RogueLogic_ReviveRole : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player;
        if (player == null)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var act = RogueService.SelectActivity(DateTime.Now);
        if (act == null)
            return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));

        var sync = new NtfSyncPlayer();
        var baseInfo = RogueService.ReadObject(player, AttrIds.Rogue.BaseInfoSid);
        var reviveTimes = RogueService.Field(baseInfo, "nReviveTimes");
        var price = act.Prices.Count > (int)reviveTimes ? act.Prices[(int)reviveTimes] : 0;

        if (price > 0 && !CurrencyService.TrySpend(player, RogueService.CurrencyId, price, sync, out var error))
            return Task.FromResult(CallGSResult.Error(error, sync));

        RogueService.ReviveFirstDead(player, sync);
        RogueService.SetField(baseInfo, "nReviveTimes", reviveTimes + 1);
        RogueService.Write(player, AttrIds.Rogue.BaseInfoSid, baseInfo, sync);
        DatabaseHelper.SaveDatabaseType(player.Data);

        return Task.FromResult(CallGSResult.Ok(new JsonObject(), sync));
    }
}

[CallGSApi("RogueLogic_RefreshShopGoods")]
public class RogueLogic_RefreshShopGoods : CallGSHandler<RogueNodeParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, RogueNodeParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var act = RogueService.SelectActivity(DateTime.Now);
        if (act == null)
            return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));

        var sync = new NtfSyncPlayer();
        var baseInfo = RogueService.ReadObject(player, AttrIds.Rogue.BaseInfoSid);
        var refreshTimes = RogueService.Field(baseInfo, "nRefreshTimes");
        var price = act.Refreshes.Count > (int)refreshTimes ? act.Refreshes[(int)refreshTimes] : 0;

        if (price > 0 && !CurrencyService.TrySpend(player, RogueService.CurrencyId, price, sync, out var error))
            return Task.FromResult(CallGSResult.Error(error, sync));

        RogueService.StoreShopGoods(player, request.Node, RogueService.RollGoods(act, request.Node), sync);
        RogueService.SetField(baseInfo, "nRefreshTimes", refreshTimes + 1);
        RogueService.Write(player, AttrIds.Rogue.BaseInfoSid, baseInfo, sync);
        DatabaseHelper.SaveDatabaseType(player.Data);

        return Task.FromResult(CallGSResult.Ok(new JsonObject(), sync));
    }
}

// The client's handler takes no parameters at all (Rogue.lua:1085), so anything is accepted.
[CallGSApi("RogueLogic_ResetRogue")]
public class RogueLogic_ResetRogue : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player;
        if (player == null)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var sync = new NtfSyncPlayer();
        RogueService.Reset(player, sync);
        DatabaseHelper.SaveDatabaseType(player.Data);

        return Task.FromResult(CallGSResult.Ok(new JsonObject(), sync));
    }
}

[CallGSApi("RogueLogic_VisitShop")]
public class RogueLogic_VisitShop : CallGSHandler<RogueNodeParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, RogueNodeParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var node = RogueService.FindNode(RogueService.CurrentMap(player), request.Node);
        if (node == null || node.Node != 3)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var act = RogueService.SelectActivity(DateTime.Now);
        if (act == null)
            return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));

        var sync = new NtfSyncPlayer();
        RogueService.EnsureShopGoods(player, act, request.Node, sync);
        RogueService.SetNodeState(player, request.Node, 1, sync);
        // (25,9998) is deprecated on the client (Rogue.lua:1143-1145); kept out of the write path.
        DatabaseHelper.SaveDatabaseType(player.Data);

        return Task.FromResult(CallGSResult.Ok(new JsonObject(), sync));
    }
}

// The rogue task ids are achievement quest ids (8001..8048). The claim service needs the quest's own
// group and check value, and guessing the group would write a claim bit into the wrong group, so the
// reply stays empty until the achievement wiring for this family is confirmed (B level).
[CallGSApi("RogueLogic_QuickGetReward")]
public class RogueLogic_QuickGetReward : CallGSHandler<RogueIdListParam>
{
    private static readonly Logger Log = new(nameof(RogueLogic_QuickGetReward));

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, RogueIdListParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        Log.Info($"Rogue quick reward requested for [{string.Join(",", request.Ids)}] by uid={player.Uid} " +
                 "(achievement group wiring pending).");
        return Task.FromResult(CallGSResult.Ok(new JsonObject { ["tbAwards"] = new JsonArray() }));
    }
}

// No s2c.Register anywhere in the client -> answering raises S2C.lua:29-35.
[CallGSApi("RogueLogic_ShowOpenStory")]
public class RogueLogic_ShowOpenStory : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param) =>
        Task.FromResult(CallGSResult.NoResponse());
}

// ExtendFightLog / ExtendFightDynamicLog already have NoResponse handlers in Handlers/Misc
// (they are shared by every fight-logging module, not part of this family).

public sealed class RogueFormationParam
{
    [JsonPropertyName("tbFormation")] public JsonArray? Formation { get; set; }
}

public sealed class RogueNodeParam
{
    [JsonPropertyName("nID")] public uint NodeId { get; set; }
    [JsonPropertyName("nNodeID")] public uint NodeIdAlt { get; set; }

    [JsonIgnore] public uint Node => NodeId != 0 ? NodeId : NodeIdAlt;
}

public sealed class RogueFinishNodeParam
{
    [JsonPropertyName("nID")] public uint NodeId { get; set; }
    [JsonPropertyName("nType")] public uint Type { get; set; }
    [JsonPropertyName("nLevelID")] public uint LevelId { get; set; }
    [JsonPropertyName("nState")] public uint State { get; set; }
    [JsonPropertyName("nSelectIndex")] public uint SelectIndex { get; set; }

    [JsonIgnore] public uint Node => NodeId;
}

public sealed class RogueIdListParam
{
    [JsonPropertyName("tbIdList")] public List<uint> Ids { get; set; } = [];
}
