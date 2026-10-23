using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.Proto;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

[CallGSApi("GirlCard_UpByBreak")]
public class GirlCard_UpByBreak : CallGSHandler<GirlCardUpByBreakParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, GirlCardUpByBreakParam req)
    {
        var player = context.Connection.Player!;

        if (req == null || req.CardId == 0 || req.BreakLv <= 0 || req.Materials == null || req.Materials.Count == 0)
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var card = player.CharacterManager.GetCharacterByGUID((uint)req.CardId);
        if (card == null)
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var expectedBreakLv = card.Break + 1;
        if (req.BreakLv != expectedBreakLv)
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var requestedMaterials = new Dictionary<ulong, uint>();
        foreach (var row in req.Materials)
        {
            if (row == null || row.Count < 5)
                continue;

            var genre = (uint)Math.Max(0, row[0]);
            var detail = (uint)Math.Max(0, row[1]);
            var particular = (uint)Math.Max(0, row[2]);
            var level = (uint)Math.Max(0, row[3]);
            var count = (uint)Math.Max(0, row[4]);
            if (genre == 0 || detail == 0 || particular == 0 || level == 0 || count == 0)
                continue;

            var templateId = GameResourceTemplateId.FromGdpl(genre, detail, particular, level);
            requestedMaterials[templateId] = requestedMaterials.GetValueOrDefault(templateId) + count;
        }

        if (requestedMaterials.Count == 0)
        {
            return Task.FromResult(CallGSResult.Error("tip.not_material_for_break"));
        }

        var syncItems = new List<Item>();
        foreach (var (templateId, count) in requestedMaterials)
        {
            var item = player.InventoryManager.InventoryData.Items.Values.FirstOrDefault(x => x.TemplateId == templateId);
            if (item == null || item.ItemCount < count)
            {
                return Task.FromResult(CallGSResult.Error("tip.not_material_for_break"));
            }
        }

        foreach (var (templateId, count) in requestedMaterials)
        {
            var item = player.InventoryManager.InventoryData.Items.Values.First(x => x.TemplateId == templateId);
            item.ItemCount -= count;

            if (item.ItemCount == 0)
            {
                player.InventoryManager.InventoryData.Items.Remove(item.UniqueId);
                syncItems.Add(BuildRemovedProto(item));
            }
            else
            {
                syncItems.Add(item.ToProto());
            }
        }

        card.Break = req.BreakLv;
        syncItems.Add(card.ToProto());

        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
        DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);

        var sync = new NtfSyncPlayer();
        sync.Items.AddRange(syncItems);

        return Task.FromResult(CallGSResult.Ok("{}", sync));
    }

    private static Item BuildRemovedProto(BaseGameItemInfo item)
    {
        var proto = item.ToProto();
        proto.Count = 0;
        return proto;
    }
}

public sealed class GirlCardUpByBreakParam
{
    [JsonPropertyName("pId")]
    public int CardId { get; set; }

    [JsonPropertyName("nBreakLv")]
    public uint BreakLv { get; set; }

    [JsonPropertyName("tbMaterials")]
    public List<List<int>> Materials { get; set; } = [];
}
