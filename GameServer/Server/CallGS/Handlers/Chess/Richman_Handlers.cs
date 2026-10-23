using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.GameServer.Game.Achievement;
using MikuSB.GameServer.Game.Chess;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chess;

// Richman board (chess.txt ID 2 / 4) and the dating board (ID 3). Both share the chess attribute
// group: (52, mapId) holds the board save, (121, mapId*10+{1,2,3}) the per-map money/entered/round
// counters, (118, 2*npc) the fan votes, (131, ...) the dating endings.
//
// The single most dangerous behaviour lives here: RichmanLogic erases its own save *before* sending
// MapSettlement (RichmanLogic.lua:498-499) and never retries, and both MapSettlement handlers are
// plain me:CallGS (not the Reconnect wrapper). So the server must (a) record the run in EnterMap,
// (b) validate the seed, (c) settle exactly once, and (d) never answer with an error packet -- an
// error still opens the settlement window, just with RewardNum 0.
[CallGSApi("RichmanLogic_CheckOpenAct")]
public class RichmanLogic_CheckOpenAct : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var row = ChessActivityService.SelectActivity(DateTime.Now, 4, 2);
        if (row == null || row.Id is not (2 or 4))
            return Task.FromResult(CallGSResult.Ok(new JsonObject { ["bOpen"] = false }));

        return Task.FromResult(CallGSResult.Ok(new JsonObject
        {
            ["bOpen"] = true,
            ["nId"] = row.Id,
            ["nBeginTime"] = now - 3600,
            ["nEndTime"] = now + 30L * 24 * 60 * 60
        }));
    }
}

// Reply keys: nId/nMapId (map load), nSeed (kept for the settlement), First (1 = play the opening
// plot before loading). tbLog is the client replaying its previous save and is not trusted.
[CallGSApi("RichmanLogic_EnterMap")]
public class RichmanLogic_EnterMap : CallGSHandler<RichmanEnterMapParam>
{
    private static readonly Logger Log = new(nameof(RichmanLogic_EnterMap));

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, RichmanEnterMapParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var sync = new NtfSyncPlayer();
        var seed = ChessActivityService.OpenSession(player, request.Id, request.MapId, sync, out var first);
        Log.Info($"uid={player.Uid} entered richman activity={request.Id} map={request.MapId} " +
                 $"continue={request.Continue} seed={seed} first={first}.");

        return Task.FromResult(CallGSResult.Ok(new JsonObject
        {
            ["nId"] = request.Id,
            ["nMapId"] = request.MapId,
            ["nSeed"] = seed,
            ["First"] = first ? 1 : 0
        }, sync));
    }
}

// tbLog[4] is the finish reason (1 TotalDeath+money, 2 TotalDeath+no money, 3 RoundOver rank 1,
// 4 RoundOver rank >1), tbLog[5] the money carried out, tbLog[6] "<lv1>-<lv2>-<lv3>". nRank is
// 1-based among the seven NPCs plus the player. Only RewardNum is read back (RichmanLogic.lua:537-548).
[CallGSApi("RichmanLogic_MapSettlement")]
public class RichmanLogic_MapSettlement : CallGSHandler<RichmanSettlementParam>
{
    private static readonly Logger Log = new(nameof(RichmanLogic_MapSettlement));

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, RichmanSettlementParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return await Task.FromResult(CallGSResult.Error("error.BadParam"));

        var sync = new NtfSyncPlayer();
        var activityId = request.ActivityId != 0 ? request.ActivityId : ChessActivityService.SelectedId(DateTime.Now);
        var mapId = request.MapId;

        if (!ChessActivityService.TryCloseSession(player, request.Seed, activityId, mapId))
        {
            // A replay, or a run the server never saw. Paying twice is worse than paying zero: the
            // client already wiped its own save, so a duplicate payout is the only irreversible loss.
            Log.Warn($"uid={player.Uid} richman settlement without a matching session " +
                     $"(activity={activityId} map={mapId} seed={request.Seed}); replay or missing EnterMap.");
            return CallGSResult.Ok(new JsonObject { ["RewardNum"] = 0 });
        }

        var reward = await ChessActivityService.PayRankRewardAsync(player, activityId, mapId, request.Rank, sync);
        Log.Info($"uid={player.Uid} richman activity={activityId} map={mapId} rank={request.Rank} " +
                 $"reason={request.Log?[3]} reward={reward}.");

        return CallGSResult.Ok(new JsonObject { ["RewardNum"] = reward }, sync);
    }
}

// The vote itself is client side (it spends the fan item locally); this call only records it. Reply is
// read as { nId, nNum } by RichmanLogic.lua:572-585 to refresh the local tally.
[CallGSApi("RichmanLogic_VoteGirl")]
public class RichmanLogic_VoteGirl : CallGSHandler<RichmanVoteParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, RichmanVoteParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        if (request.NpcId is 0 or > 8 || request.Count == 0)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var sync = new NtfSyncPlayer();
        ChessActivityService.AddVotes(player, request.NpcId, request.Count, sync);

        return Task.FromResult(CallGSResult.Ok(new JsonObject
        {
            ["nId"] = request.NpcId,
            ["nNum"] = request.Count
        }, sync));
    }
}

// Tier reward for the fan votes: nId is the chess_richmanawards row (1..8), whose Vote is the
// threshold and Award the payout row. The claim bit the client checks is (118, 2*id+2).
[CallGSApi("RichmanLogic_ReceiveVoteveReward")]
public class RichmanLogic_ReceiveVoteveReward : CallGSHandler<RichmanVoteRewardParam>
{
    private static readonly Logger Log = new(nameof(RichmanLogic_ReceiveVoteveReward));

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, RichmanVoteRewardParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return CallGSResult.Error("error.BadParam");

        if (!GameData.ChessRichmanAwardData.TryGetValue(request.NpcId, out var tier))
            return CallGSResult.Error("error.BadParam");

        if (ChessActivityService.IsVoteRewarded(player, request.NpcId))
            return CallGSResult.Error("achievement.RewardGeted");

        if (ChessActivityService.Votes(player, request.NpcId) < tier.Vote)
        {
            Log.Warn($"uid={player.Uid} claimed vote reward {request.NpcId} below the threshold " +
                     $"({ChessActivityService.Votes(player, request.NpcId)}/{tier.Vote}).");
            return CallGSResult.Error("ui.TxtNotOpen");
        }

        var sync = new NtfSyncPlayer();
        var granted = new JsonArray();
        var award = tier.Award;
        if (award.Count >= 4)
        {
            IReadOnlyList<IReadOnlyList<uint>> rows = [[award[0], award[1], award[2], award[3], 1]];
            var result = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);
            foreach (var row in result)
            {
                if (row != null)
                    granted.Add(row.DeepClone());
            }
        }

        ChessActivityService.MarkVoteRewarded(player, request.NpcId, sync);
        return CallGSResult.Ok(new JsonObject { ["tbAward"] = granted, ["nId"] = request.NpcId }, sync);
    }
}

[CallGSApi("RichManLoveLogic_CheckOpenAct")]
public class RichManLoveLogic_CheckOpenAct : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var row = ChessActivityService.Row(3);
        if (row == null)
            return Task.FromResult(CallGSResult.Ok(new JsonObject { ["bOpen"] = false }));

        return Task.FromResult(CallGSResult.Ok(new JsonObject
        {
            ["bOpen"] = true,
            ["nId"] = 3u,
            ["nBeginTime"] = now - 3600,
            ["nEndTime"] = now + 30L * 24 * 60 * 60
        }));
    }
}

[CallGSApi("RichManLoveLogic_EnterMap")]
public class RichManLoveLogic_EnterMap : CallGSHandler<RichmanEnterMapParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, RichmanEnterMapParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var sync = new NtfSyncPlayer();
        var seed = ChessActivityService.OpenSession(player, 3, request.MapId, sync, out var first);

        return Task.FromResult(CallGSResult.Ok(new JsonObject
        {
            ["nId"] = 3u,
            ["nMapId"] = request.MapId,
            ["nSeed"] = seed,
            ["First"] = first ? 1 : 0
        }, sync));
    }
}

// RichManLoveLogic.lua:504-511 sends this after the map loads and registers no callback at all.
[CallGSApi("RichManLoveLogic_EnterMapLog")]
public class RichManLoveLogic_EnterMapLog : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param) =>
        Task.FromResult(CallGSResult.NoResponse());
}

// activityId is hard coded to 2 by the client even when the dating board (3) is running
// (RichManLoveLogic.lua:539-545), so the run is identified by map and context instead.
[CallGSApi("RichManLoveLogic_MapSettlement")]
public class RichManLoveLogic_MapSettlement : CallGSHandler<RichmanSettlementParam>
{
    private static readonly Logger Log = new(nameof(RichManLoveLogic_MapSettlement));

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, RichmanSettlementParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var closed = ChessActivityService.TryCloseSession(player, request.Seed, 3, request.MapId);
        Log.Info($"uid={player.Uid} richman-love map={request.MapId} seed={request.Seed} settled={closed}.");
        return Task.FromResult(CallGSResult.Ok(new JsonObject()));
    }
}

// Unlocking an ending writes (131, id); the client reads it back through RichManLoveLogic.IsUnlock.
[CallGSApi("RichManLoveLogic_SettlementUnlockEnd")]
public class RichManLoveLogic_SettlementUnlockEnd : CallGSHandler<RichmanUnlockEndParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, RichmanUnlockEndParam request)
    {
        var player = context.Connection.Player;
        if (player == null || request.EndId == 0)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var sync = new NtfSyncPlayer();
        var attr = player.Attributes.Set(AttrIds.RichmanLove.EndGid, request.EndId, 1);
        player.Attributes.SyncTo(sync, attr);

        return Task.FromResult(CallGSResult.Ok(new JsonObject { ["nId"] = request.EndId }, sync));
    }
}

// "Like" on an ending: writes (131, 25+id). The ranking page then refreshes through QueryRank on
// "RichManLoveEndLike" (umg_richmanlove_endMessage.lua:22-27).
[CallGSApi("RichManLoveLogic_LikeEnd")]
public class RichManLoveLogic_LikeEnd : CallGSHandler<RichmanLikeEndParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, RichmanLikeEndParam request)
    {
        var player = context.Connection.Player;
        if (player == null || request.EndId == 0)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        if (!ChessActivityService.IsEndUnlocked(player, request.EndId))
            return Task.FromResult(CallGSResult.Error("ui.TxtArmLock4"));

        var sync = new NtfSyncPlayer();
        ChessActivityService.MarkEndLiked(player, request.EndId, sync);
        return Task.FromResult(CallGSResult.Ok(new JsonObject { ["nId"] = request.EndId }, sync));
    }
}

// Sent from RichmanLogic.lua:607-611 even though the api carries the love prefix. tbAwards is a list
// of 5 element rows; the client merges the 4-1-4-1/2/3 tiers itself (RichmanLogic.lua:628-630).
[CallGSApi("RichManLoveLogic_QuickGetReward")]
public class RichManLoveLogic_QuickGetReward : CallGSHandler<RichmanIdListParam>
{
    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, RichmanIdListParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return CallGSResult.Error("error.BadParam");

        var sync = new NtfSyncPlayer();
        var granted = new JsonArray();

        foreach (var id in request.Ids)
        {
            if (!GameData.ActivityQuestData.TryGetValue(id, out var quest))
                continue;

            var rows = AchievementClaimService.ReadRewardRows(quest.Rewards);
            var result = await AchievementClaimService.TryClaimAsync(
                player, AttrIds.Achievement.QuestGroupId, id, quest.CheckValue, rows, sync);

            if (result == null)
                continue;

            foreach (var row in result)
            {
                if (row != null)
                    granted.Add(row.DeepClone());
            }
        }

        return CallGSResult.Ok(new JsonObject { ["tbAwards"] = granted }, sync);
    }
}

// Both dice apis are dead on the client (ReceiveDice's send call and handler are commented out,
// UseDice's only call sites are commented), but they still have s2c registrations, so an empty object
// keeps them out of the "ui.TxtNotOpen" fallback if they ever come back.
[CallGSApi("RichManLoveLogic_UseDice")]
public class RichManLoveLogic_UseDice : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param) =>
        Task.FromResult(CallGSResult.Ok(new JsonObject()));
}

[CallGSApi("RichManLoveLogic_ReceiveDice")]
public class RichManLoveLogic_ReceiveDice : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param) =>
        Task.FromResult(CallGSResult.Ok(new JsonObject()));
}

public sealed class RichmanEnterMapParam
{
    [JsonPropertyName("nId")] public uint Id { get; set; }
    [JsonPropertyName("nMapId")] public uint MapId { get; set; }
    [JsonPropertyName("bContinue")] public bool Continue { get; set; }
    [JsonPropertyName("tbLog")] public JsonNode? Log { get; set; }
}

public sealed class RichmanSettlementParam
{
    [JsonPropertyName("nSeed")] public uint Seed { get; set; }
    [JsonPropertyName("activityId")] public uint ActivityId { get; set; }
    [JsonPropertyName("activityType")] public uint ActivityType { get; set; }
    [JsonPropertyName("mapId")] public uint MapId { get; set; }
    [JsonPropertyName("nRank")] public uint Rank { get; set; }
    [JsonPropertyName("tbLevelNum")] public JsonNode? LevelNum { get; set; }
    [JsonPropertyName("tbLog")] public JsonNode? Log { get; set; }
}

public sealed class RichmanVoteParam
{
    [JsonPropertyName("nId")] public uint NpcId { get; set; }
    [JsonPropertyName("nNum")] public uint Count { get; set; }
    [JsonPropertyName("nTotalNum")] public uint TotalCount { get; set; }
}

public sealed class RichmanVoteRewardParam
{
    [JsonPropertyName("nId")] public uint NpcId { get; set; }
}

public sealed class RichmanUnlockEndParam
{
    [JsonPropertyName("nMapId")] public uint MapId { get; set; }
    [JsonPropertyName("nId")] public uint EndId { get; set; }
    [JsonPropertyName("tbLog")] public JsonNode? Log { get; set; }
}

public sealed class RichmanLikeEndParam
{
    [JsonPropertyName("nId")] public uint EndId { get; set; }
}

public sealed class RichmanIdListParam
{
    [JsonPropertyName("tbIdList")] public List<uint> Ids { get; set; } = [];
}
