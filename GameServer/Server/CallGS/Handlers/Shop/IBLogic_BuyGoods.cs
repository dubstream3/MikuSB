using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.Database.Player;
using MikuSB.Enums.Item;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Shop;

[CallGSApi("IBLogic_BuyGoods")]
public class IBLogic_BuyGoods : CallGSHandler<IbBuyGoodsParam>
{
    private const uint BuyGroupId = AttrIds.Shop.PurchaseGid;
    private const uint RedGroupId = AttrIds.Shop.RedDotGid;
    private const uint CashGroupId = AttrIds.CurrencyGid;
    private const uint BattlePassGroupId = AttrIds.BattlePass.Gid;
    private const uint BattlePassCurIdSid = AttrIds.BattlePass.CurrentIdSid;
    private const uint BattlePassStatusSid = AttrIds.BattlePass.StatusSid;

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, IbBuyGoodsParam req)
    {

        var player = context.Connection.Player!;
        if (req?.Type == 3 && req.GoodsId > 0 && req.Count > 0)
        {
            return await HandleBattlePassPurchase(player, req);
}

        if (req == null ||
            req.GoodsId == 0 ||
            req.Count == 0 ||
            !GameData.IbGoodsData.TryGetValue(req.GoodsId, out var goods))
        {
            return CallGSResult.Error("error.BadParam");
        }

        if (goods.LimitTimes > 0)
        {
            var buyAttr = player.Attributes.GetOrCreate(BuyGroupId, req.GoodsId);
            if (buyAttr.Val >= goods.LimitTimes)
            {
                return CallGSResult.Error("tip.Mall_Limit_Buy");
            }
        }

        var rewardItems = BuildRewardItems(goods, req);
        if (rewardItems.Count == 0)
        {
            return CallGSResult.Error("error.BadParam");
        }

        var sync = new NtfSyncPlayer();
        foreach (var reward in rewardItems)
            await GrantRewardAsync(player, sync, reward);

        var buyCountAttr = player.Attributes.GetOrCreate(BuyGroupId, req.GoodsId);
        buyCountAttr.Val += req.Count;
        player.Attributes.SyncTo(sync, buyCountAttr);

        var redAttr = player.Attributes.GetOrCreate(RedGroupId, req.GoodsId);
        if (redAttr.Val == 0)
        {
            redAttr.Val = 1;
            player.Attributes.SyncTo(sync, redAttr);
        }

        DatabaseHelper.SaveDatabaseType(player.Data);
        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
        DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);

        var responseGoods = new JsonArray();
        foreach (var reward in rewardItems)
        {
            var row = new JsonArray();
            foreach (var value in reward)
                row.Add((int)value);
            responseGoods.Add(row);
        }

        var rsp = new JsonObject
        {
            ["nGoodsId"] = (int)req.GoodsId,
            ["tbGoods"] = responseGoods
        };

        var productId = goods.GetProductId();
        if (!string.IsNullOrWhiteSpace(productId))
            rsp["sProductId"] = productId;

        var cost = req.Index == 2 ? goods.Cost2 : goods.Cost;
        if (cost.Count >= 2)
            rsp["nTotalPrice"] = (int)cost[1];

        return CallGSResult.Ok(rsp.ToJsonString(), sync);
    }

    private static Task<CallGSResult> HandleBattlePassPurchase(PlayerInstance player, IbBuyGoodsParam req)
    {
        var sync = new NtfSyncPlayer();
        var battlePassId = ResolveCurrentBattlePassId();
        if (battlePassId > 0)
        {
            var curIdAttr = player.Attributes.GetOrCreate(BattlePassGroupId, BattlePassCurIdSid);
            curIdAttr.Val = battlePassId;
            player.Attributes.SyncTo(sync, curIdAttr);
        }

        var statusAttr = player.Attributes.GetOrCreate(BattlePassGroupId, BattlePassStatusSid);
        if (statusAttr.Val < 2)
        {
            statusAttr.Val = 2;
            player.Attributes.SyncTo(sync, statusAttr);
        }

        var buyCountAttr = player.Attributes.GetOrCreate(BuyGroupId, req.GoodsId);
        buyCountAttr.Val += req.Count;
        player.Attributes.SyncTo(sync, buyCountAttr);

        var redAttr = player.Attributes.GetOrCreate(RedGroupId, req.GoodsId);
        if (redAttr.Val == 0)
        {
            redAttr.Val = 1;
            player.Attributes.SyncTo(sync, redAttr);
        }

        DatabaseHelper.SaveDatabaseType(player.Data);

        var rsp = new JsonObject
        {
            ["nGoodsId"] = (int)req.GoodsId,
            ["tbGoods"] = new JsonArray()
        };

        return Task.FromResult(CallGSResult.Ok(rsp.ToJsonString(), sync));
    }

    private static List<List<uint>> BuildRewardItems(IbGoodsExcel goods, IbBuyGoodsParam req)
    {
        var rewards = new List<List<uint>>();

        if (goods.Item.Count >= 4)
            rewards.Add(WithCount(goods.Item, req.Count));

        if (req.SelectItem1?.Count >= 4)
            rewards.Add(WithCount(req.SelectItem1, req.Count));

        if (req.SelectItem2?.Count >= 4)
            rewards.Add(WithCount(req.SelectItem2, req.Count));

        return rewards;
    }

    private static List<uint> WithCount(IReadOnlyList<uint> item, uint buyCount)
    {
        var reward = item.Take(5).ToList();
        while (reward.Count < 5)
            reward.Add(1);

        reward[4] = Math.Max(1u, reward[4]) * Math.Max(1u, buyCount);
        return reward;
    }

    private static async Task GrantRewardAsync(PlayerInstance player, NtfSyncPlayer sync, IReadOnlyList<uint> reward)
    {
        if (reward.Count < 5)
            return;

        var itemType = (ItemTypeEnum)reward[0];
        var detail = reward[1];
        var particular = reward[2];
        var level = reward[3];
        var count = Math.Max(1u, reward[4]);

        switch (itemType)
        {
            case ItemTypeEnum.TYPE_CARD:
                for (var i = 0u; i < count; i++)
                {
                    var character = await player.CharacterManager.AddCharacter(itemType, detail, particular, level, sendPacket: false);
                    if (character != null)
                        sync.Items.Add(character.ToProto());
                }
                break;
            case ItemTypeEnum.TYPE_WEAPON:
                for (var i = 0u; i < count; i++)
                {
                    var weapon = await player.InventoryManager.AddWeaponItem(itemType, detail, particular, level, sendPacket: false);
                    if (weapon != null)
                        sync.Items.Add(weapon.ToProto());
                }
                break;
            case ItemTypeEnum.TYPE_SUPPORT:
                for (var i = 0u; i < count; i++)
                {
                    var support = await player.InventoryManager.AddSupportCardItem(detail, particular, level, sendPacket: false);
                    if (support != null)
                        sync.Items.Add(support.ToProto());
                }
                break;
            case ItemTypeEnum.TYPE_SUPPLIES:
            {
                var templateId = GameResourceTemplateId.FromGdpl(reward[0], detail, particular, level);
                if (!GameData.SuppliesData.TryGetValue(templateId, out var supplies))
                    break;

                var item = await player.InventoryManager.AddSuppliesItem(supplies, count, sendPacket: false);
                if (item != null)
                    sync.Items.Add(item.ToProto());
                break;
            }
            case ItemTypeEnum.TYPE_USEABLE:
            {
                if (!TryGrantCashBox(player, sync, detail, particular, level, count))
                {
                    var item = AddOtherItem(player.InventoryManager.InventoryData, reward[0], detail, particular, level, count);
                    if (item != null)
                        sync.Items.Add(item.ToProto());
                }
                break;
            }
            case ItemTypeEnum.TYPE_WEAPON_PART:
                for (var i = 0u; i < count; i++)
                {
                    var item = await player.InventoryManager.AddWeaponPartItem(itemType, detail, particular, level, sendPacket: false);
                    if (item != null)
                        sync.Items.Add(item.ToProto());
                }
                break;
            case ItemTypeEnum.TYPE_CARD_SKIN:
                for (var i = 0u; i < count; i++)
                {
                    var item = await player.InventoryManager.AddSkinItem(itemType, detail, particular, level, sendPacket: false);
                    if (item != null)
                        sync.Items.Add(item.ToProto());
                }
                break;
            case ItemTypeEnum.TYPE_HOUSE:
                for (var i = 0u; i < count; i++)
                {
                    var item = await player.InventoryManager.AddHouseFurnitureItem(itemType, detail, particular, level, sendPacket: false);
                    if (item != null)
                        sync.Items.Add(item.ToProto());
                }
                break;
            case ItemTypeEnum.TYPE_PROFILE:
            case ItemTypeEnum.TYPE_FRAME:
            case ItemTypeEnum.TYPE_BADGE:
            case ItemTypeEnum.TYPE_COVER:
            case ItemTypeEnum.TYPE_NAMECARD:
            case ItemTypeEnum.TYPE_EXPRESSION:
            case ItemTypeEnum.TYPE_BUBBLE:
            case ItemTypeEnum.TYPE_ANALYST:
                for (var i = 0u; i < count; i++)
                {
                    var item = await player.InventoryManager.AddProfileItem(itemType, detail, particular, level, sendPacket: false);
                    if (item != null)
                        sync.Items.Add(item.ToProto());
                }
                break;
            case ItemTypeEnum.TYPE_WEAPON_SKIN:
                for (var i = 0u; i < count; i++)
                {
                    var item = await player.InventoryManager.AddWeaponSkinItem(itemType, detail, particular, level, sendPacket: false);
                    if (item != null)
                        sync.Items.Add(item.ToProto());
                }
                break;
            case ItemTypeEnum.TYPE_MANIFESTATION:
                for (var i = 0u; i < count; i++)
                {
                    var item = await player.InventoryManager.AddManifestationItem(itemType, detail, particular, level, sendPacket: false);
                    if (item != null)
                        sync.Items.Add(item.ToProto());
                }
                break;
            case ItemTypeEnum.TYPE_CARD_SKIN_PART:
                for (var i = 0u; i < count; i++)
                {
                    var item = await player.InventoryManager.AddSkinPartItem(itemType, detail, particular, level, sendPacket: false);
                    if (item != null)
                        sync.Items.Add(item.ToProto());
                }
                break;
            case ItemTypeEnum.TYPE_AR:
                for (var i = 0u; i < count; i++)
                {
                    var item = await player.InventoryManager.AddArItem(itemType, detail, particular, level, sendPacket: false);
                    if (item != null)
                        sync.Items.Add(item.ToProto());
                }
                break;
            case ItemTypeEnum.TYPE_CALL:
                for (var i = 0u; i < count; i++)
                {
                    var item = await player.InventoryManager.AddCallItem(itemType, detail, particular, level, sendPacket: false);
                    if (item != null)
                        sync.Items.Add(item.ToProto());
                }
                break;
        }
    }

    private static BaseGameItemInfo? AddOtherItem(InventoryData inventory, uint genre, uint detail, uint particular, uint level, uint count)
    {
        var templateId = GameResourceTemplateId.FromGdpl(genre, detail, particular, level);
        if (!GameData.OtherItemData.TryGetValue(templateId, out var otherItem))
            return null;

        var maxCount = otherItem.GMnum > 0 ? otherItem.GMnum : 99999u;
        var existing = inventory.Items.Values.FirstOrDefault(x => x.TemplateId == templateId);
        if (existing != null)
        {
            existing.ItemCount = Math.Min(existing.ItemCount + count, maxCount);
            return existing;
        }

        var item = new BaseGameItemInfo
        {
            TemplateId = templateId,
            UniqueId = inventory.NextUniqueUid++,
            ItemType = ItemTypeEnum.TYPE_USEABLE,
            ItemCount = Math.Min(count, maxCount)
        };
        inventory.Items[item.UniqueId] = item;
        return item;
    }

    private static bool TryGrantCashBox(PlayerInstance player, NtfSyncPlayer sync, uint detail, uint particular, uint level, uint count)
    {
        var templateId = GameResourceTemplateId.FromGdpl((uint)ItemTypeEnum.TYPE_USEABLE, detail, particular, level);
        if (!GameData.OtherItemData.TryGetValue(templateId, out var otherItem))
            return false;

        uint moneyType = otherItem.LuaType switch
        {
            "money_box" => AttrIds.Currency.Money,
            "gold_box" => AttrIds.Currency.Gold,
            "silver_box" => AttrIds.Currency.Silver,
            "vigor_box" => AttrIds.Currency.Vigor,
            _ => 0
        };

        if (moneyType == 0 || otherItem.Param1 == 0)
            return false;

        var amount = checked(otherItem.Param1 * count);
        var sid = AttrIds.Currency.GetSid(moneyType);
        var attr = player.Attributes.GetOrCreate(CashGroupId, sid);
        attr.Val += amount;
        player.Attributes.SyncTo(sync, attr);
        if (moneyType == AttrIds.Currency.Money)
        {
            foreach (var (key, value) in player.BuildMoneySync())
                sync.Money[key] = value;
        }
        return true;
    }

    private static uint ResolveCurrentBattlePassId()
    {
        var now = DateTime.Now;
        var parsed = GameData.BattlePassTimeData.Values
            .Select(x => new
            {
                Config = x,
                Start = ParseConfigTime(x.StartTime),
                End = ParseConfigTime(x.EndTime)
            })
            .Where(x => x.Start.HasValue && x.End.HasValue)
            .OrderBy(x => x.Start)
            .ToList();

        var current = parsed.FirstOrDefault(x => x.Start <= now && now < x.End);
        if (current != null)
            return current.Config.Id;

        var latestStarted = parsed.LastOrDefault(x => x.Start <= now && x.End > x.Start);
        return latestStarted?.Config.Id ?? 0;
    }

    private static DateTime? ParseConfigTime(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var normalized = raw.Trim().Trim('[', ']');
        if (normalized.Length != 12)
            return null;

        return DateTime.TryParseExact(
            normalized,
            "yyyyMMddHHmm",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var value)
            ? value
            : null;
    }

}

public sealed class IbBuyGoodsParam
{
    [JsonPropertyName("nType")]
    public int Type { get; set; }

    [JsonPropertyName("nGoodsId")]
    public uint GoodsId { get; set; }

    [JsonPropertyName("nCount")]
    public uint Count { get; set; }

    [JsonPropertyName("nIndex")]
    public int Index { get; set; }

    [JsonPropertyName("tbSelectItem1")]
    public List<uint>? SelectItem1 { get; set; }

    [JsonPropertyName("tbSelectItem2")]
    public List<uint>? SelectItem2 { get; set; }
}
