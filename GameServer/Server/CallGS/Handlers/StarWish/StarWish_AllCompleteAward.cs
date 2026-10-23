using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.StarWish;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.StarWish;

// 星祈心愿 full-collection gift (DLC/StarWish/StarWishLogic.lua:1080-1130).
//
//     request : me:CallGS("StarWish_GetAllCompleteAward")   -- no body
//     reply   : { tbAward = [[G, D, P, L]] }   -- ONE four element row, count implied
//
// The callback reads tbParam.tbAward[1] and hands it to HouseMessageHandle as {{G,D,P,L,1}}: the gift
// is a dorm message (main.json DormGift = [8, 99, 34, 3]), not an inventory item, so there is nothing
// for the server to grant into the bag -- it only has to say yes once. A reply without the key is
// harmless, but tbAward = [] would be indexed at [1] and crash, so the "not eligible" answer is {}.
//
// The panel also fires this automatically the moment the 28th story quest completes (:1092-1097), and
// without the flag below that would re-send the gift on every re-report. AllCompleteGiftSid is never
// read by the client; it only makes the server idempotent.
[CallGSApi("StarWish_GetAllCompleteAward")]
public class StarWish_GetAllCompleteAward : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player;
        if (player == null)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var act = StarWishService.SelectAct(DateTime.Now);
        if (act == null || !StarWishService.IsOpen(player))
            return Task.FromResult(CallGSResult.Ok(new JsonObject()));

        if (StarWishService.IsAllCompleteGiftGiven(player))
            return Task.FromResult(CallGSResult.Ok(new JsonObject()));

        if (!StarWishService.AllStoryQuestsComplete(player, act))
            return Task.FromResult(CallGSResult.Ok(new JsonObject()));

        if (act.DormGift.Count == 0)
            return Task.FromResult(CallGSResult.Ok(new JsonObject()));

        var sync = new NtfSyncPlayer();
        StarWishService.MarkAllCompleteGiftGiven(player, sync);
        DatabaseHelper.SaveDatabaseType(player.Data);

        var row = new JsonArray();
        foreach (var value in act.DormGift[0].Take(4))
            row.Add(value);

        Logger.GetByClassName().Info($"uid={player.Uid} received the StarWish full-collection dorm gift.");

        return Task.FromResult(CallGSResult.Ok(new JsonObject
        {
            ["tbAward"] = new JsonArray { row }
        }, sync));
    }
}

[CallGSApi("StarWish_CompleteShareAchievement")]
public class StarWish_CompleteShareAchievement : CallGSHandler
{
    // The only StarWish api without an s2c.Register (grep over Script/ for
    // 'StarWish_CompleteShareAchievement' finds the sender and nothing else). Replying would hit the
    // unknown-api branch in Misc/S2C.lua:29-35, which raises a Lua error, so this must stay silent.
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param) =>
        Task.FromResult(CallGSResult.NoResponse());
}
