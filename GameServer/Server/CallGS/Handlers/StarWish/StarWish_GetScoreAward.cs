using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.StarWish;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.StarWish;

// 星祈心愿 collection-score rewards (DLC/StarWish/StarWishLogic.lua:990-1010).
//
//     request : me:CallGS("StarWish_GetScoreAward")   -- no body
//     reply   : { tbAwardList = [[G, D, P, L, N], ...] }
//
// The callback is entered through UI.Call2('StarWishMain', 'OnRspGetScoreAward'), which reads sError
// first (a non-empty string shows a toast and returns) and then Item.Gain(tbAwardList). The button
// only unlocks when the local jigsaw collection score reaches a tier, so the server recomputes that
// score from the ownership bitmap instead of trusting the panel.
//
// Only tiers above the last claimed one are granted. The claimed tier is stored in
// me:GetAttribute(StarWish, ScoreAwardGotSid); the client compares the tier id against it to grey out
// the row, so it has to move forward with the claim.
[CallGSApi("StarWish_GetScoreAward")]
public class StarWish_GetScoreAward : CallGSHandler
{
    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player;
        if (player == null)
            return CallGSResult.Error("error.BadParam");

        var act = StarWishService.SelectAct(DateTime.Now);
        if (act == null)
            return CallGSResult.Ok(new JsonObject { ["tbAwardList"] = new JsonArray() });

        if (!StarWishService.IsOpen(player))
            return CallGSResult.Ok(new JsonObject { ["tbAwardList"] = new JsonArray() });

        var score = StarWishService.Score(player);
        var claimed = player.Attributes.GetValue(AttrIds.StarWish.Gid, AttrIds.StarWish.ScoreAwardGotSid);

        var claimable = GameData.StarWishScoreAwardData.Values
            .Where(tier => tier.Score <= score && tier.Id > claimed)
            .OrderBy(tier => tier.Score)
            .ThenBy(tier => tier.Id)
            .ToList();

        if (claimable.Count == 0)
            return CallGSResult.Ok(new JsonObject { ["tbAwardList"] = new JsonArray() });

        var rows = new List<IReadOnlyList<uint>>();
        foreach (var tier in claimable)
        {
            foreach (var row in tier.Awards)
                rows.Add(row);
        }

        var sync = new NtfSyncPlayer();
        var granted = rows.Count > 0
            ? await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync)
            : new JsonArray();

        StarWishService.Set(player, AttrIds.StarWish.ScoreAwardGotSid, claimable[^1].Id, sync);
        DatabaseHelper.SaveDatabaseType(player.Data);

        return CallGSResult.Ok(new JsonObject { ["tbAwardList"] = granted }, sync);
    }
}
