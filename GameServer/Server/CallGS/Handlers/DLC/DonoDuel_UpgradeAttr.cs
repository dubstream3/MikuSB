using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

// 多诺-巅峰对决 卡牌属性升级 (攻击/防御/生命 +N).
//
// Client origin: DonoDuel:DoUpgradeAttr (DLC/DonoDuel/DonoDuel.lua:2305-2319) sends
// { nUniqueId, nPos } (nPos 1 = attack, 2 = defence, 3 = health) after UI.ShowConnection(); the
// callback (:2321-2339) only reacts to tbParam.sError and otherwise re-reads group 185 through
// DonoDuelBag/DonoDuelFormation:OnReceiveUpdate.
//
// Cost and gains come from donoduel_config ConfigID 1/23/24 ([[quality, cost, min, max], ...]) and
// are paid with the season currency (63). The ceiling is the herocard's per-quality map - the client
// refuses a further upgrade at the cap with error.condition_limit, so the server mirrors that.
//
// Request:  { "nUniqueId": <1..350>, "nPos": 1|2|3 }
// Response: { } on success, { "sError": "..." } otherwise (plus the NtfSyncAttr push).
[CallGSApi("DonoDuel_UpgradeAttr")]
public class DonoDuel_UpgradeAttr : CallGSHandler
{
    private static readonly Logger Log = new(nameof(DonoDuel_UpgradeAttr));

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player!;

        var root = DonoDuelGrowth.ParseObject(param);
        var uniqueId = DonoDuelGrowth.ReadUint(root?["nUniqueId"]);
        var position = DonoDuelGrowth.ReadUint(root?["nPos"]);

        if (!DonoDuelBag.HasCard(player, uniqueId) || position is < DonoDuelGrowth.AttrAttack or > DonoDuelGrowth.AttrHealth)
        {
            Log.Warn($"Donoduel attribute upgrade for unknown card {uniqueId} pos={position} from uid={player.Uid}.");
            return DonoDuelGrowth.Bad();
        }

        var quality = DonoDuelBag.GetQuality(player, uniqueId);
        var (cost, gainMin, gainMax) = DonoDuelConfig.AttrGain(position, quality);
        if (cost == 0 || gainMax == 0)
        {
            Log.Warn($"No attribute gain row for quality {quality} pos={position} (uid={player.Uid}).");
            return DonoDuelGrowth.Bad();
        }

        var templateId = DonoDuelBag.GetTemplateId(player, uniqueId);
        var cap = DonoDuelGrowth.AttrCap(templateId, quality, position);
        if (cap == 0)
        {
            Log.Warn($"Hero card {templateId} has no ceiling for pos={position} quality={quality}.");
            return DonoDuelGrowth.Bad();
        }

        var current = DonoDuelBag.GetAttr(player, uniqueId, position);
        if (current >= cap)
            return DonoDuelGrowth.Bad("error.condition_limit");

        var sync = new NtfSyncPlayer();
        if (!CurrencyService.TrySpend(player, DonoDuelGrowth.SeasonCurrency(player), cost, sync, out var error))
            return DonoDuelGrowth.Bad(error);

        var gain = (uint)Random.Shared.Next((int)gainMin, (int)Math.Max(gainMin, gainMax) + 1);
        var updated = Math.Min(current + gain, cap);
        DonoDuelBag.SetAttr(player, uniqueId, position, updated, sync);
        DonoDuelBag.Save(player);
        await player.SendPacket(CmdIds.NtfSyncAttr, sync);

        Log.Info(
            $"Donoduel card {uniqueId} attribute {position} {current} -> {updated} (+{gain}, cap {cap}, " +
            $"cost {cost}) for uid={player.Uid}.");

        return DonoDuelGrowth.Ok();
    }
}
