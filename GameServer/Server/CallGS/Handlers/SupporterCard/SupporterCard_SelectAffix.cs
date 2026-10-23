using MikuSB.GameServer.Game.Support;
using MikuSB.Proto;
using System.Text.Json;

namespace MikuSB.GameServer.Server.CallGS.Handlers.SupporterCard;

[CallGSApi("SupporterCard_SelectAffix")]
public class SupporterCard_SelectAffix : CallGSHandler<SupporterCardSelectParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, SupporterCardSelectParam req)
    {

        var card = req == null ? null : context.Connection.Player!.InventoryManager.GetSupportCardItem((uint)req.SupportCardUid);
        if (card == null || !SupportAffixStateService.HasAffix(card, SupportAffixStateService.PendingMaxAffixSlot))
        {
            return Task.FromResult(SupporterCardAffixShared.SelectResponse());
}

        if (req!.SelectNew)
            SupportAffixStateService.CopyAffix(card, SupportAffixStateService.PendingMaxAffixSlot, SupportAffixStateService.ActiveThirdAffixSlot);

        SupportAffixStateService.ClearAffix(card, SupportAffixStateService.PendingMaxAffixSlot);

        var sync = new NtfSyncPlayer();
        sync.Items.Add(card.ToProto());
        SupporterCardAffixShared.Save(context.Connection);
        return Task.FromResult(SupporterCardAffixShared.SelectResponse(sync));
    }
}
