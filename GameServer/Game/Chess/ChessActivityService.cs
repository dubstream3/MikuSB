using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Game.Chess;

// Shared state for the three board games that run on the chess/richman client code:
//   * ChessActivity   (chess.txt ID 1, DLC1 board)      -> ChessLogic_*   apis
//   * Richman         (chess.txt ID 2 and 4)            -> RichmanLogic_* apis
//   * RichmanLove     (chess.txt ID 3, dating board)    -> RichManLoveLogic_* apis
//
// The client decides which UI to open purely from the activity id the server echoes back
// (ChessLogic.lua:151-157, RichmanLogic.lua:418-419, RichManLoveLogic.lua:462), so every
// CheckOpenAct handler picks a row here and returns it verbatim.
//
// The boards themselves (money, cards, buildings) live in the client's local save; the server only
// records "this run started" and turns the settlement into a rank reward. That run record is the
// only defence against the client's worst behaviour: RichmanLogic wipes its own save *before*
// sending MapSettlement (RichmanLogic.lua:498-499) and never retries, so a lost packet must still
// be recoverable from (12, 210).
public static class ChessActivityService
{
    // Rank rewards are paid in the currency named by chess.txt's Award column when present; otherwise
    // in the "fans item" stack the client uses for the same payout (RichmanLogic.FansItem).
    private static readonly uint[] FansItem = [5, 25, 4, 1];

    public static ChessActivityExcel? Row(uint id) =>
        GameData.ChessActivityData.TryGetValue(id, out var row) ? row : null;

    public static ChessMapExcel? MapRow(uint activityId, uint mapId) =>
        GameData.ChessMapData.TryGetValue(activityId * 100 + mapId, out var row) ? row : null;

    // The tables store windows as "[202507100400]" (bracketed), the same shape the client strips with
    // string.sub(v, 2, -2) before ParseTime (ChessLogic.lua:43-44).
    public static long ToUnix(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return 0;
        return DlcRuntimeOverrides.ToUnixSeconds(DlcRuntimeOverrides.ParseConfigTime(raw.Trim().Trim('[', ']')));
    }

    /// <summary>Picks the row to open, in preference order, preferring a row whose window holds <paramref name="now"/>.</summary>
    public static ChessActivityExcel? SelectActivity(DateTime now, params uint[] prefer)
    {
        var nowUnix = DlcRuntimeOverrides.ToUnixSeconds(now);
        ChessActivityExcel? fallback = null;
        foreach (var id in prefer)
        {
            var row = Row(id);
            if (row == null)
                continue;
            fallback ??= row;
            var begin = ToUnix(row.Begin);
            var end = ToUnix(row.End);
            if ((begin == 0 || nowUnix >= begin) && (end == 0 || nowUnix < end))
                return row;
        }

        // Nothing in window: fall back to the newest row that already started, then the last prepared one.
        return GameData.ChessActivityData.Values
                   .Where(r => ToUnix(r.Begin) <= nowUnix)
                   .OrderByDescending(r => r.Id)
                   .FirstOrDefault()
               ?? fallback
               ?? GameData.ChessActivityData.Values.OrderByDescending(r => r.Id).FirstOrDefault();
    }

    public static uint SelectedId(DateTime now) => SelectActivity(now, 4, 2, 1, 3)?.Id ?? 0;

    /// <summary>
    /// Seeds the slots the client reads as configuration but never writes: the current activity id
    /// (12,0 -- ChessLogic.GetOpenID) and (12,201) CurrentActType, which selects between the
    /// (12, 10+id) and Launch.GPASSID first-clear counters.
    /// </summary>
    public static void Seed(PlayerInstance player, NtfSyncPlayer? sync)
    {
        var id = SelectedId(DateTime.Now);
        if (id != 0 && player.Attributes.GetValue(AttrIds.Chess.Gid, AttrIds.Chess.ActivitySid) != id)
        {
            var attr = player.Attributes.Set(AttrIds.Chess.Gid, AttrIds.Chess.ActivitySid, id);
            if (sync != null)
                player.Attributes.SyncTo(sync, attr);
        }

        if (player.Attributes.GetValue(AttrIds.Chess.Gid, AttrIds.Chess.CurrentActTypeSid) != 1)
        {
            var attr = player.Attributes.Set(AttrIds.Chess.Gid, AttrIds.Chess.CurrentActTypeSid, 1);
            if (sync != null)
                player.Attributes.SyncTo(sync, attr);
        }
    }

    public static uint MapEnterBits(PlayerInstance player) =>
        player.Attributes.GetValue(AttrIds.Chess.Gid, AttrIds.Chess.MapEnterSid);

    public static bool IsMapEntered(PlayerInstance player, uint mapId) =>
        mapId is > 0 and < 32 && ((MapEnterBits(player) >> (int)mapId) & 1) != 0;

    public static void SetMapEntered(PlayerInstance player, uint mapId, NtfSyncPlayer sync)
    {
        if (mapId is 0 or >= 32)
            return;
        var bits = MapEnterBits(player) | (1u << (int)mapId);
        var attr = player.Attributes.Set(AttrIds.Chess.Gid, AttrIds.Chess.MapEnterSid, bits);
        player.Attributes.SyncTo(sync, attr);
    }

    public static uint NewSeed() => (uint)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() & 0x7FFFFFFF);

    /// <summary>
    /// Records "this board run is in progress" and returns the seed the settlement must echo back.
    /// The seed is the only thing tying a MapSettlement request to a real EnterMap, because the client
    /// reports nothing else that the server can trust.
    /// </summary>
    public static uint OpenSession(
        PlayerInstance player,
        uint activityId,
        uint mapId,
        NtfSyncPlayer? sync,
        out bool first)
    {
        first = !IsMapEntered(player, mapId);
        var seed = NewSeed();

        var session = new JsonObject
        {
            ["seed"] = seed,
            ["act"] = activityId,
            ["map"] = mapId,
            ["at"] = (long)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["done"] = 0
        };

        var attr = player.Attributes.SetString(AttrIds.Chess.Gid, AttrIds.Chess.ActiveSessionSid, session.ToJsonString());
        if (sync != null)
            player.Attributes.SyncTo(sync, attr);

        if (first)
            SetMapEntered(player, mapId, sync ?? new NtfSyncPlayer());

        DatabaseHelper.SaveDatabaseType(player.Data);
        return seed;
    }

    public static JsonObject? ReadSession(PlayerInstance player)
    {
        var raw = player.Attributes.GetStringValue(AttrIds.Chess.Gid, AttrIds.Chess.ActiveSessionSid);
        if (string.IsNullOrEmpty(raw))
            return null;
        try
        {
            return JsonNode.Parse(raw) as JsonObject;
        }
        catch
        {
            return null;
        }
    }

    public static uint SessionUInt(JsonObject? session, string key) =>
        session?[key] is JsonValue value && value.TryGetValue(out uint parsed) ? parsed : 0;

    /// <summary>
    /// Validates and closes the run opened by <see cref="OpenSession"/>. Returns false for replays and
    /// for settlements of a run that never started, so rewards can only be paid once.
    /// </summary>
    public static bool TryCloseSession(PlayerInstance player, uint seed, uint activityId, uint mapId)
    {
        var session = ReadSession(player);
        if (session == null)
            return false;
        if (SessionUInt(session, "done") != 0)
            return false;

        if (seed != 0 && SessionUInt(session, "seed") != seed)
            return false;
        if (SessionUInt(session, "act") != activityId || SessionUInt(session, "map") != mapId)
            return false;

        session["done"] = 1;
        player.Attributes.SetString(AttrIds.Chess.Gid, AttrIds.Chess.ActiveSessionSid, session.ToJsonString());
        DatabaseHelper.SaveDatabaseType(player.Data);
        return true;
    }

    /// <summary>Pays the rank reward for one richman board and returns what the settlement UI shows.</summary>
    public static async Task<uint> PayRankRewardAsync(
        PlayerInstance player,
        uint activityId,
        uint mapId,
        uint rank,
        NtfSyncPlayer sync)
    {
        var map = MapRow(activityId, mapId);
        var amount = map?.RankAward(rank) ?? 0;
        if (amount == 0)
            amount = rank <= 1 ? 20u : 10u;

        var activity = Row(activityId);
        if (activity is { AwardId: > 0 })
        {
            player.AddCurrency(activity.AwardId, amount, sync);
        }
        else
        {
            IReadOnlyList<IReadOnlyList<uint>> rows = [[FansItem[0], FansItem[1], FansItem[2], FansItem[3], amount]];
            await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);
        }

        return amount;
    }

    // ---- vote / fans (Richman "vote for your girl") -------------------------------------------
    // VoteGirl itself is client side: it spends the fan item and calls the server only to record the
    // result. The tally lives in (118, 2*id+1), the claim bit in (118, 2*id+2); section 6 of the
    // contract documents why the leaderboard must expose npc ids "1".."8" rather than player ids.
    public static uint Votes(PlayerInstance player, uint npcId) =>
        player.Attributes.GetValue(AttrIds.Chess.RichmanFansGid, AttrIds.Chess.RichmanFansSidStride * npcId + 1);

    public static bool IsVoteRewarded(PlayerInstance player, uint npcId) =>
        player.Attributes.GetValue(AttrIds.Chess.RichmanFansGid, AttrIds.Chess.RichmanFansSidStride * npcId + 2) != 0;

    public static void AddVotes(PlayerInstance player, uint npcId, uint count, NtfSyncPlayer sync)
    {
        var attr = player.Attributes.Add(AttrIds.Chess.RichmanFansGid,
            AttrIds.Chess.RichmanFansSidStride * npcId + 1, count);
        player.Attributes.SyncTo(sync, attr);
    }

    public static void MarkVoteRewarded(PlayerInstance player, uint npcId, NtfSyncPlayer sync)
    {
        var attr = player.Attributes.Set(AttrIds.Chess.RichmanFansGid,
            AttrIds.Chess.RichmanFansSidStride * npcId + 2, 1);
        player.Attributes.SyncTo(sync, attr);
    }

    // ---- richman-love ending slots (gid 131) --------------------------------------------------
    public static bool IsEndUnlocked(PlayerInstance player, uint endId) =>
        player.Attributes.GetValue(AttrIds.RichmanLove.EndGid, endId) != 0;

    public static bool IsEndLiked(PlayerInstance player, uint endId) =>
        player.Attributes.GetValue(AttrIds.RichmanLove.EndGid, 25 + endId) != 0;

    public static void MarkEndLiked(PlayerInstance player, uint endId, NtfSyncPlayer sync)
    {
        var attr = player.Attributes.Set(AttrIds.RichmanLove.EndGid, 25 + endId, 1);
        player.Attributes.SyncTo(sync, attr);
    }
}
