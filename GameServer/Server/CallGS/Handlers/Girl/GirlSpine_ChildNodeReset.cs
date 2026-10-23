using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

// "Reset spine" button in the nerve panel (UMG/Role/Widget/Nerve/uw_nerve_main.lua:148).
// Client contract (Item/Spine.lua:484 Req_ChildNodeReset + s2c.Register("GirlSpine_ChildNodeReset")):
// request is {"pId": <card guid>} and the reply carries { D = <card Detail>, tbItem = [[g,d,p,l,count], ...] }.
// The reply's D makes the client re-read GetStrAttribute(30, D); the cleared nodes themselves travel
// inside the NtfSyncPlayer payload as the updated character proto.
[CallGSApi("GirlSpine_ChildNodeReset")]
public class GirlSpine_ChildNodeReset : CallGSHandler<ChildNodeResetParam>
{
    private const int MaxMastNum = 6;
    private const int MaxSubNum = 9;

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, ChildNodeResetParam req)
    {
        var player = context.Connection.Player!;

        if (req == null || req.CardId == 0)
        {
            return CallGSResult.Error("error.BadParam");
        }

        var card = player.CharacterManager.GetCharacterByGUID((uint)req.CardId);
        if (card == null)
        {
            return CallGSResult.Error("error.BadParam");
        }

        var cardDetail = (uint)((card.TemplateId >> 16) & 0xFFFF);
        var cardParticular = (uint)((card.TemplateId >> 32) & 0xFFFF);

        // Refund every unlocked sub-node with the NodeXRefund column of its master frame
        // (item/skill/nodecondition.json). The frame per master index is resolved exactly like
        // GirlSpine_UnlockNodeOneKey does for the cost side.
        var refundTotals = new Dictionary<ulong, uint>();
        var cardExcel = GameData.CardData.Values.FirstOrDefault(
            x => x.Detail == cardDetail && x.Particular == cardParticular);

        if (cardExcel != null && GameData.SpineData.TryGetValue(cardExcel.SpineId, out var spineExcel))
        {
            for (var mastIdx = 1; mastIdx <= MaxMastNum; mastIdx++)
            {
                var listIdx = mastIdx - 1;
                if (listIdx >= card.Spines.Count) break;

                var mask = card.Spines[listIdx];
                if (mask == 0) continue;

                var nodeCondId = spineExcel.GetNodeReq(mastIdx);
                if (nodeCondId == 0 || !GameData.NodeConditionData.TryGetValue(nodeCondId, out var nodeCond)) continue;

                for (var subIdx = 1; subIdx <= MaxSubNum; subIdx++)
                {
                    if ((mask & (1u << (subIdx - 1))) == 0) continue;

                    foreach (var row in nodeCond.GetNodeRefund(subIdx))
                    {
                        if (row == null || row.Count < 5) continue;

                        var count = (uint)Math.Max(0, row[4]);
                        if (count == 0) continue;

                        var templateId = GameResourceTemplateId.FromGdpl(
                            (uint)Math.Max(0, row[0]), (uint)Math.Max(0, row[1]),
                            (uint)Math.Max(0, row[2]), (uint)Math.Max(0, row[3]));
                        refundTotals[templateId] = refundTotals.GetValueOrDefault(templateId) + count;
                    }
                }
            }
        }

        // Drop every unlocked bit. A character fully reset keeps its SpineStrAttr entry (ns = the
        // branch the player is working on); only the sub-node bits are what the tree renders from.
        var syncItems = new List<Item>();
        for (var i = 0; i < card.Spines.Count; i++)
        {
            card.Spines[i] = 0;
        }
        syncItems.Add(card.ToProto());

        // Hand the refunded materials back and echo the same rows for the client's gain popup.
        var awardedItems = new List<List<int>>();
        foreach (var (templateId, count) in refundTotals)
        {
            if (GameData.SuppliesData.TryGetValue(templateId, out var supplies))
            {
                var item = await player.InventoryManager.AddSuppliesItem(supplies, count, sendPacket: false);
                if (item != null)
                {
                    syncItems.Add(item.ToProto());
                }
                awardedItems.Add([
                    (int)supplies.Genre, (int)supplies.Detail,
                    (int)supplies.Particular, (int)supplies.Level, (int)count]);
                continue;
            }

            // No supply template for this GDPL: still report the row so the player sees the refund.
            awardedItems.Add([
                (int)(templateId & 0xFFFF), (int)((templateId >> 16) & 0xFFFF),
                (int)((templateId >> 32) & 0xFFFF), (int)((templateId >> 48) & 0xFFFF), (int)count]);
        }

        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
        DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);

        var sync = new NtfSyncPlayer();
        sync.Items.AddRange(syncItems);

        var rsp = JsonSerializer.Serialize(new { D = cardDetail, tbItem = awardedItems });
        return CallGSResult.Ok(rsp, sync);
    }
}

public sealed class ChildNodeResetParam
{
    [JsonPropertyName("pId")]
    public int CardId { get; set; }
}
