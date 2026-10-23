using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.BagRogue;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BagRogue;

// The entry + route flow of 背包肉鸽 (DLC28). Every one of these opens a connection overlay on the client
// first, so a missing reply is a permanent spinner; ExFightLog is the only member of the family without a
// registered s2c callback and therefore must not answer at all (Misc/S2C.lua:29-36).

/// <summary>
/// BagRogue_CheckOpenAct -- answers open + a window that covers now, and seeds (142,1). The client rewrites
/// its own timelist row with the returned window (BagRogue.lua:1212-1215) and then re-checks every second
/// (umg_backpack.lua:49-77), so both halves are required: without the attribute BackpackMain dereferences a
/// nil config (umg_backpack.lua:104), without the window it closes itself one second later.
/// </summary>
[CallGSApi("BagRogue_CheckOpenAct")]
public class BagRogue_CheckOpenAct : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player;
        var schedule = player == null ? BagRogueService.CurrentSchedule : BagRogueService.ScheduleOf(player);
        if (player == null || schedule == null)
            return Task.FromResult(CallGSResult.Ok(new JsonObject { ["bOpen"] = false }));

        var sync = new NtfSyncPlayer();
        var attr = player.Attributes.Set(
            AttrIds.BagRogue.GroupId, AttrIds.BagRogue.ActivitySid, schedule.Id);
        player.Attributes.SyncTo(sync, attr);

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return Task.FromResult(CallGSResult.Ok(new JsonObject
        {
            ["bOpen"] = true,
            ["nId"] = schedule.Id,
            ["nStartTime"] = now - 3600,
            ["nEndTime"] = now + 2592000
        }, sync));
    }
}

/// <summary>
/// BagRogue_FirstEnter -- bSet makes the client wipe the trial-card formation left over from the previous
/// run (BagRogue.lua:1078-1089). No ShowConnection here (BagRogue.lua:1722), so a lost reply is silent.
/// </summary>
[CallGSApi("BagRogue_FirstEnter")]
public class BagRogue_FirstEnter : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player;
        if (player == null)
            return Task.FromResult(CallGSResult.Ok(new JsonObject { ["bSet"] = false }));

        var first = player.Attributes.GetValue(AttrIds.BagRogue.GroupId, AttrIds.BagRogue.FirstEnterSid) == 0;
        if (!first)
            return Task.FromResult(CallGSResult.Ok(new JsonObject { ["bSet"] = false }));

        var sync = new NtfSyncPlayer();
        var attr = player.Attributes.Set(AttrIds.BagRogue.GroupId, AttrIds.BagRogue.FirstEnterSid, 1);
        player.Attributes.SyncTo(sync, attr);
        DatabaseHelper.SaveDatabaseType(player.Data);

        // bSet=true on a fresh account: the reply has to be a JSON object, never null.
        return Task.FromResult(CallGSResult.Ok(new JsonObject { ["bSet"] = true }, sync));
    }
}

/// <summary>
/// BagRogue_CheckNewMapNode -- the map widget only checks its local (142,4) after the reply
/// (BagRogue.lua:1265), so the server is responsible for having generated the route by now.
/// </summary>
[CallGSApi("BagRogue_CheckNewMapNode")]
public class BagRogue_CheckNewMapNode : CallGSHandler
{
    private static readonly Logger Log = new(nameof(BagRogue_CheckNewMapNode));

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player;
        if (player == null)
            return Task.FromResult(CallGSResult.Ok());

        var sync = new NtfSyncPlayer();
        if (player.Attributes.GetValue(AttrIds.BagRogue.GroupId, AttrIds.BagRogue.CurrentLevelNodeSid) == 0)
        {
            var schedule = BagRogueService.ScheduleOf(player);
            if (schedule == null)
            {
                Log.Warn("BagRogue: no period configured, route not generated.");
                return Task.FromResult(CallGSResult.Ok(new JsonObject(), sync));
            }

            BagRogueService.StartRoute(player, schedule, sync);
            DatabaseHelper.SaveDatabaseType(player.Data);
        }

        return Task.FromResult(CallGSResult.Ok(new JsonObject(), sync));
    }
}

/// <summary>
/// BagRogue_NodeWalk -- validates the walk against 61/1 and marks the node visited. The reply body is not
/// read; the client just refreshes its node states (BagRogue.lua:1291).
/// </summary>
[CallGSApi("BagRogue_NodeWalk")]
public class BagRogue_NodeWalk : CallGSHandler<BagRogueNodeWalkParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, BagRogueNodeWalkParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var sync = new NtfSyncPlayer();
        if (!BagRogueService.TryWalk(player, request.NodeId, sync, out var error))
            return Task.FromResult(CallGSResult.Error(error, sync));

        DatabaseHelper.SaveDatabaseType(player.Data);
        return Task.FromResult(CallGSResult.Ok(new JsonObject(), sync));
    }
}

/// <summary>
/// BagRogue_NodeFinish -- closes the run and opens BackpackSettlement with tbAward. The key is mandatory:
/// the panel iterates it without a nil guard (umg_backpack_settlement.lua:37-47).
/// </summary>
[CallGSApi("BagRogue_NodeFinish")]
public class BagRogue_NodeFinish : CallGSHandler
{
    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player;
        if (player == null)
            return CallGSResult.Error("error.BadParam");

        var sync = new NtfSyncPlayer();
        var awards = BagRogueService.FinishRoute(player, sync);
        // No counter bump here: bits 16-31 of (142,2) mean "finished a run", which only an ending node
        // proves. Counting the close button as well would let a player clear the period by walking out.

        var rows = new List<IReadOnlyList<uint>>();
        foreach (var item in awards)
        {
            if (item is not JsonArray row)
                continue;

            var values = new List<uint>();
            foreach (var cell in row)
                values.Add(uint.TryParse(cell?.ToString(), out var parsed) ? parsed : 0);

            if (values.Count >= 5 && values[4] > 0)
                rows.Add(values);
        }

        if (rows.Count > 0)
            await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);

        DatabaseHelper.SaveDatabaseType(player.Data);
        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);

        return CallGSResult.Ok(new JsonObject { ["tbAward"] = awards }, sync);
    }
}

/// <summary>BagRogue_EnterLevel -- combat permission + seed; the map itself is opened client side.</summary>
[CallGSApi("BagRogue_EnterLevel")]
public class BagRogue_EnterLevel : CallGSHandler<BagRogueEnterLevelParam>
{
    private static readonly Logger Log = new(nameof(BagRogue_EnterLevel));

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, BagRogueEnterLevelParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        if (!GameData.BagRogueLevelData.ContainsKey(request.Id))
        {
            Log.Warn($"BagRogue_EnterLevel: stage {request.Id} is not in level.json.");
            return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));
        }

        var seed = (uint)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() & 0x7FFFFFFF);
        return Task.FromResult(CallGSResult.Ok(new JsonObject { ["nSeed"] = seed }));
    }
}

/// <summary>
/// BagRogue_ExFightLog -- no s2c.Register on the client (BagRogue.lua:1675), so answering would throw in
/// Misc/S2C.lua:29-36.
/// </summary>
[CallGSApi("BagRogue_ExFightLog")]
public class BagRogue_ExFightLog : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param) =>
        Task.FromResult(CallGSResult.NoResponse());
}

public sealed class BagRogueNodeWalkParam
{
    [JsonPropertyName("nNodeId")] public uint NodeId { get; set; }
}

public sealed class BagRogueEnterLevelParam
{
    [JsonPropertyName("nID")] public uint Id { get; set; }
    [JsonPropertyName("tbLog")] public JsonNode? Log { get; set; }
}
