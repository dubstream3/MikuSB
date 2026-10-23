using MikuSB.GameServer.Game.Support;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.SupporterCard;

[CallGSApi("SupporterCard_ResetInitialAffix")]
public class SupporterCard_ResetInitialAffix : CallGSHandler<SupporterCardResetInitialParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, SupporterCardResetInitialParam req)
    {
        return Reset(context.Connection, req, fixedMode: false);
    }

    internal static Task<CallGSResult> Reset(Connection connection, SupporterCardResetInitialParam req, bool fixedMode)
    {
        var card = connection.Player!.InventoryManager.GetSupportCardItem((uint)req.SupportCardUid);
        var excel = card == null ? null : SupporterCardAffixShared.GetExcel(card);
        if (card == null || excel == null || req.Index is < 1 or > 2 || excel.AffixPool.Count < req.Index)
        {
            return Task.FromResult(SupporterCardAffixShared.ResetResponse());
        }

        var costs = fixedMode ? new[] { excel.FixedAffixCost } : excel.InitialAffixCost;
        if (!costs.Any() || !SupporterCardAffixShared.HasEnoughItems(connection, costs))
        {
            return Task.FromResult(SupporterCardAffixShared.ResetResponse());
        }

        var sync = new NtfSyncPlayer();
        sync.Items.AddRange(SupporterCardAffixShared.ConsumeCostItems(connection, costs));

        uint affixId;
        uint tier;
        if (fixedMode && req.FixedId > 0)
        {
            affixId = req.FixedId;
            tier = SupportAffixService.GenerateTier(affixId);
        }
        else
        {
            var excluded = SupporterCardAffixShared.GetActiveAffixIds(card, req.Index);
            (affixId, tier) = SupportAffixService.GenerateRandomAffix(excel.AffixPool[req.Index - 1], excluded);
        }

        SupportAffixStateService.SetAffix(card, SupportAffixStateService.PendingInitialAffixSlot, affixId, tier);
        card.AffixId = (uint)req.Index;

        var player = connection.Player!;
        var attr = player.Attributes.GetOrCreate(SupporterCardAffixShared.BaseGid, SupporterCardAffixShared.FixedResetSid);
        attr.Val += 1;
        SupporterCardAffixShared.SetAttr(connection, sync, SupporterCardAffixShared.BaseGid, SupporterCardAffixShared.FixedResetSid, attr.Val);

        sync.Items.Add(card.ToProto());
        SupporterCardAffixShared.Save(connection);
        return Task.FromResult(SupporterCardAffixShared.ResetResponse(sync));
    }
}
