using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database.Inventory;
using MikuSB.Enums.Item;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using Newtonsoft.Json.Linq;

namespace MikuSB.GameServer.Game.Reward;

public sealed class RewardManager(PlayerInstance player) : BasePlayerManager(player)
{
    private static readonly Logger Logger = new("Reward");

    public async ValueTask<JsonArray> GrantLevelRewardsAsync(
        ILevelRewardConfig levelConfig,
        bool isFirstClear,
        uint seed,
        NtfSyncPlayer sync)
    {
        var random = new Random(unchecked((int)(seed ^ levelConfig.ID)));
        var response = new JsonArray
        {
            await GrantDropCategoryAsync(levelConfig.FirstDropIds(), isFirstClear, random, sync),
            await GrantDropCategoryAsync(levelConfig.BaseDropIds(), !isFirstClear, random, sync),
            await GrantDropCategoryAsync(levelConfig.RandomDropIds(), true, random, sync)
        };

        await GrantPlayerExperienceAsync(levelConfig.PlayerExp(), sync);
        Player.CharacterManager.AddExperienceToLineup(levelConfig.RoleExp(), sync, Player.ActiveLevelTeamId);
        return response;
    }

    public JsonArray ResolveLevelRewards(ILevelRewardConfig levelConfig, bool isFirstClear, uint seed)
    {
        var random = new Random(unchecked((int)(seed ^ levelConfig.ID)));
        return new JsonArray
        {
            BuildDropCategory(levelConfig.FirstDropIds(), isFirstClear, random),
            BuildDropCategory(levelConfig.BaseDropIds(), !isFirstClear, random),
            BuildDropCategory(levelConfig.RandomDropIds(), true, random)
        };
    }

    public async ValueTask<JsonArray> GrantConfiguredRewardsAsync(
        IEnumerable<IReadOnlyList<uint>> rewardRows,
        NtfSyncPlayer sync)
    {
        var response = new JsonArray();
        foreach (var row in rewardRows)
        {
            if (row.Count < 5 || row[4] == 0)
                continue;

            var reward = new RewardEntry(row[0], row[1], row[2], row[3], row[4]);
            if (await GrantRewardAsync(reward, sync))
                response.Add(reward.ToJson());
        }

        return response;
    }

    public NtfSyncPlayer BuildFullSync()
    {
        var sync = new NtfSyncPlayer();
        sync.Core[(uint)PlayerCoreAttribute.Level] = Player.Data.Level;
        sync.Core[(uint)PlayerCoreAttribute.Exp] = (uint)Math.Max(0, Player.Data.Exp);
        sync.Core[(uint)PlayerCoreAttribute.Vigor] = Player.Data.Vigor;

        foreach (var attr in Player.Attributes.All)
            Player.Attributes.SyncTo(sync, attr);
        Player.QuestManager.SyncTestingStateTo(sync);

        foreach (var item in Player.CharacterManager.CharacterData.Characters)
            sync.Items.Add(item.ToProto());
        foreach (var item in Player.InventoryManager.InventoryData.Items.Values)
            sync.Items.Add(item.ToProto());
        foreach (var item in Player.InventoryManager.InventoryData.Skins.Values)
            sync.Items.Add(item.ToProto());
        foreach (var item in Player.InventoryManager.InventoryData.Weapons.Values)
            sync.Items.Add(item.ToProto());
        foreach (var item in Player.InventoryManager.InventoryData.SupportCards.Values)
            sync.Items.Add(item.ToProto());

        foreach (var (key, value) in Player.BuildMoneySync())
            sync.Money[key] = value;
        return sync;
    }

    private async ValueTask<JsonArray> GrantDropCategoryAsync(
        IReadOnlyList<uint> dropIds,
        bool enabled,
        Random random,
        NtfSyncPlayer sync)
    {
        var rewards = ResolveDropIds(dropIds, enabled, random);
        var response = new JsonArray();
        foreach (var reward in rewards)
        {
            if (await GrantRewardAsync(reward, sync))
                response.Add(reward.ToJson());
        }

        return response;
    }

    private JsonArray BuildDropCategory(IReadOnlyList<uint> dropIds, bool enabled, Random random)
    {
        var response = new JsonArray();
        foreach (var reward in ResolveDropIds(dropIds, enabled, random))
            response.Add(reward.ToJson());
        return response;
    }

    private List<RewardEntry> ResolveDropIds(IReadOnlyList<uint> dropIds, bool enabled, Random random)
    {
        if (!enabled)
            return [];

        var rewards = new List<RewardEntry>();
        foreach (var dropId in dropIds)
        {
            if (!GameData.DropData.TryGetValue(dropId, out var dropConfig))
            {
                Logger.Warn($"Drop config not found. dropId={dropId}");
                continue;
            }

            foreach (var drop in dropConfig.Drop)
            {
                if (drop.Count < 3 || drop[0] == 0 || drop[2] == 0)
                    continue;

                if (!GameData.DropGroupData.TryGetValue(drop[0], out var groupConfig))
                {
                    Logger.Warn($"Drop group config not found. groupId={drop[0]}");
                    continue;
                }

                var multiplier = drop[2];
                ResolveDropGroup(groupConfig, multiplier, random, rewards);
            }
        }

        return rewards;
    }

    private static void ResolveDropGroup(
        DropGroupExcel groupConfig,
        uint multiplier,
        Random random,
        ICollection<RewardEntry> rewards)
    {
        if (groupConfig.GroupRaw is not JArray groups)
            return;

        if (groupConfig.RandType == 1)
        {
            for (var i = 0u; i < multiplier; i++)
            {
                var selected = SelectWeighted(groups, random);
                if (selected != null && TryReadReward(selected, out var reward))
                    rewards.Add(reward);
            }

            return;
        }

        if (groupConfig.RandType == 2)
        {
            var selected = SelectWeighted(groups, random);
            if (selected == null || selected.Count < 3 || selected[2] is not JArray nestedRewards)
                return;

            var drawCount = ReadUInt(selected[0]);
            for (var i = 0u; i < multiplier * drawCount; i++)
            {
                var nested = SelectWeighted(nestedRewards, random);
                if (nested != null && TryReadReward(nested, out var reward))
                    rewards.Add(reward);
            }

            return;
        }

        foreach (var group in groups)
        {
            if (group is JArray rewardRow && TryReadReward(rewardRow, out var reward))
            {
                reward = reward with { Count = checked(reward.Count * multiplier) };
                rewards.Add(reward);
            }
        }
    }

    private static JArray? SelectWeighted(JArray entries, Random random)
    {
        var weighted = entries
            .OfType<JArray>()
            .Where(entry => entry.Count >= 2 && ReadUInt(entry[1]) > 0)
            .ToArray();
        if (weighted.Length == 0)
            return null;

        var totalWeight = weighted.Aggregate(0UL, (total, entry) => total + ReadUInt(entry[1]));
        var value = (ulong)(random.NextDouble() * totalWeight);
        foreach (var entry in weighted)
        {
            var weight = ReadUInt(entry[1]);
            if (value < weight)
                return entry;
            value -= weight;
        }

        return weighted[^1];
    }

    private static bool TryReadReward(JArray entry, out RewardEntry reward)
    {
        reward = default;
        if (entry.Count < 3 || entry[0] is not JArray gdpl || gdpl.Count < 4)
            return false;

        var values = gdpl.Select(ReadUInt).ToArray();
        var count = ReadUInt(entry[2]);
        if (values.Any(x => x == 0) || count == 0)
            return false;

        reward = new RewardEntry(values[0], values[1], values[2], values[3], count);
        return true;
    }

    private async ValueTask GrantPlayerExperienceAsync(uint amount, NtfSyncPlayer sync)
    {
        var levels = Player.AddPlayerExperience(amount, sync);
        foreach (var level in levels)
        {
            foreach (var reward in level.GiftItems)
            {
                if (reward.Count < 5)
                    continue;

                await GrantRewardAsync(
                    new RewardEntry(reward[0], reward[1], reward[2], reward[3], reward[4]),
                    sync,
                    grantPlayerLevelRewards: false);
            }
        }
    }

    private async ValueTask<bool> GrantRewardAsync(
        RewardEntry reward,
        NtfSyncPlayer sync,
        bool grantPlayerLevelRewards = true)
    {
        var itemType = (ItemTypeEnum)reward.Genre;
        switch (itemType)
        {
            case ItemTypeEnum.TYPE_CARD:
                for (var i = 0u; i < reward.Count; i++)
                {
                    var character = await Player.CharacterManager.AddCharacter(itemType, reward.Detail, reward.Particular, reward.Level, sendPacket: false);
                    if (character != null)
                        sync.Items.Add(character.ToProto());
                }
                return true;
            case ItemTypeEnum.TYPE_WEAPON:
                for (var i = 0u; i < reward.Count; i++)
                {
                    var item = await Player.InventoryManager.AddWeaponItem(itemType, reward.Detail, reward.Particular, reward.Level, sendPacket: false);
                    if (item != null)
                        sync.Items.Add(item.ToProto());
                }
                return true;
            case ItemTypeEnum.TYPE_SUPPORT:
                for (var i = 0u; i < reward.Count; i++)
                {
                    var item = await Player.InventoryManager.AddSupportCardItem(reward.Detail, reward.Particular, reward.Level, sendPacket: false);
                    if (item != null)
                        sync.Items.Add(item.ToProto());
                }
                return true;
            case ItemTypeEnum.TYPE_SUPPLIES:
            {
                var templateId = GameResourceTemplateId.FromGdpl(reward.Genre, reward.Detail, reward.Particular, reward.Level);
                if (!GameData.SuppliesData.TryGetValue(templateId, out var supplies))
                    return false;

                var item = await Player.InventoryManager.AddSuppliesItem(supplies, reward.Count, sendPacket: false);
                if (item != null)
                    sync.Items.Add(item.ToProto());
                return item != null;
            }
            case ItemTypeEnum.TYPE_USEABLE:
                return await GrantOtherItemAsync(reward, sync, grantPlayerLevelRewards);
            case ItemTypeEnum.TYPE_WEAPON_PART:
                return await AddRepeatedItemsAsync(reward, sync, (g, d, p, l) => Player.InventoryManager.AddWeaponPartItem(g, d, p, l, false));
            case ItemTypeEnum.TYPE_CARD_SKIN:
                return await AddRepeatedItemsAsync(reward, sync, (g, d, p, l) => Player.InventoryManager.AddSkinItem(g, d, p, l, false));
            case ItemTypeEnum.TYPE_HOUSE:
                return await AddRepeatedItemsAsync(reward, sync, (g, d, p, l) => Player.InventoryManager.AddHouseFurnitureItem(g, d, p, l, false));
            case ItemTypeEnum.TYPE_PROFILE:
            case ItemTypeEnum.TYPE_FRAME:
            case ItemTypeEnum.TYPE_BADGE:
            case ItemTypeEnum.TYPE_COVER:
            case ItemTypeEnum.TYPE_NAMECARD:
            case ItemTypeEnum.TYPE_EXPRESSION:
            case ItemTypeEnum.TYPE_BUBBLE:
            case ItemTypeEnum.TYPE_ANALYST:
                return await AddRepeatedItemsAsync(reward, sync, (g, d, p, l) => Player.InventoryManager.AddProfileItem(g, d, p, l, false));
            case ItemTypeEnum.TYPE_WEAPON_SKIN:
                return await AddRepeatedItemsAsync(reward, sync, (g, d, p, l) => Player.InventoryManager.AddWeaponSkinItem(g, d, p, l, false));
            case ItemTypeEnum.TYPE_MANIFESTATION:
                return await AddRepeatedItemsAsync(reward, sync, (g, d, p, l) => Player.InventoryManager.AddManifestationItem(g, d, p, l, false));
            case ItemTypeEnum.TYPE_CARD_SKIN_PART:
                return await AddRepeatedItemsAsync(reward, sync, (g, d, p, l) => Player.InventoryManager.AddSkinPartItem(g, d, p, l, false));
            case ItemTypeEnum.TYPE_AR:
                return await AddRepeatedItemsAsync(reward, sync, (g, d, p, l) => Player.InventoryManager.AddArItem(g, d, p, l, false));
            case ItemTypeEnum.TYPE_CALL:
                return await AddRepeatedItemsAsync(reward, sync, (g, d, p, l) => Player.InventoryManager.AddCallItem(g, d, p, l, false));
            case ItemTypeEnum.TYPE_MONSTER_CARD:
                return await AddRepeatedItemsAsync(reward, sync, (g, d, p, l) => Player.InventoryManager.AddMonsterCardItem(d, p, l, false));
            default:
                Logger.Warn($"Unsupported reward item type. genre={reward.Genre} detail={reward.Detail} particular={reward.Particular} level={reward.Level}");
                return false;
        }
    }

    private async ValueTask<bool> GrantOtherItemAsync(RewardEntry reward, NtfSyncPlayer sync, bool grantPlayerLevelRewards)
    {
        var templateId = GameResourceTemplateId.FromGdpl(reward.Genre, reward.Detail, reward.Particular, reward.Level);
        if (!GameData.OtherItemData.TryGetValue(templateId, out var otherItem))
            return false;

        switch (otherItem.LuaType)
        {
            case "money_box":
                Player.AddCurrency(AttrIds.Currency.Money, Multiply(otherItem.Param1, reward.Count), sync);
                return true;
            case "gold_box":
                Player.AddCurrency(AttrIds.Currency.Gold, Multiply(otherItem.Param1, reward.Count), sync);
                return true;
            case "silver_box":
                Player.AddCurrency(AttrIds.Currency.Silver, Multiply(otherItem.Param1, reward.Count), sync);
                return true;
            case "vigor_box":
                Player.AddCurrency(AttrIds.Currency.Vigor, Multiply(otherItem.Param1, reward.Count), sync);
                return true;
            case "playerexp_box":
            {
                var levels = Player.AddPlayerExperience(Multiply(otherItem.Param1, reward.Count), sync);
                if (grantPlayerLevelRewards)
                {
                    foreach (var level in levels)
                    {
                        foreach (var gift in level.GiftItems)
                        {
                            if (gift.Count >= 5)
                                await GrantRewardAsync(new RewardEntry(gift[0], gift[1], gift[2], gift[3], gift[4]), sync, false);
                        }
                    }
                }

                return true;
            }
            case "cashbox" when otherItem.Param1 > 0:
                Player.AddCurrency(otherItem.Param1, reward.Count, sync);
                return true;
            default:
            {
                var item = await Player.InventoryManager.AddOtherItem(
                    (ItemTypeEnum)reward.Genre,
                    reward.Detail,
                    reward.Particular,
                    reward.Level,
                    reward.Count,
                    sendPacket: false);
                if (item != null)
                    sync.Items.Add(item.ToProto());
                return item != null;
            }
        }
    }

    private static async ValueTask<bool> AddRepeatedItemsAsync<T>(
        RewardEntry reward,
        NtfSyncPlayer sync,
        Func<ItemTypeEnum, uint, uint, uint, ValueTask<T?>> addItem)
        where T : BaseGameItemInfo
    {
        var granted = false;
        for (var i = 0u; i < reward.Count; i++)
        {
            var item = await addItem((ItemTypeEnum)reward.Genre, reward.Detail, reward.Particular, reward.Level);
            if (item == null)
                continue;

            granted = true;
            sync.Items.Add(item.ToProto());
        }

        return granted;
    }

    private static uint Multiply(uint left, uint right) =>
        (uint)Math.Min(uint.MaxValue, (ulong)left * right);

    private static uint ReadUInt(JToken? token)
    {
        if (token == null)
            return 0;

        return token.Type switch
        {
            JTokenType.Integer => token.Value<uint>(),
            JTokenType.Float => (uint)Math.Max(0, token.Value<decimal>()),
            JTokenType.String when uint.TryParse(token.Value<string>(), out var value) => value,
            _ => 0
        };
    }

    private readonly record struct RewardEntry(uint Genre, uint Detail, uint Particular, uint Level, uint Count)
    {
        public JsonArray ToJson() => new((int)Genre, (int)Detail, (int)Particular, (int)Level, (int)Count);
    }
}
