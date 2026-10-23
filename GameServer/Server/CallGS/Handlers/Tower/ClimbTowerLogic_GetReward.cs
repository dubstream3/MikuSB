using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.Enums.Item;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Tower;

[CallGSApi("ClimbTowerLogic_GetReward")]
public class ClimbTowerLogic_GetReward : CallGSHandler<ClimbTowerGetRewardParam>
{
    private const uint TowerGroupId = AttrIds.Tower.Gid;
    private const uint RewardStateSidBase = AttrIds.Tower.RewardStateSidBase;
    private const uint TowerLevelStateSidBase = AttrIds.Tower.LevelStateSidBase;
    private const uint LaunchPassGroupId = AttrIds.Tower.PassGid;
    private const uint AdvancedDiffSid = AttrIds.Tower.DiffSid;

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, ClimbTowerGetRewardParam req)
    {
        var player = context.Connection.Player!;

        if (req == null || req.Layer <= 0)
        {
            return CallGSResult.Error("error.BadParam");
        }

        var cycle = ResolveCurrentCycle(GameData.ClimbTowerTimeData.Values, DateTime.Now);
        if (cycle == null)
        {
            return CallGSResult.Error("error.BadParam");
        }

        if (!TryResolveLayer(cycle, req.Layer, player, out var towerIds, out var diff))
        {
            return CallGSResult.Error("error.BadParam");
        }

        if (!GameData.ClimbTowerAwardData.TryGetValue((uint)req.Layer, out var diffMap) ||
            !diffMap.TryGetValue(diff, out var rewardCfg))
        {
            return CallGSResult.Error("error.BadParam");
        }

        var groups = ResolveRequestedGroups(req.Group);
        if (groups.Count == 0)
        {
            return CallGSResult.Error("error.BadParam");
        }

        var claimableGroups = groups
            .Where(group => CanClaimGroup(player, rewardCfg, towerIds, req.Layer, group))
            .Distinct()
            .ToList();

        if (claimableGroups.Count == 0)
        {
            return CallGSResult.Error("error.BadParam");
        }

        var sync = new NtfSyncPlayer();
        var rewardStateAttr = player.Attributes.GetOrCreate(TowerGroupId, RewardStateSidBase + (uint)req.Layer);
        var responseRewards = new JsonArray();

        foreach (var group in claimableGroups)
        {
            rewardStateAttr.Val |= 1u << GetFlagBitOffset(group);

            foreach (var reward in rewardCfg.GetRewards(group))
            {
                if (reward.Count < 5)
                    continue;

                await GrantRewardAsync(player, sync, reward);
                responseRewards.Add(new JsonArray(
                    (int)reward[0],
                    (int)reward[1],
                    (int)reward[2],
                    (int)reward[3],
                    (int)reward[4]));
            }
        }

        player.Attributes.SyncTo(sync, rewardStateAttr);
        DatabaseHelper.SaveDatabaseType(player.Data);
        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
        DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);

        var rsp = new JsonObject
        {
            ["tbRewards"] = responseRewards
        };

        return CallGSResult.Ok(rsp.ToJsonString(), sync);
    }

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

    private static bool CanClaimGroup(
        PlayerInstance player,
        ClimbTowerAwardExcel rewardCfg,
        IReadOnlyList<uint> towerIds,
        int layer,
        int group)
    {
        if (group is < 0 or > 3 || IsRewardClaimed(player, layer, group))
            return false;

        if (group == 0)
            return IsLayerPass(player, towerIds);

        var requiredStar = rewardCfg.GetStarCount(group);
        return requiredStar > 0 && GetLayerStar(player, towerIds) >= requiredStar;
    }

    private static bool IsLayerPass(PlayerInstance player, IReadOnlyList<uint> towerIds)
    {
        foreach (var towerId in towerIds)
        {
            if (!GameData.ClimbTowerLevelOrderData.TryGetValue(towerId, out var orderCfg))
                return false;

            if (player.Attributes.GetValue(LaunchPassGroupId, orderCfg.LevelID) == 0)
                return false;
        }

        return true;
    }

    private static int GetLayerStar(PlayerInstance player, IReadOnlyList<uint> towerIds)
    {
        var total = 0;
        foreach (var towerId in towerIds)
        {
            var value = player.Attributes.GetValue(TowerGroupId, TowerLevelStateSidBase + towerId);
            for (var i = 0; i < 9; i++)
            {
                if (((value >> i) & 1u) != 0)
                    total++;
            }
        }

        return total;
    }

    private static bool IsRewardClaimed(PlayerInstance player, int layer, int group)
    {
        var offset = GetFlagBitOffset(group);
        return ((player.Attributes.GetValue(TowerGroupId, RewardStateSidBase + (uint)layer) >> offset) & 0xFu) > 0;
    }

    private static int GetFlagBitOffset(int group) => group switch
    {
        0 => 0,
        1 => 4,
        2 => 8,
        3 => 12,
        _ => 0
    };

    private static List<int> ResolveRequestedGroups(int? group)
    {
        if (!group.HasValue)
            return [0, 1, 2, 3];

        return group.Value is >= 0 and <= 3 ? [group.Value] : [];
    }

    private static bool TryResolveLayer(
        ClimbTowerTimeExcel cycle,
        int layer,
        PlayerInstance player,
        out IReadOnlyList<uint> towerIds,
        out int diff)
    {
        var basicGroups = cycle.GetLevelGroups(1);
        if (layer <= basicGroups.Count)
        {
            towerIds = basicGroups[layer - 1];
            diff = 1;
            return towerIds.Count > 0;
        }

        var advancedIndex = layer - basicGroups.Count;
        var advancedGroups = cycle.GetLevelGroups(2);
        if (advancedIndex <= 0 || advancedIndex > advancedGroups.Count)
        {
            towerIds = [];
            diff = 0;
            return false;
        }

        diff = (int)player.Attributes.GetValue(TowerGroupId, AdvancedDiffSid);
        towerIds = advancedGroups[advancedIndex - 1];
        return diff > 0 && towerIds.Count > 0;
    }

    private static ClimbTowerTimeExcel? ResolveCurrentCycle(IEnumerable<ClimbTowerTimeExcel> configs, DateTime now)
    {
        var parsed = configs
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
            return current.Config;

        var latestStarted = parsed.LastOrDefault(x => x.Start <= now);
        if (latestStarted != null)
            return latestStarted.Config;

        return parsed.FirstOrDefault()?.Config;
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

public sealed class ClimbTowerGetRewardParam
{
    [JsonPropertyName("nType")]
    public int? Type { get; set; }

    [JsonPropertyName("nLayer")]
    public int Layer { get; set; }

    [JsonPropertyName("nGroup")]
    public int? Group { get; set; }
}
