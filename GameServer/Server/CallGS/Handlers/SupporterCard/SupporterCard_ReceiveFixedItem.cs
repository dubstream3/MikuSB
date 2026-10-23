using MikuSB.Data;
using MikuSB.Proto;
using System.Text.Json.Nodes;

namespace MikuSB.GameServer.Server.CallGS.Handlers.SupporterCard;

[CallGSApi("SupporterCard_ReceiveFixedItem")]
public class SupporterCard_ReceiveFixedItem : CallGSHandler
{
    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player!;
        if (!GameData.SupportFixedData.TryGetValue(1, out var fixedCfg) || fixedCfg.Item.Count < 5 || fixedCfg.Num <= 0)
        {
            return CallGSResult.Ok("{}");
        }

        var attr = player.Attributes.GetOrCreate(SupporterCardAffixShared.BaseGid, SupporterCardAffixShared.FixedResetSid);
        var claimCount = attr.Val / (uint)fixedCfg.Num;
        if (claimCount == 0)
        {
            return CallGSResult.Ok("{}");
        }

        attr.Val %= (uint)fixedCfg.Num;

        var rewardTemplateId = GameResourceTemplateId.FromGdpl(fixedCfg.Item);
        var rewardItem = GameData.SuppliesData.GetValueOrDefault(rewardTemplateId);
        if (rewardItem == null)
        {
            return CallGSResult.Ok("{}");
        }

        var granted = await player.InventoryManager.AddSuppliesItem(rewardItem, claimCount * fixedCfg.Item[4], sendPacket: false);

        var sync = new NtfSyncPlayer();
        if (granted != null)
            sync.Items.Add(granted.ToProto());
        SupporterCardAffixShared.SetAttr(context.Connection, sync, SupporterCardAffixShared.BaseGid, SupporterCardAffixShared.FixedResetSid, attr.Val);
        SupporterCardAffixShared.Save(context.Connection);

        var arg = new JsonObject
        {
            ["tbRewards"] = new JsonArray(
                (int)fixedCfg.Item[0],
                (int)fixedCfg.Item[1],
                (int)fixedCfg.Item[2],
                (int)fixedCfg.Item[3],
                (int)(claimCount * fixedCfg.Item[4]))
        }.ToJsonString();

        return CallGSResult.Ok(arg, sync);
    }
}
