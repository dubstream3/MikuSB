using MikuSB.Data;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

// 多诺-巅峰对决 卡牌品质(潜力)提升.
//
// Client origin: DonoDuel:DoUpgradeQuality (DLC/DonoDuel/DonoDuel.lua:2206-2218) sends { nUniqueId }
// and the target is always current + 1; the callback (:2220-2238) only reacts to tbParam.sError.
//
// donoduel_config: ConfigID 2 = the price per target quality {2:700, 3:1500, 4:2800, 5:5000, 6:8000}
// (season currency 63), ConfigID 16 = quality cap [6], ConfigID 3 = the unlock requirements per
// target quality - {2: [[36, 103]]} means "the Donoduel story mission 103 was claimed", {3..6:
// [[37, levelId]]} means "that arena level was cleared". Both are server state, so the check is
// ours to make. Raising the quality never changes the attributes by itself, it only raises the
// ceilings (ConfigID 1/23/24 rows are keyed by quality) - attributes above the new ceiling are
// clamped, mirroring the client's preview.
//
// Request:  { "nUniqueId": <1..350> }
// Response: { } on success, { "sError": "..." } otherwise (plus the NtfSyncAttr push).
[CallGSApi("DonoDuel_UpgradeQuality")]
public class DonoDuel_UpgradeQuality : CallGSHandler
{
    private static readonly Logger Log = new(nameof(DonoDuel_UpgradeQuality));

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player!;

        var root = DonoDuelGrowth.ParseObject(param);
        var uniqueId = DonoDuelGrowth.ReadUint(root?["nUniqueId"]);

        if (!DonoDuelBag.HasCard(player, uniqueId))
        {
            Log.Warn($"Donoduel quality upgrade for unknown card {uniqueId} from uid={player.Uid}.");
            return DonoDuelGrowth.Bad();
        }

        var quality = DonoDuelBag.GetQuality(player, uniqueId);
        var target = quality + 1;
        if (target > DonoDuelConfig.QualityMax())
            return DonoDuelGrowth.Bad("error.condition_limit");

        foreach (var condition in DonoDuelConfig.QualityUnlockConditions(target))
        {
            if (DonoDuelGrowth.ConditionMet(player, condition))
                continue;

            Log.Info(
                $"Donoduel card {uniqueId} quality {target} locked for uid={player.Uid} " +
                $"(condition [{string.Join(',', condition)}]).");
            return DonoDuelGrowth.Bad("error.condition_limit");
        }

        var cost = DonoDuelConfig.QualityCost(target);
        if (cost == 0)
        {
            Log.Warn($"No quality price for target quality {target} (uid={player.Uid}).");
            return DonoDuelGrowth.Bad();
        }

        var sync = new NtfSyncPlayer();
        if (!CurrencyService.TrySpend(player, DonoDuelGrowth.SeasonCurrency(player), cost, sync, out var error))
            return DonoDuelGrowth.Bad(error);

        DonoDuelBag.SetQuality(player, uniqueId, target, sync);

        var templateId = DonoDuelBag.GetTemplateId(player, uniqueId);
        for (var position = DonoDuelGrowth.AttrAttack; position <= DonoDuelGrowth.AttrHealth; position++)
        {
            var cap = DonoDuelGrowth.AttrCap(templateId, target, position);
            if (cap == 0)
                continue;

            var current = DonoDuelBag.GetAttr(player, uniqueId, position);
            if (current > cap)
                DonoDuelBag.SetAttr(player, uniqueId, position, cap, sync);
        }

        DonoDuelBag.Save(player);
        await player.SendPacket(CmdIds.NtfSyncAttr, sync);

        Log.Info($"Donoduel card {uniqueId} quality {quality} -> {target} (cost {cost}) for uid={player.Uid}.");

        return DonoDuelGrowth.Ok();
    }
}
