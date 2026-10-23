using MikuSB.GameServer.Game.Support;
using MikuSB.Proto;
using System.Text.Json;

namespace MikuSB.GameServer.Server.CallGS.Handlers.SupporterCard;

[CallGSApi("SupporterCard_ResetAffix")]
public class SupporterCard_ResetAffix : CallGSHandler<SupporterCardIdParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, SupporterCardIdParam req)
    {

        var card = req == null ? null : context.Connection.Player!.InventoryManager.GetSupportCardItem((uint)req.SupportCardUid);
        var excel = card == null ? null : SupporterCardAffixShared.GetExcel(card);
        if (card == null || excel == null || excel.AffixCost.Count < 5 || !SupportAffixStateService.HasAffix(card, SupportAffixStateService.ActiveThirdAffixSlot))
        {
            return Task.FromResult(SupporterCardAffixShared.ResetResponse());
}

        var costs = new[] { excel.AffixCost };
        if (!SupporterCardAffixShared.HasEnoughItems(context.Connection, costs))
        {
            return Task.FromResult(SupporterCardAffixShared.ResetResponse());
}

        var sync = new NtfSyncPlayer();
        sync.Items.AddRange(SupporterCardAffixShared.ConsumeCostItems(context.Connection, costs));
        var excluded = SupporterCardAffixShared.GetActiveAffixIds(card, SupportAffixStateService.ActiveThirdAffixSlot);
        var (affixId, tier) = SupportAffixService.GenerateRandomAffix(excel.AffixPool[SupportAffixStateService.ActiveThirdAffixSlot - 1], excluded);
        SupportAffixStateService.SetAffix(card, SupportAffixStateService.PendingMaxAffixSlot, affixId, tier);
        sync.Items.Add(card.ToProto());

        SupporterCardAffixShared.Save(context.Connection);
        return Task.FromResult(SupporterCardAffixShared.ResetResponse(sync));
    }
}
