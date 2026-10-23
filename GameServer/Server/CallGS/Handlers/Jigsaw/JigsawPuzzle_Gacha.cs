using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Proto;
using MikuSB.GameServer.Game.Achievement;
using MikuSB.GameServer.Game.Jigsaw;
using MikuSB.GameServer.Game.Player;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Jigsaw;

// JigsawPuzzle.GetPiece (Activity/JigsawPuzzle.lua:450-472 + UMG/Puzzle/umg_puzzle_gacha.lua:184-197).
// Request { nId, nStage, nNum, nActivityId }: nId is the schedule, nStage the phase whose pool is being
// drawn from, nNum 1..Multiple[1] pieces, nActivityId the outer activity row (only used to check that
// the phase has unlocked). Each draw costs one unit of the schedule's KeyID currency; the s2c callback
// reads tbGet (the piece ids that were obtained) and nothing else.
[CallGSApi("JigsawPuzzle_GetPiece")]
public class JigsawPuzzle_GetPiece : CallGSHandler<JigsawGetPieceParam>
{
    private static readonly Logger Log = new(nameof(JigsawPuzzle_GetPiece));

    /// <summary>The ten-draw cap in the gacha UI (umg_puzzle_gacha.lua:121-131).</summary>
    private const uint MaxDraw = 10;

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, JigsawGetPieceParam request)
    {
        var player = context.Connection.Player!;
        var schedule = JigsawPuzzleService.FindSchedule(request.ScheduleId);
        if (schedule is null || schedule.PhasePieces.Count == 0)
            return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));

        var stage = request.Stage;
        if (stage == 0 || stage > schedule.PhasePieces.Count)
            return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var unlock = JigsawPuzzleService.StageTime(player, request.ScheduleId, request.ActivityId, (int)stage);
        if (unlock > now)
            return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));

        var count = Math.Clamp(request.Count, 1u, MaxDraw);
        var pool = schedule.PhasePieces[(int)stage - 1];
        var candidates = pool
            .Where(pieceId => JigsawPuzzleService.FindPiece(request.ScheduleId, pieceId) is not null)
            .Where(pieceId => JigsawPuzzleService.GetPieceState(player, request.ScheduleId, pieceId) ==
                              AttrIds.Jigsaw.StateNotOwned)
            .ToList();

        if (candidates.Count == 0)
            return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));

        if (candidates.Count < count)
            count = (uint)candidates.Count;

        var sync = new NtfSyncPlayer();
        if (!CurrencyService.TrySpend(player, schedule.KeyId, count, sync, out var error))
            return Task.FromResult(CallGSResult.Error(error));

        var got = new JsonArray();
        foreach (var pieceId in candidates.Take((int)count))
        {
            JigsawPuzzleService.SetPieceState(
                player, request.ScheduleId, pieceId, AttrIds.Jigsaw.StateOwned, sync);
            got.Add(pieceId);
        }

        DatabaseHelper.SaveDatabaseType(player.Data);
        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);

        Log.Info($"GetPiece: schedule={request.ScheduleId} stage={stage} drew={got.Count} " +
                 $"for uid={player.Uid}.");

        return Task.FromResult(CallGSResult.Ok(new JsonObject
        {
            ["tbGet"] = got
        }, sync));
    }
}

public sealed class JigsawGetPieceParam
{
    [JsonPropertyName("nId")] public uint ScheduleId { get; set; }
    [JsonPropertyName("nStage")] public uint Stage { get; set; }
    [JsonPropertyName("nNum")] public uint Count { get; set; }
    [JsonPropertyName("nActivityId")] public uint ActivityId { get; set; }
}

// JigsawPuzzle.GetMissionReward (Activity/JigsawPuzzle.lua:538-554). Request { nActId, nId } where
// nId is an activity_quest id and -1 means "claim everything that is ready" (the quick button at
// umg_puzzle_mission.lua:22-38 sends -1). The s2c callback feeds tbReward straight to Item.Gain, so it
// must be the flat [[g,d,p,l,n], ...] shape.
[CallGSApi("JigsawPuzzle_GetReward")]
public class JigsawPuzzle_GetReward : CallGSHandler<JigsawGetRewardParam>
{
    private static readonly Logger Log = new(nameof(JigsawPuzzle_GetReward));

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, JigsawGetRewardParam request)
    {
        var player = context.Connection.Player!;
        var schedule = JigsawPuzzleService.FindSchedule(request.ActId);
        if (schedule is null)
            return CallGSResult.Error("ui.TxtNotOpen");

        var quick = request.TaskId is 0 or -1;
        if (!quick && !schedule.TaskIds.Contains((uint)request.TaskId))
            return CallGSResult.Error("ui.TxtNotOpen");

        var sync = new NtfSyncPlayer();
        var awards = new JsonArray();

        foreach (var taskId in schedule.TaskIds)
        {
            if (!quick && taskId != (uint)request.TaskId)
                continue;

            var quest = GameData.ActivityQuestData.GetValueOrDefault(taskId);
            if (quest is null || quest.Rewards is null)
                continue;

            var granted = await AchievementClaimService.TryClaimAsync(
                player,
                AchievementClaimService.QuestGroupId,
                taskId,
                quest.CheckValue,
                AchievementClaimService.ReadRewardRows(quest.Rewards),
                sync);
            if (granted is null)
                continue;

            foreach (var row in granted)
                awards.Add(row?.DeepClone());
        }

        if (awards.Count > 0)
        {
            DatabaseHelper.SaveDatabaseType(player.Data);
            DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
        }

        Log.Info($"GetReward: schedule={request.ActId} task={(quick ? -1 : request.TaskId)} " +
                 $"rows={awards.Count} for uid={player.Uid}.");

        return CallGSResult.Ok(new JsonObject
        {
            ["tbReward"] = awards
        }, sync);
    }
}

public sealed class JigsawGetRewardParam
{
    [JsonPropertyName("nActId")] public uint ActId { get; set; }

    /// <summary>Signed on purpose: the quick button sends -1.</summary>
    [JsonPropertyName("nId")] public int TaskId { get; set; }
}

// JigsawPuzzle.EnterLog (Activity/JigsawPuzzle.lua:556-559) records the first entry timestamp that
// GetStageTime uses to decide whether the player joined before the activity opened (and therefore gets
// every phase at once). The client registers no s2c callback for it, so answering would trip the
// S2C.lua:29-35 guard — the handler must stay silent.
[CallGSApi("JigsawPuzzle_EnterLog")]
public class JigsawPuzzle_EnterLog : CallGSHandler<JigsawEnterLogParam>
{
    private static readonly Logger Log = new(nameof(JigsawPuzzle_EnterLog));

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, JigsawEnterLogParam request)
    {
        var player = context.Connection.Player!;
        if (JigsawPuzzleService.FindSchedule(request.ScheduleId) is null)
            return Task.FromResult(CallGSResult.NoResponse());

        JigsawPuzzleService.SetEnterTime(
            player, request.ScheduleId, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), null);

        Log.Info($"EnterLog: schedule={request.ScheduleId} type={request.EnterType} " +
                 $"for uid={player.Uid}.");

        return Task.FromResult(CallGSResult.NoResponse());
    }
}

public sealed class JigsawEnterLogParam
{
    [JsonPropertyName("nId")] public uint ScheduleId { get; set; }
    [JsonPropertyName("nEnterType")] public uint EnterType { get; set; }
}
