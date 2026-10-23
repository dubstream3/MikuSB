using MikuSB.Data;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Cash;

// 数据金 -> 体力 / 通用银 (Script/Cash/Exchange.lua:157-179 CashExchange.Exchange)
//   request : me:CallGS("Cash_Exchange", {nExhcangeCash, nCost, nExhcangTarget})   -- note the typos
//   reply   : MUST be {nCashType, nCount}
//
// Why the reply shape is not negotiable: Script/Cash/Exchange.lua:256-261 feeds the payload straight
// into UI.Open("GainItem", {{tbParam.nCashType, tbParam.nCount}}) and Misc/S2C.lua:29-36 never looks
// at sErr before dispatching. A CallGSResult.Error therefore arrives at the client as a table of nils,
// umg_general_gain_tips.lua:129 sees #tbItem == 0 instead of == 2 and falls into the item branch,
// and line 144 dies in string.format ('bad argument #2 to format (number expected, got nil)' -
// the 2026-10-10 03:56 report from uid 4). So every outcome an honest client can reach answers with
// numbers, and a refusal is {nCashType = <target>, nCount = 0}, which the UI renders as "0 gained".
// CallGSResult.Error is reserved for requests a real client cannot produce (forged target/source).
//
// The price comes from the server's own ladder row (used exchanges + 1). nCost is a hint only: a
// double click re-sends the previous row's cost while the ladder has already advanced, and after the
// 2026-10-10 incident a "reject on mismatch" would have to answer with an error and crash the UI.
// Charging the authoritative row keeps a tampered client from naming its own price - the same rule
// Item_Exchange and ShopPrice follow.
[CallGSApi("Cash_Exchange")]
public class Cash_Exchange : CallGSHandler<CashExchangeParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, CashExchangeParam req)
    {
        var player = context.Connection.Player!;

        // Only 数据金 (Gold) is ever the thing handed over: the client hardcodes MoneyType_Gold for
        // both ladders (Exchange.lua:110 and :134) and the third table (cash/cash_exchange.json) is
        // dead config whose rows carry no rate at all.
        if (req == null || req.Source != AttrIds.Currency.Gold)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var target = req.Target;
        if (target != AttrIds.Currency.Vigor && target != AttrIds.Currency.Silver)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var sid = target == AttrIds.Currency.Vigor
            ? AttrIds.CashExchange.VigorLimitSid
            : AttrIds.CashExchange.SilverLimitSid;

        // Group 70 counts how many exchanges of this kind have already happened, per kind; it is what
        // the client feeds into me:GetAttribute(70, sid) + 1 to pick its row.
        var used = player.Attributes.GetValue(AttrIds.CashExchange.GroupId, sid);
        var row = ResolveRow(target, used + 1);
        if (row == null)
            return Task.FromResult(Refuse(player, target, used, $"the {target} ladder has no row {used + 1}"));

        var (cost, gain) = row.Value;

        if (req.Cost != cost)
            Logger.GetByClassName().Warn(
                $"uid={player.Uid} offered {req.Cost}x moneyType={req.Source} for {target} " +
                $"while ladder row {used + 1} costs {cost}; charging the ladder price.");

        // Vigor is NOT clamped to the level cap on this path: the retail server hands the bought
        // points out even when the bar is already full (the client shows the overflow as a full bar
        // and stops regenerating until it drops under the cap again). This used to clamp to the
        // remaining room and refuse with nCount = 0 when full, which made a full-tank purchase look
        // like it silently did nothing - no stamina, no data gold spent.
        var sync = new NtfSyncPlayer();
        if (!CurrencyService.TrySpend(player, req.Source, cost, sync, out var spendError))
            return Task.FromResult(Refuse(player, target, used, $"cannot afford {cost} ({spendError})"));

        player.AddCurrency(target, gain, sync, allowVigorOverflow: target == AttrIds.Currency.Vigor);

        // Advance the ladder only after the charge went through, so a refused exchange never costs
        // the player a step.
        var attr = player.Attributes.Set(AttrIds.CashExchange.GroupId, sid, used + 1);
        player.Attributes.SyncTo(sync, attr);
        CurrencyService.Persist(player);

        Logger.GetByClassName().Info(
            $"uid={player.Uid} exchanged {cost}x moneyType={req.Source} for {gain}x moneyType={target} " +
            $"(step {used + 1}).");

        return Task.FromResult(CallGSResult.Ok(
            new JsonObject { ["nCashType"] = target, ["nCount"] = gain }, sync));
    }

    private static (uint Cost, uint Gain)? ResolveRow(uint target, uint step)
    {
        if (target == AttrIds.Currency.Vigor)
            return GameData.VigorExchangeData.TryGetValue(step, out var vigor)
                ? (vigor.Cost, vigor.Vigor)
                : null;

        return GameData.SilverExchangeData.TryGetValue(step, out var silver)
            ? (silver.Cost, silver.Silver)
            : null;
    }

    /// <summary>
    /// Refuses an exchange in the only shape the client survives: a numeric payload that renders as
    /// "gained 0". See the class comment - an sErr reply crashes umg_general_gain_tips.lua.
    /// </summary>
    private static CallGSResult Refuse(PlayerInstance player, uint target, uint used, string reason)
    {
        Logger.GetByClassName().Warn(
            $"uid={player.Uid} refused a {target} exchange at step {used + 1}: {reason}.");

        return CallGSResult.Ok(new JsonObject { ["nCashType"] = target, ["nCount"] = 0u });
    }
}

public sealed class CashExchangeParam
{
    /// <summary>moneyType handed over. The client always sends 2 (数据金).</summary>
    [JsonPropertyName("nExhcangeCash")] public uint Source { get; set; }

    /// <summary>Price the client's dialog showed. Advisory: the server charges its own ladder row.</summary>
    [JsonPropertyName("nCost")] public uint Cost { get; set; }

    /// <summary>moneyType received: 4 = 体力, 3 = 通用银.</summary>
    [JsonPropertyName("nExhcangTarget")] public uint Target { get; set; }
}
