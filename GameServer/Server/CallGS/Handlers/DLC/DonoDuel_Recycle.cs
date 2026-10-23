using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

// 多诺-巅峰对决 卡牌回收 (分解成赛季代币).
//
// Client origin: DonoDuel:DoRecycle (DLC/DonoDuel/DonoDuel.lua:2076-2084) sends
// { tbRecycle = <card uniqueId list> }; the callback (:2086-2110) shows tbParam.sError, pays the
// UI-only Item.Gain(tbParam.tbRecycleAward) popup and refreshes the bag.
//
// IMPORTANT: Item.Gain is a pure popup (Item.lua:639-644 opens the "GainItem" window) - it neither
// grants currency nor touches attributes, so the season currency has to be credited here.
//
// Reward = donoduel_config ConfigID 13 (by star) + 14 (by quality) + 15 (every 1000 attribute points
// pays 3), matching DonoDuel:CalcRewards (:811-862). A card can only be recycled when it is not the
// whole card locked (flag bit0) and not part of a formation; the block is emptied, the stockroom
// counter drops by one and (185, 2) stays untouched because it is a monotonic allocator.
//
// Request:  { "tbRecycle": [<uniqueId>, ...] }
// Response: { "tbRecycle": [...], "tbRecycleAward": [63, total] } (plus the NtfSyncAttr push).
[CallGSApi("DonoDuel_Recycle")]
public class DonoDuel_Recycle : CallGSHandler
{
    private static readonly Logger Log = new(nameof(DonoDuel_Recycle));

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player!;

        var root = DonoDuelGrowth.ParseObject(param);
        var requested = DonoDuelGrowth.ReadUintList(root?["tbRecycle"]);
        if (requested.Count == 0)
            return DonoDuelGrowth.Bad();

        var sync = new NtfSyncPlayer();
        var recycled = new JsonArray();
        var (perAttr, rewardPerAttr) = DonoDuelConfig.RecycleAttr();
        var currency = DonoDuelGrowth.SeasonCurrency(player);
        uint total = 0;

        foreach (var uniqueId in requested)
        {
            if (!DonoDuelBag.HasCard(player, uniqueId))
                continue;

            if (DonoDuelBag.IsLocked(player, uniqueId) || DonoDuelBag.IsInAnyFormation(player, uniqueId))
            {
                Log.Info($"Donoduel card {uniqueId} is locked or deployed; not recycled (uid={player.Uid}).");
                continue;
            }

            var star = DonoDuelBag.GetStar(player, uniqueId);
            var quality = DonoDuelBag.GetQuality(player, uniqueId);
            var attributes = DonoDuelBag.GetAttr(player, uniqueId, DonoDuelGrowth.AttrAttack)
                             + DonoDuelBag.GetAttr(player, uniqueId, DonoDuelGrowth.AttrDefence)
                             + DonoDuelBag.GetAttr(player, uniqueId, DonoDuelGrowth.AttrHealth);

            var value = DonoDuelConfig.RecycleStar(star) + DonoDuelConfig.RecycleQuality(quality);
            if (perAttr > 0)
                value += attributes / perAttr * rewardPerAttr;

            total += value;
            DonoDuelBag.DestroyCard(player, uniqueId, sync);
            recycled.Add(uniqueId);

            Log.Info($"Donoduel card {uniqueId} recycled for {value} (star {star}, quality {quality}) by uid={player.Uid}.");
        }

        if (recycled.Count == 0)
            return DonoDuelGrowth.Bad();

        if (total > 0)
            player.AddCurrency(currency, total, sync);

        DonoDuelBag.Save(player);
        await player.SendPacket(CmdIds.NtfSyncAttr, sync);

        Log.Info($"Donoduel recycle by uid={player.Uid}: {recycled.Count} card(s), {total} currency {currency}.");

        return CallGSResult.Ok(new JsonObject
        {
            ["tbRecycle"] = recycled,
            ["tbRecycleAward"] = new JsonArray { currency, total }
        });
    }
}
