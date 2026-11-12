using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.Enums.Item;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using Newtonsoft.Json.Linq;
using System.Text.Json.Nodes;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// Shared VirCapture reward granting. Mirrors the item dispatch used by
/// <see cref="VirCapture_GetLevelAward"/> so that every VirCapture reward path writes the
/// same inventory tables through <see cref="GameServer.Game.Inventory.InventoryManager"/>.
/// </summary>
internal static class VirCaptureRewardHelper
{
    /// <summary>Mission reward kinds (client VirCaptureTask.REWARD_*).</summary>
    public const uint KindItem = 1;
    public const uint KindBuff = 2;
    public const uint KindMapLevelUp = 3;
    public const uint KindFreeWorker = 4;
    public const uint KindRecruitWorker = 5;
    public const uint KindUnlockBuilding = 6;

    private static readonly Logger Log = new("VirCapture.Reward");

    /// <summary>
    /// Grants one [itemType, detail, particular, level, count] row and appends the granted item
    /// to <paramref name="sync"/>. Returns false when the template is unknown.
    /// </summary>
    public static async Task<bool> GrantItemRewardAsync(PlayerInstance player, NtfSyncPlayer sync, IReadOnlyList<uint> reward)
    {
        if (reward.Count < 5)
            return false;

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
                return true;
            case ItemTypeEnum.TYPE_WEAPON:
                for (var i = 0u; i < count; i++)
                {
                    var weapon = await player.InventoryManager.AddWeaponItem(itemType, detail, particular, level, sendPacket: false);
                    if (weapon != null)
                        sync.Items.Add(weapon.ToProto());
                }
                return true;
            case ItemTypeEnum.TYPE_SUPPORT:
                for (var i = 0u; i < count; i++)
                {
                    var support = await player.InventoryManager.AddSupportCardItem(detail, particular, level, sendPacket: false);
                    if (support != null)
                        sync.Items.Add(support.ToProto());
                }
                return true;
            case ItemTypeEnum.TYPE_SUPPLIES:
            {
                var templateId = GameResourceTemplateId.FromGdpl(reward[0], detail, particular, level);
                if (!GameData.SuppliesData.TryGetValue(templateId, out var supplies))
                    return false;

                var item = await player.InventoryManager.AddSuppliesItem(supplies, count, sendPacket: false);
                if (item != null)
                    sync.Items.Add(item.ToProto());
                return true;
            }
            case ItemTypeEnum.TYPE_USEABLE:
            {
                var item = AddOtherItem(player.InventoryManager.InventoryData, reward[0], detail, particular, level, count);
                if (item != null)
                    sync.Items.Add(item.ToProto());
                return true;
            }
            case ItemTypeEnum.TYPE_WEAPON_PART:
                for (var i = 0u; i < count; i++)
                {
                    var item = await player.InventoryManager.AddWeaponPartItem(itemType, detail, particular, level, sendPacket: false);
                    if (item != null)
                        sync.Items.Add(item.ToProto());
                }
                return true;
            case ItemTypeEnum.TYPE_CARD_SKIN:
                for (var i = 0u; i < count; i++)
                {
                    var item = await player.InventoryManager.AddSkinItem(itemType, detail, particular, level, sendPacket: false);
                    if (item != null)
                        sync.Items.Add(item.ToProto());
                }
                return true;
            case ItemTypeEnum.TYPE_HOUSE:
                for (var i = 0u; i < count; i++)
                {
                    var item = await player.InventoryManager.AddHouseFurnitureItem(itemType, detail, particular, level, sendPacket: false);
                    if (item != null)
                        sync.Items.Add(item.ToProto());
                }
                return true;
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
                return true;
            case ItemTypeEnum.TYPE_WEAPON_SKIN:
                for (var i = 0u; i < count; i++)
                {
                    var item = await player.InventoryManager.AddWeaponSkinItem(itemType, detail, particular, level, sendPacket: false);
                    if (item != null)
                        sync.Items.Add(item.ToProto());
                }
                return true;
            default:
                Log.Warn($"VirCapture reward item type {itemType} is not handled");
                return false;
        }
    }

    /// <summary>
    /// Grants a mission reward payload. The table uses two shapes:
    /// [[1,4,1,6,14,500]] (kind + 5 gdpl fields) and {"4":121} (kind -> id).
    /// Item rewards are granted here; island state rewards (buff / level up / worker / building)
    /// are returned to the caller so the build/staff layer can apply them.
    /// </summary>
    public static async Task<(JsonArray AwardList, List<Entry> Deferred)> GrantMissionRewardsAsync(
        PlayerInstance player, NtfSyncPlayer sync, JToken? rewards)
    {
        var awardList = new JsonArray();
        var deferred = new List<Entry>();
        if (rewards == null)
            return (awardList, deferred);

        if (rewards is JArray rows)
        {
            foreach (var row in rows)
            {
                if (row is not JArray cells || cells.Count < 2)
                    continue;

                var kind = ToUInt(cells[0]);
                if (kind == KindItem && cells.Count >= 6)
                {
                    var reward = new List<uint>(5);
                    for (var i = 1; i < 6; i++)
                        reward.Add(ToUInt(cells[i]));

                    if (!await GrantItemRewardAsync(player, sync, reward))
                        continue;

                    awardList.Add(new JsonArray(
                        (int)reward[0], (int)reward[1], (int)reward[2], (int)reward[3], (int)reward[4]));
                }
                else
                {
                    var param = cells.Count >= 3 ? ToUInt(cells[1]) : 0u;
                    var value = ToUInt(cells[cells.Count - 1]);
                    deferred.Add(new Entry(kind, param, value));
                    Log.Info($"VirCapture mission reward kind {kind} param {param} -> {value} deferred to island state layer");
                }
            }

            return (awardList, deferred);
        }

        if (rewards is JObject map)
        {
            foreach (var pair in map)
            {
                if (!uint.TryParse(pair.Key, out var kind))
                    continue;

                var value = ToUInt(pair.Value);
                deferred.Add(new Entry(kind, 0u, value));
                Log.Info($"VirCapture mission reward kind {kind} -> {value} deferred to island state layer");
            }
        }

        return (awardList, deferred);
    }

    public static void Save(PlayerInstance player)
    {
        DatabaseHelper.SaveDatabaseType(player.Data);
        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
        DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
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

    public static uint ToUInt(JToken? token)
    {
        if (token == null)
            return 0;

        return token.Type switch
        {
            JTokenType.Integer => (uint)Math.Max(0, token.Value<long>()),
            JTokenType.Float => (uint)Math.Max(0, token.Value<double>()),
            JTokenType.String => uint.TryParse(token.Value<string>(), out var parsed) ? parsed : 0,
            _ => 0
        };
    }

    /// <summary>Mission reward descriptor after decoding.</summary>
    public readonly record struct Entry(uint Kind, uint Param, uint Value);
}
