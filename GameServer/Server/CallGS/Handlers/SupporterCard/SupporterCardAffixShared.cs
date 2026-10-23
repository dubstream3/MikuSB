using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.GameServer.Game.Support;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.SupporterCard;

internal static class SupporterCardAffixShared
{
    public const uint BaseGid = AttrIds.SupporterCard.Gid;
    public const uint FixedResetSid = AttrIds.SupporterCard.FixedResetSid;

    public static SupportCardExcel? GetExcel(GameSupportCardInfo card)
    {
        return GameData.SupportCardData.FirstOrDefault(x => x.TemplateId == card.TemplateId);
    }

    public static CallGSResult ResetResponse(NtfSyncPlayer? sync = null)
    {
        return CallGSResult.Ok("null", sync, "SupporterCard_ResetAffix");
    }

    public static CallGSResult SelectResponse(NtfSyncPlayer? sync = null)
    {
        return CallGSResult.Ok("null", sync, "SupporterCard_SelectAffix");
    }

    public static List<Item> ConsumeCostItems(Connection connection, IEnumerable<IReadOnlyList<uint>> costs)
    {
        var player = connection.Player!;
        var syncItems = new List<Item>();

        foreach (var cost in costs)
        {
            if (cost.Count < 5)
                continue;

            var templateId = GameResourceTemplateId.FromGdpl(cost);
            var item = player.InventoryManager.InventoryData.Items.Values.FirstOrDefault(x => x.TemplateId == templateId);
            if (item == null || item.ItemCount < cost[4])
                throw new InvalidOperationException("support affix material not enough");

            item.ItemCount -= cost[4];
            var proto = item.ToProto();
            if (item.ItemCount == 0)
            {
                player.InventoryManager.InventoryData.Items.Remove(item.UniqueId);
                proto.Count = 0;
            }
            syncItems.Add(proto);
        }

        return syncItems;
    }

    public static bool HasEnoughItems(Connection connection, IEnumerable<IReadOnlyList<uint>> costs)
    {
        var items = connection.Player!.InventoryManager.InventoryData.Items.Values;
        return costs.All(cost =>
        {
            if (cost.Count < 5)
                return false;

            var templateId = GameResourceTemplateId.FromGdpl(cost);
            var item = items.FirstOrDefault(x => x.TemplateId == templateId);
            return item != null && item.ItemCount >= cost[4];
        });
    }

    public static void SetAttr(Connection connection, NtfSyncPlayer sync, uint gid, uint sid, uint value)
    {
        var player = connection.Player!;
        var attr = player.Attributes.Set(gid, sid, value);
        attr.Val = value;
        player.Attributes.SyncTo(sync, attr);
    }

    public static IEnumerable<uint> GetActiveAffixIds(GameSupportCardInfo card, params int[] ignoreSlots)
    {
        var ignored = ignoreSlots.ToHashSet();
        for (var slot = 1; slot <= SupportAffixStateService.ActiveThirdAffixSlot; slot++)
        {
            if (ignored.Contains(slot))
                continue;

            var (affixId, _) = SupportAffixStateService.GetAffix(card, slot);
            if (affixId > 0)
                yield return affixId;
        }
    }

    public static void Save(Connection connection)
    {
        var player = connection.Player!;
        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
        DatabaseHelper.SaveDatabaseType(player.Data);
    }
}

public sealed class SupporterCardIdParam
{
    [JsonPropertyName("Id")]
    public int SupportCardUid { get; set; }
}

public sealed class SupporterCardSelectParam
{
    [JsonPropertyName("Id")]
    public int SupportCardUid { get; set; }

    [JsonPropertyName("SelectNew")]
    public bool SelectNew { get; set; }
}

public sealed class SupporterCardResetInitialParam
{
    [JsonPropertyName("Id")]
    public int SupportCardUid { get; set; }

    [JsonPropertyName("Index")]
    public int Index { get; set; }

    [JsonPropertyName("FixedId")]
    public uint FixedId { get; set; }
}

public sealed class SupporterCardSelectInitialParam
{
    [JsonPropertyName("Id")]
    public int SupportCardUid { get; set; }

    [JsonPropertyName("Index")]
    public int Index { get; set; }

    [JsonPropertyName("SelectNew")]
    public bool SelectNew { get; set; }
}
