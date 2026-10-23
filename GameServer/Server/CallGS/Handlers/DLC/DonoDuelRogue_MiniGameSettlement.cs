using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

// 多诺-巅峰对决 抓捕玩法: 小游戏(转盘)结算.
//
// Client origin: DonoDuelRogue:Req_MiniGameAward (DLC/DonoDuel/DonoDuelRogue.lua:801-809) sends
// { nSeed, nRoomId } after UI.ShowConnection(); the callback (:811-821) hands tbParam.nUniqueId to the
// in-run fight bag when it is non-zero and then fires Event.OnDonoRogueTurntable, whose listener
// (UMG/DonoDuel/umg_donoduel_turntable.lua:36-44) spins the wheel to the slot named by
// tbParam.nAwardId - i.e. the pool row's Sort - and only then opens the GainItem popup.
//
// The pool is donoduel_catch_room_minigame.json (client twin: DonoDuelRogue.lua:131-152):
//   RewardType 1 -> Reward1 = [g, d, p, l, count]      : a normal item grant
//   RewardType 2 -> Reward2 = [heroCard, star, quality]: a Donoduel hero card block
// A roll costs season currency and is limited to donoduel_config ConfigID 17 = [plays, price];
// the spent count lives in (185, 504) = DonoDuelRogue.MiniGameNum, which the widget renders as
// TotalCount - used (umg_donoduel_turntable.lua:78, :140).
//
// Request:  { "nSeed": <u32>, "nRoomId": <u32> }
// Response: { "nAwardId": <wheel slot>, "nUniqueId": <card block, when a card was awarded> }
[CallGSApi("DonoDuelRogue_MiniGameSettlement")]
public class DonoDuelRogue_MiniGameSettlement : CallGSHandler<DonoDuelRogueMiniGameParam>
{
    private static readonly Logger Log = new(nameof(DonoDuelRogue_MiniGameSettlement));

    /// <summary>donoduel_config.json ConfigID 17 = [plays per run, season currency per roll].</summary>
    private const uint TurntableConfigId = 17;

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, DonoDuelRogueMiniGameParam request)
    {
        var player = context.Connection.Player!;
        var sync = new NtfSyncPlayer();

        var config = GameData.DonoDuelConfigData.GetValueOrDefault(TurntableConfigId)?.Flat();
        var totalPlays = config is { Count: > 0 } ? config[0] : 0u;
        var price = config is { Count: > 1 } ? config[1] : 0u;
        if (totalPlays == 0)
        {
            Log.Warn("donoduel_config.json has no ConfigID 17 row; the turntable has no play budget.");
            return DonoDuelGrowth.Bad();
        }

        var used = player.Attributes.GetValue(AttrIds.DonoDuel.GroupId, AttrIds.DonoDuel.RogueMiniGameUsedSid);
        if (used >= totalPlays)
            return DonoDuelGrowth.Bad("error.condition_limit");

        var row = Roll();
        if (row == null)
        {
            Log.Warn("The donoduel turntable pool is empty.");
            return DonoDuelGrowth.Bad();
        }

        if (price > 0 &&
            !CurrencyService.TrySpend(player, DonoDuelGrowth.SeasonCurrency(player), price, sync, out var error))
            return DonoDuelGrowth.Bad(error);

        var response = new JsonObject { ["nAwardId"] = row.Sort };

        if (row.RewardType == 2 && row.Reward2.Count >= 3)
        {
            var granted = DonoDuelBag.GrantCards(player, [(row.Reward2[0], row.Reward2[1], row.Reward2[2])], sync);
            if (granted.Count > 0)
                response["nUniqueId"] = granted[0];
            else
                Log.Warn($"The bag is full; turntable card {row.Reward2[0]} was dropped for uid={player.Uid}.");
        }
        else if (row.Reward1.Count >= 5)
        {
            await player.RewardManager.GrantConfiguredRewardsAsync([row.Reward1], sync);
        }
        else
        {
            Log.Warn($"Turntable row {row.Id} carries no usable reward payload (type {row.RewardType}).");
            return DonoDuelGrowth.Bad();
        }

        DonoDuelBag.SetSlot(
            player, AttrIds.DonoDuel.RogueMiniGameUsedSid, Math.Min(used + 1, totalPlays), sync);
        DonoDuelBag.Save(player);

        Log.Info(
            $"Donoduel turntable in room {request.RoomId} (seed {request.Seed}) rolled slot {row.Sort} " +
            $"(row {row.Id}, type {row.RewardType}) for uid={player.Uid}; plays {used + 1}/{totalPlays}, " +
            $"paid {price} season currency.");

        return CallGSResult.Ok(response, sync);
    }

    /// <summary>
    /// Weighted pick over the 18 row pool (donoduel_catch_room_minigame.json Weight column). Rows with
    /// weight 0 are never drawn; the last one is the fallback for rounding.
    /// </summary>
    private static DonoDuelCatchRoomMiniGameExcel? Roll()
    {
        var pool = new List<DonoDuelCatchRoomMiniGameExcel>();
        var total = 0ul;
        foreach (var row in GameData.DonoDuelMiniGameData.Values)
        {
            if (row.Weight == 0)
                continue;

            pool.Add(row);
            total += row.Weight;
        }

        if (pool.Count == 0 || total == 0)
            return null;

        var pick = (ulong)Random.Shared.NextInt64((long)total);
        foreach (var row in pool)
        {
            if (pick < row.Weight)
                return row;

            pick -= row.Weight;
        }

        return pool[^1];
    }
}

public sealed class DonoDuelRogueMiniGameParam
{
    [JsonPropertyName("nSeed")]
    public uint Seed { get; set; }

    [JsonPropertyName("nRoomId")]
    public uint RoomId { get; set; }
}
