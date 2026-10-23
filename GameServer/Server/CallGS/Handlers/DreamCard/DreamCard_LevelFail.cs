using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using System.Text.Json;
using System.Text.Json.Nodes;

using MikuSB.Data;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DreamCard;

/// <summary>
/// 幸运卡牌 (DreamCard) 关卡失败上报.
///
/// The live client never calls this API directly: <c>DreamCard.LevelFail</c>
/// (DLC/DreamCard/DreamCard.lua:2318) hands the payload to <c>Reconnect.Send_SettleInfo</c>, so it arrives
/// through the <c>Chapter_DealLevelSettlement</c> channel with <c>sCmd = "DreamCard_LevelFail"</c>.
///
/// Without a router branch the payload fell through to the echo fallback, which returned the request's own
/// fields and nothing else: no <c>NewRecord</c>, no score record, no period id. The client callback
/// (DreamCard.lua:2321-2332) reads <c>nID</c>/<c>nType</c>/<c>nDiff</c> to resolve the level config and then
/// opens the <c>LuckySettlement</c> panel, so a failure run silently skipped its own结算画面.
///
/// The record bookkeeping mirrors <see cref="DreamCard_LevelSettlement"/> (challenge scores live in
/// <c>AttrIds.DreamCard.LevelGid</c> at <c>10 * nID + nDiff + 4</c>, the current challenge period sits at
/// sid 0), because the same panel is what both paths feed.
/// </summary>
internal static class DreamCard_LevelFail
{
    private const uint LevelGroupId = AttrIds.DreamCard.LevelGid;
    private const uint PeriodSid = 0;
    private const uint LevelSubNum = 10;
    private const int OrdinaryType = 1;
    private const int ChallengeType = 2;
    private const int EndlessType = 3;

    public static (JsonObject Response, NtfSyncPlayer Sync) HandleFail(PlayerInstance player, JsonNode? tbParam)
    {
        var req = tbParam?.Deserialize<DreamCardLevelSettlementParam>();
        var sync = new NtfSyncPlayer();
        if (req == null || req.LevelId <= 0 || req.Diff <= 0 || req.Type is < OrdinaryType or > EndlessType)
            return (new JsonObject { ["sErr"] = "error.BadParam" }, sync);

        var response = new JsonObject
        {
            ["nID"] = req.LevelId,
            ["nDiff"] = req.Diff,
            ["nType"] = req.Type,
            // The client opens the same settlement panel for a loss, so it needs a value for every field it
            // reads; ordinary runs never carry a score and endless runs keep no record table server side
            // (DreamCard_LevelSettlement.HandleEndless answers false as well).
            ["NewRecord"] = false
        };

        if (req.Type == ChallengeType)
        {
            var baseSid = LevelSubNum * (uint)req.LevelId;
            var scoreSid = baseSid + (uint)req.Diff + 4;
            var score = (uint)Math.Max(0, req.Score);
            var scoreAttr = player.Attributes.GetOrCreate(LevelGroupId, scoreSid);
            if (score > scoreAttr.Val)
            {
                scoreAttr.Val = score;
                player.Attributes.SyncTo(sync, scoreAttr);
                response["NewRecord"] = true;
            }

            var challengePeriodId = DreamCard_LevelSettlement.ResolveCurrentChallengePeriodId(DateTime.Now);
            if (challengePeriodId > 0)
            {
                var periodAttr = player.Attributes.GetOrCreate(LevelGroupId, PeriodSid);
                if (periodAttr.Val != challengePeriodId)
                {
                    periodAttr.Val = challengePeriodId;
                    player.Attributes.SyncTo(sync, periodAttr);
                }
            }
        }

        // A loss writes no pass counter and no difficulty/star bits (that is what the settlement path is
        // for), but the challenge score record above still has to survive a restart.
        DatabaseHelper.SaveDatabaseType(player.Data);

        return (response, sync);
    }
}
