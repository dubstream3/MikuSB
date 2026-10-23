using MikuSB.GameServer.Game.Support;
using MikuSB.Proto;
using System.Text.Json;

namespace MikuSB.GameServer.Server.CallGS.Handlers.SupporterCard;

[CallGSApi("SupporterCard_SelectInitialAffix")]
public class SupporterCard_SelectInitialAffix : CallGSHandler<SupporterCardSelectInitialParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, SupporterCardSelectInitialParam req)
    {

        var card = req == null ? null : context.Connection.Player!.InventoryManager.GetSupportCardItem((uint)req.SupportCardUid);
        if (req == null || card == null || req.Index is < 1 or > 2 || card.AffixId != req.Index || !SupportAffixStateService.HasAffix(card, SupportAffixStateService.PendingInitialAffixSlot))
        {
            return Task.FromResult(SupporterCardAffixShared.SelectResponse());
}

        if (req.SelectNew)
            SupportAffixStateService.CopyAffix(card, SupportAffixStateService.PendingInitialAffixSlot, req.Index);

        SupportAffixStateService.ClearAffix(card, SupportAffixStateService.PendingInitialAffixSlot);
        card.AffixId = 0;

        var sync = new NtfSyncPlayer();
        sync.Items.Add(card.ToProto());
        SupporterCardAffixShared.Save(context.Connection);
        return Task.FromResult(SupporterCardAffixShared.SelectResponse(sync));
    }
}
