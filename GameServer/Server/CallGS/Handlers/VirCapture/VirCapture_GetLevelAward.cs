using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.Database.Player;
using MikuSB.Enums.Item;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

[CallGSApi("VirCapture_GetLevelAward")]
public class VirCapture_GetLevelAward : CallGSHandler<VirCaptureGetLevelAwardParam>
{
    private const uint VirCaptureGroupId = AttrIds.VirCapture.Gid;
    private const uint CurLevelSid = AttrIds.VirCapture.CurrentLevelSid;
    private const uint LevelAwardFlagStartSid = AttrIds.VirCapture.LevelAwardFlagStartSid;
    private const uint LevelAwardFlagEndSid = AttrIds.VirCapture.LevelAwardFlagEndSid;

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, VirCaptureGetLevelAwardParam req)
    {
        var player = context.Connection.Player!;

        if (req == null || req.IdList == null || req.IdList.Count == 0)
        {
            return CallGSResult.Ok("{\"tbAwardList\":[]}");
        }

        var curLevel = player.Attributes.GetValue(VirCaptureGroupId, CurLevelSid);
        var requestedLevels = req.IdList
            .Where(x => x > 0)
            .Select(x => (uint)x)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        var claimLevels = requestedLevels
            .Where(level => level <= curLevel && CanClaimLevel(player, level))
            .ToList();

        var sync = new NtfSyncPlayer();
        var responseAwards = new JsonArray();

        foreach (var level in claimLevels)
        {
            if (!GameData.VirCaptureLevelListData.TryGetValue(level, out var levelCfg) ||
                levelCfg.Rewards.Count == 0)
            {
                continue;
            }

            SetClaimed(player, sync, level);

            foreach (var reward in levelCfg.Rewards)
            {
                if (reward.Count < 5)
                    continue;

                await GrantRewardAsync(player, sync, reward);
                responseAwards.Add(new JsonArray(
                    (int)reward[0],
                    (int)reward[1],
                    (int)reward[2],
                    (int)reward[3],
                    (int)reward[4]));
            }
        }

        DatabaseHelper.SaveDatabaseType(player.Data);
        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
        DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);

        var rsp = new JsonObject
        {
            ["tbAwardList"] = responseAwards
        };
        return CallGSResult.Ok(rsp.ToJsonString(), sync);
    }

    private static bool CanClaimLevel(PlayerInstance player, uint level)
    {
        var sid = GetLevelAwardSid(level);
        if (sid < LevelAwardFlagStartSid || sid > LevelAwardFlagEndSid)
            return false;

        var pos = GetLevelAwardBit(level);
        return (player.Attributes.GetValue(VirCaptureGroupId, sid) & (1u << pos)) == 0;
    }

    private static void SetClaimed(PlayerInstance player, NtfSyncPlayer sync, uint level)
    {
        var sid = GetLevelAwardSid(level);
        var pos = GetLevelAwardBit(level);
        var attr = player.Attributes.GetOrCreate(VirCaptureGroupId, sid);
        attr.Val |= 1u << pos;
        player.Attributes.SyncTo(sync, attr);
    }

    private static uint GetLevelAwardSid(uint level) => LevelAwardFlagStartSid + (level / 30);

    private static int GetLevelAwardBit(uint level) => (int)(level % 30);

    private static async Task GrantRewardAsync(PlayerInstance player, NtfSyncPlayer sync, IReadOnlyList<uint> reward)
    {
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
                if (GameData.SuppliesData.TryGetValue(templateId, out var supplies))
                {
                    var item = await player.InventoryManager.AddSuppliesItem(supplies, count, sendPacket: false);
                    if (item != null)
                        sync.Items.Add(item.ToProto());
                }
                break;
            }
            case ItemTypeEnum.TYPE_USEABLE:
            {
                var item = AddOtherItem(player.InventoryManager.InventoryData, reward[0], detail, particular, level, count);
                if (item != null)
                    sync.Items.Add(item.ToProto());
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
}

public sealed class VirCaptureGetLevelAwardParam
{
    [JsonPropertyName("nId")]
    public int ActId { get; set; }

    [JsonPropertyName("tbIdList")]
    public List<int> IdList { get; set; } = [];
}
