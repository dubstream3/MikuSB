using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Proto;
using MikuSB.GameServer.Game.Jigsaw;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Jigsaw;

// JigsawPuzzle.GetGirl (Activity/JigsawPuzzle.lua:402-419). Request { nId } where nId is the schedule
// id (1..3), not the activity row id. The s2c callback only unpacks tbGirl as
// { g, d, p, l, n, nTime } and hands the first five to Item.Gain, so the reply must echo the whole
// RoleID row of the schedule table (activity/jigsaw_puzzle/jigsawpuzzle.json).
[CallGSApi("JigsawPuzzle_GetGirl")]
public class JigsawPuzzle_GetGirl : CallGSHandler<JigsawGetGirlParam>
{
    private static readonly Logger Log = new(nameof(JigsawPuzzle_GetGirl));

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, JigsawGetGirlParam request)
    {
        var player = context.Connection.Player!;
        var schedule = JigsawPuzzleService.FindSchedule(request.ScheduleId);
        if (schedule is null || schedule.Role.Count < 5)
        {
            Log.Warn($"GetGirl: unknown schedule {request.ScheduleId} for uid={player.Uid}.");
            return CallGSResult.Error("ui.TxtNotOpen");
        }

        if (JigsawPuzzleService.HasGirl(player, request.ScheduleId))
            return CallGSResult.Error("achievement.RewardGeted");

        var sync = new NtfSyncPlayer();
        JigsawPuzzleService.SetGirl(player, request.ScheduleId, sync);

        var role = schedule.Role;
        var rows = new List<IReadOnlyList<uint>>
        {
            new List<uint> { role[0], role[1], role[2], role[3], role[4] }
        };
        await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);

        DatabaseHelper.SaveDatabaseType(player.Data);
        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);

        Log.Info($"GetGirl: schedule={request.ScheduleId} role=[{string.Join(',', role)}] for uid={player.Uid}.");

        var girl = new JsonArray();
        for (var index = 0; index < role.Count; index++)
            girl.Add(role[index]);

        return CallGSResult.Ok(new JsonObject
        {
            ["tbGirl"] = girl
        }, sync);
    }
}

public sealed class JigsawGetGirlParam
{
    [JsonPropertyName("nId")] public uint ScheduleId { get; set; }
}

// JigsawPuzzle.PutPiece (Activity/JigsawPuzzle.lua:421-448). Request { nActId, nId }. The adjacency
// requirement on the client is commented out (:366-388), so "owned -> placed" is the only rule; the
// reply only needs to echo nId so the play UI repaints that one tile.
[CallGSApi("JigsawPuzzle_PutPiece")]
public class JigsawPuzzle_PutPiece : CallGSHandler<JigsawPutPieceParam>
{
    private static readonly Logger Log = new(nameof(JigsawPuzzle_PutPiece));

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, JigsawPutPieceParam request)
    {
        var player = context.Connection.Player!;
        if (JigsawPuzzleService.FindPiece(request.ActId, request.PieceId) is null)
            return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));

        var state = JigsawPuzzleService.GetPieceState(player, request.ActId, request.PieceId);
        if (state != AttrIds.Jigsaw.StateOwned)
            return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));

        var sync = new NtfSyncPlayer();
        JigsawPuzzleService.SetPieceState(
            player, request.ActId, request.PieceId, AttrIds.Jigsaw.StatePlaced, sync);
        JigsawPuzzleService.ReportProgress(player, request.ActId, sync);
        DatabaseHelper.SaveDatabaseType(player.Data);

        Log.Info($"PutPiece: schedule={request.ActId} piece={request.PieceId} " +
                 $"placed={JigsawPuzzleService.CountPlaced(player, request.ActId)} for uid={player.Uid}.");

        return Task.FromResult(CallGSResult.Ok(new JsonObject
        {
            ["nId"] = request.PieceId
        }, sync));
    }
}

public sealed class JigsawPutPieceParam
{
    [JsonPropertyName("nActId")] public uint ActId { get; set; }
    [JsonPropertyName("nId")] public uint PieceId { get; set; }
}

// JigsawPuzzle.SetActive (Activity/JigsawPuzzle.lua:474-499): the "tuning" button, only reachable when
// every piece is placed. The reply must carry the same nId (the play UI compares it against its own).
[CallGSApi("JigsawPuzzle_SetActive")]
public class JigsawPuzzle_SetActive : CallGSHandler<JigsawSetActiveParam>
{
    private static readonly Logger Log = new(nameof(JigsawPuzzle_SetActive));

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, JigsawSetActiveParam request)
    {
        var player = context.Connection.Player!;
        if (JigsawPuzzleService.FindSchedule(request.ScheduleId) is null)
            return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));

        if (JigsawPuzzleService.IsActive(player, request.ScheduleId))
            return Task.FromResult(CallGSResult.Error("achievement.RewardGeted"));

        if (!JigsawPuzzleService.AllPlaced(player, request.ScheduleId))
            return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));

        var sync = new NtfSyncPlayer();
        JigsawPuzzleService.SetActive(player, request.ScheduleId, sync);
        JigsawPuzzleService.ReportProgress(player, request.ScheduleId, sync);
        DatabaseHelper.SaveDatabaseType(player.Data);

        Log.Info($"SetActive: schedule={request.ScheduleId} tuned for uid={player.Uid}.");

        return Task.FromResult(CallGSResult.Ok(new JsonObject
        {
            ["nId"] = request.ScheduleId
        }, sync));
    }
}

public sealed class JigsawSetActiveParam
{
    [JsonPropertyName("nId")] public uint ScheduleId { get; set; }
}

// JigsawPuzzle.UseHelp (Activity/JigsawPuzzle.lua:501-536). The client picks the piece itself and only
// asks the server to stamp the cooldown; the reply feeds OnUseHelp which requires nId AND nActId.
[CallGSApi("JigsawPuzzle_UseHelp")]
public class JigsawPuzzle_UseHelp : CallGSHandler<JigsawUseHelpParam>
{
    private static readonly Logger Log = new(nameof(JigsawPuzzle_UseHelp));

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, JigsawUseHelpParam request)
    {
        var player = context.Connection.Player!;
        var schedule = JigsawPuzzleService.FindSchedule(request.ActId);
        if (schedule is null || JigsawPuzzleService.FindPiece(request.ActId, request.PieceId) is null)
            return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var last = JigsawPuzzleService.GetHelpTime(player, request.ActId);
        if (last > 0 && now - last < schedule.HitInterval)
            return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));

        var sync = new NtfSyncPlayer();
        JigsawPuzzleService.SetHelpTime(player, request.ActId, now, sync);
        DatabaseHelper.SaveDatabaseType(player.Data);

        Log.Info($"UseHelp: schedule={request.ActId} piece={request.PieceId} for uid={player.Uid}.");

        return Task.FromResult(CallGSResult.Ok(new JsonObject
        {
            ["nId"] = request.PieceId,
            ["nActId"] = request.ActId
        }, sync));
    }
}

public sealed class JigsawUseHelpParam
{
    [JsonPropertyName("nActId")] public uint ActId { get; set; }
    [JsonPropertyName("nId")] public uint PieceId { get; set; }
}
