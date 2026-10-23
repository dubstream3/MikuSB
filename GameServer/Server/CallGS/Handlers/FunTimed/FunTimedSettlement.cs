using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.FunTimed;

// 抢滩登陆 (Sevendays / FunTimed) level settlement.
//
// The run does not settle through its own API: FunTimed.LevelSettlement (Launch/FunTimed/FunTimed.lua:372)
// wraps the payload in Reconnect.Send_SettleInfo, which turns it into a Chapter_DealLevelSettlement call
// with sCmd = "FunTimed_LevelSettlement" and tbParam = { nID, nSeed, nStar, nScore, tbLog }. The wrapper
// retries five times two seconds apart and force-disconnects after that (Misc/Reconnect.lua:144-168), so
// this branch has to answer every time and every write below is idempotent.
//
// Progress lives in two plain Launch attributes that the client reads back when it paints the star row:
//   (Launch.GID   = 21, levelID) -> star bits, value 1 / 3 / 7 for 1 / 2 / 3 stars (FunTimed.lua:68-79)
//   (Launch.GPASSID = 22, levelID) -> best score (FunTimed.lua:84-86)
//
// Star bits double as the reward ledger. StarAwards holds one tier per star (level.json, three nested
// levels), the client can replay a level (bAgainFight), and the reward is only ever owed for a tier the
// player just reached; granting "all tiers up to nStar" on every clear would let a 3 star replay mint the
// tier rewards forever. So only the tiers whose bit is newly set are paid out, and the reply carries just
// those rows (FunTimed.lua:382-392 shows whatever tbAwards contains).
public static class FunTimedSettlement
{
    private static readonly Logger Log = new(nameof(FunTimedSettlement));

    private const uint StarGroupId = AttrIds.Launch.LevelStateGid; // Launch.GID = 21
    private const uint ScoreGroupId = AttrIds.Launch.LevelPassGid; // Launch.GPASSID = 22
    private const uint StarMask = 0x7u;

    public static async ValueTask<(JsonNode Payload, NtfSyncPlayer? Sync)> HandleSettlementAsync(
        PlayerInstance player,
        JsonNode? tbParam)
    {
        var levelId = ReadUint(tbParam, "nID");
        var starCount = Math.Min(ReadUint(tbParam, "nStar"), 3);
        var score = ReadUint(tbParam, "nScore");

        if (levelId == 0 || !GameData.FunTimedLevelData.TryGetValue(levelId, out var level))
        {
            // Unknown id: answer with an empty, well formed payload instead of falling through to the
            // echo -- the echo would hand the raw request back and the client would treat nStar as an
            // award list.
            Log.Warn($"FunTimed settlement for unknown level {levelId}; answering with no awards.");
            return (new JsonObject { ["tbAwards"] = new JsonArray() }, null);
        }

        // (1 << n) - 1, i.e. 1 / 3 / 7 for one / two / three stars.
        var mask = starCount == 0 ? 0u : (uint)((1 << (int)starCount) - 1) & StarMask;

        var sync = new NtfSyncPlayer();

        var starAttr = player.Attributes.GetOrCreate(StarGroupId, levelId);
        var newlyReached = mask & ~starAttr.Val & StarMask;
        var oldBits = starAttr.Val;
        starAttr.Val = (starAttr.Val & ~StarMask) | mask;
        player.Attributes.SyncTo(sync, starAttr);

        var scoreAttr = player.Attributes.GetOrCreate(ScoreGroupId, levelId);
        if (score > scoreAttr.Val)
        {
            scoreAttr.Val = score;
            player.Attributes.SyncTo(sync, scoreAttr);
        }

        var rows = new List<uint[]>();
        for (var tier = 1; tier <= 3; tier++)
        {
            var bit = 1u << (tier - 1);
            if ((newlyReached & bit) == 0)
                continue;

            foreach (var row in level.TierAwards(tier))
            {
                if (row.Count >= 5)
                    rows.Add([row[0], row[1], row[2], row[3], row[4]]);
            }
        }

        var granted = rows.Count == 0
            ? new JsonArray()
            : await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);

        DatabaseHelper.SaveDatabaseType(player.Data);
        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);

        Log.Info($"FunTimed level {levelId} settled by uid={player.Uid} (stars={starCount}, score={score}, " +
                 $"oldBits={oldBits}, newBits={newlyReached}, {rows.Count} reward row(s)): {granted.ToJsonString()}");

        return (new JsonObject
        {
            ["tbAwards"] = granted,
            ["nStar"] = starCount,
            ["nScore"] = scoreAttr.Val
        }, sync);
    }

    private static uint ReadUint(JsonNode? node, string key)
    {
        if (node is not JsonObject obj)
            return 0;

        var raw = obj[key]?.ToString().Trim().Trim('[', ']', '"');
        return uint.TryParse(raw, out var value) ? value : 0;
    }
}
