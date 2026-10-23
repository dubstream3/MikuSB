using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.StarWish;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.StarWish;

// 星祈心愿 tree-part lottery (DLC/StarWish/StarWishLogic.lua:1012-1045).
//
//     request : me:CallGS("StarWish_LotteryJigsaw", { nTimes })
//     reply   : { tbJigsaw = [id, ...] }            (sError tolerated)
//
// Each draw costs main.CashID = [61, 100] -- money type 61 (AttrIds.Currency.StarWish) at 100 per
// draw. The client checks the balance itself before sending (CheckScoreCanLottery, :543-556), so a
// refusal here is only reachable through a stale panel or a forged request; it answers sError so the
// widget shows the usual toast instead of a full screen spinner that never closes.
//
// tbJigsaw must only contain ids that exist in jigsaw.json: umg_starwish_gift.lua:38 builds the part
// from StarWishConfig.tbJigsaw[id] without a nil check. An empty list means "nothing left to draw"
// and the gift window closes immediately.
[CallGSApi("StarWish_LotteryJigsaw")]
public class StarWish_LotteryJigsaw : CallGSHandler<StarWishLotteryJigsawParam>
{
    private static readonly Logger Log = new(nameof(StarWish_LotteryJigsaw));

    /// <summary>Safety rail for forged payloads; the panel only ever sends 1 or 10.</summary>
    private const uint MaxTimes = 10;

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, StarWishLotteryJigsawParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var act = StarWishService.SelectAct(DateTime.Now);
        if (act == null || !StarWishService.IsOpen(player))
            return Task.FromResult(CallGSResult.Ok(new JsonObject { ["sError"] = "ui.TxtNotOpen" }));

        var times = request.Times == 0 ? 1u : Math.Min(request.Times, MaxTimes);
        var (moneyType, price) = act.LotteryCost;

        var sync = new NtfSyncPlayer();
        if (price > 0 && moneyType != 0)
        {
            var cost = price * times;
            if (!CurrencyService.TrySpend(player, moneyType, cost, sync, out var spendError))
            {
                Log.Info($"uid={player.Uid} cannot pay {cost}x moneyType={moneyType} for {times} draw(s): {spendError}");
                return Task.FromResult(CallGSResult.Ok(new JsonObject { ["sError"] = spendError }, sync));
            }
        }

        var drawn = new JsonArray();
        for (var i = 0; i < times; i++)
        {
            var id = StarWishService.RollJigsaw(player);
            if (id == 0)
                break;

            StarWishService.GrantJigsaw(player, id, sync);
            drawn.Add(id);
        }

        DatabaseHelper.SaveDatabaseType(player.Data);
        CurrencyService.Persist(player);

        Log.Info($"uid={player.Uid} drew {drawn.Count}/{times} part(s) for moneyType={moneyType} " +
                 $"price={price}: [{string.Join(", ", drawn)}].");

        return Task.FromResult(CallGSResult.Ok(new JsonObject { ["tbJigsaw"] = drawn }, sync));
    }
}

public sealed class StarWishLotteryJigsawParam
{
    [JsonPropertyName("nTimes")] public uint Times { get; set; }
}
