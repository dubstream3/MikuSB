using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.Enums.Item;
using MikuSB.Proto;
using Newtonsoft.Json.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Inventory;

[CallGSApi("Item_Recycle")]
public class Item_Recycle : CallGSHandler<ItemRecycleParam>
{
    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, ItemRecycleParam req)
    {
        var player = context.Connection.Player!;

        if (req?.TbItems == null || req.TbItems.Count == 0)
        {
            return CallGSResult.Error("error.BadParam");
        }

        var config = RecycleConfig.Load();

        var itemsToRecycle = new List<(BaseGameItemInfo Item, int RecycleId)>();
        foreach (var uniqueId in req.TbItems)
        {
            BaseGameItemInfo? item = player.InventoryManager.GetWeaponItem((uint)uniqueId)
                ?? (BaseGameItemInfo?)player.InventoryManager.GetSupportCardItem((uint)uniqueId);

            if (item == null)
            {
                return CallGSResult.Error("error.Recycle.ItemNotExists");
            }

            var recycleId = GetRecycleId(item);
            if (recycleId <= 0 || !config.HasConfig(recycleId))
            {
                return CallGSResult.Error("error.Recycle.ItemCanNotRecycle");
            }

            itemsToRecycle.Add((item, recycleId));
        }

        var sync = new NtfSyncPlayer();

        foreach (var (item, recycleId) in itemsToRecycle)
        {
            var rewards = config.CalcRewards(item, recycleId);
            foreach (var reward in rewards)
                await GrantRewardAsync(player, sync, reward);

            RemoveItem(player.InventoryManager.InventoryData, item, sync);
        }

        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);

        return CallGSResult.Ok("{}", sync);
    }

    private static int GetRecycleId(BaseGameItemInfo item)
    {
        if (item.ItemType == ItemTypeEnum.TYPE_WEAPON)
        {
            var t = GameData.WeaponData.Values.FirstOrDefault(x =>
                GameResourceTemplateId.FromGdpl(x.Genre, x.Detail, x.Particular, x.Level) == item.TemplateId);
            return t?.RecycleID ?? 0;
        }
        if (item.ItemType == ItemTypeEnum.TYPE_SUPPORT)
        {
            var t = GameData.SupportCardData.FirstOrDefault(x => x.TemplateId == item.TemplateId);
            return t?.RecycleID ?? 0;
        }
        return 0;
    }

    private static void RemoveItem(InventoryData inventory, BaseGameItemInfo item, NtfSyncPlayer sync)
    {
        var removed = item.ToProto();
        removed.Count = 0;
        sync.Items.Add(removed);

        if (item.ItemType == ItemTypeEnum.TYPE_WEAPON)
            inventory.Weapons.Remove(item.UniqueId);
        else
            inventory.SupportCards.Remove(item.UniqueId);
    }

    private static async Task GrantRewardAsync(GameServer.Game.Player.PlayerInstance player, NtfSyncPlayer sync, IReadOnlyList<uint> reward)
    {
        if (reward.Count < 5) return;

        var itemType = (ItemTypeEnum)reward[0];
        var detail = reward[1];
        var particular = reward[2];
        var level = reward[3];
        var count = Math.Max(1u, reward[4]);

        switch (itemType)
        {
            case ItemTypeEnum.TYPE_SUPPLIES:
            {
                var templateId = GameResourceTemplateId.FromGdpl(reward[0], detail, particular, level);
                if (!GameData.SuppliesData.TryGetValue(templateId, out var supplies)) break;
                var item = await player.InventoryManager.AddSuppliesItem(supplies, count, sendPacket: false);
                if (item != null) sync.Items.Add(item.ToProto());
                break;
            }
        }
    }
}

internal sealed class RecycleConfig
{
    private readonly Dictionary<int, RecycleEntry> _entries;
    private readonly List<SupplyTemplate> _weaponSupplies;
    private readonly List<SupplyTemplate> _supportSupplies;
    private readonly Dictionary<int, ulong> _weaponLevelExp;
    private readonly Dictionary<int, ulong> _supportLevelExp;

    private readonly Dictionary<int, ulong> _weaponLevelExpSsr;
    private readonly Dictionary<int, ulong> _supportLevelExpSsr;

    private RecycleConfig(
        Dictionary<int, RecycleEntry> entries,
        List<SupplyTemplate> weaponSupplies,
        List<SupplyTemplate> supportSupplies,
        Dictionary<int, ulong> weaponLevelExp,
        Dictionary<int, ulong> weaponLevelExpSsr,
        Dictionary<int, ulong> supportLevelExp,
        Dictionary<int, ulong> supportLevelExpSsr)
    {
        _entries = entries;
        _weaponSupplies = weaponSupplies;
        _supportSupplies = supportSupplies;
        _weaponLevelExp = weaponLevelExp;
        _weaponLevelExpSsr = weaponLevelExpSsr;
        _supportLevelExp = supportLevelExp;
        _supportLevelExpSsr = supportLevelExpSsr;
    }

    public static RecycleConfig Load()
    {
        var entries = new Dictionary<int, RecycleEntry>();
        foreach (var row in GameData.RecycleData.Values)
        {
            var fixedRewards = ParseRewards(row.RecycleReward);
            var recycleBase = GetUInt(row.RecycleBase);
            var recycleRatio = GetDecimal(row.RecycleRatio);
            entries[row.ID] = new RecycleEntry(fixedRewards, recycleBase, recycleRatio);
        }

        var weaponSupplies = new List<SupplyTemplate>();
        var supportSupplies = new List<SupplyTemplate>();
        foreach (var s in GameData.AllSuppliesData)
        {
            if (s.ProvideExp == 0) continue;
            if (s.Genre == 5 && s.Detail == 2)
                weaponSupplies.Add(new SupplyTemplate(s.Genre, s.Detail, s.Particular, s.Level, s.ProvideExp));
            else if (s.Genre == 5 && s.Detail == 3)
                supportSupplies.Add(new SupplyTemplate(s.Genre, s.Detail, s.Particular, s.Level, s.ProvideExp));
        }
        weaponSupplies.Sort((a, b) => b.ProvideExp.CompareTo(a.ProvideExp));
        supportSupplies.Sort((a, b) => b.ProvideExp.CompareTo(a.ProvideExp));

        var weaponLevelExp = BuildLevelExpTable(GameData.UpgradeExpData.Values.Select(x => (x.Lv, x.WeaponNeedExp)));
        var weaponLevelExpSsr = BuildLevelExpTable(GameData.UpgradeExpData.Values.Select(x => (x.Lv, x.SSRWeaponNeedExp)));
        var supportLevelExp = BuildLevelExpTable(GameData.UpgradeExpData.Values.Select(x => (x.Lv, x.SusNeedExp)));
        var supportLevelExpSsr = BuildLevelExpTable(GameData.UpgradeExpData.Values.Select(x => (x.Lv, x.SSRSusNeedExp)));

        return new RecycleConfig(entries, weaponSupplies, supportSupplies, weaponLevelExp, weaponLevelExpSsr, supportLevelExp, supportLevelExpSsr);
    }

    public bool HasConfig(int recycleId) => _entries.ContainsKey(recycleId);

    public List<IReadOnlyList<uint>> CalcRewards(BaseGameItemInfo item, int recycleId)
    {
        if (!_entries.TryGetValue(recycleId, out var entry))
            return [];

        var rewards = new List<IReadOnlyList<uint>>(entry.FixedRewards);

        var expRewards = CalcExpRewards(item, entry);
        rewards.AddRange(expRewards);

        return rewards;
    }

    private List<IReadOnlyList<uint>> CalcExpRewards(BaseGameItemInfo item, RecycleEntry entry)
    {
        if (entry.RecycleRatio == 0) return [];

        List<SupplyTemplate> supplies;
        Dictionary<int, ulong> levelExp;

        if (item.ItemType == ItemTypeEnum.TYPE_WEAPON)
        {
            supplies = _weaponSupplies;
            var color = GetItemColor(item);
            levelExp = color == 5 ? _weaponLevelExpSsr : _weaponLevelExp;
        }
        else if (item.ItemType == ItemTypeEnum.TYPE_SUPPORT)
        {
            supplies = _supportSupplies;
            var color = GetItemColor(item);
            levelExp = color == 5 ? _supportLevelExpSsr : _supportLevelExp;
        }
        else
        {
            return [];
        }

        var baseExp = (ulong)entry.RecycleBase;
        var levelAccum = levelExp.GetValueOrDefault((int)item.Level);
        var totalExp = (ulong)Math.Floor((baseExp + levelAccum + item.Exp) * (double)entry.RecycleRatio);

        if (totalExp == 0 || supplies.Count == 0) return [];

        var rewards = new List<IReadOnlyList<uint>>();
        var remaining = totalExp;
        foreach (var supply in supplies)
        {
            if (remaining == 0) break;
            var count = remaining / supply.ProvideExp;
            if (count == 0) continue;
            remaining -= count * supply.ProvideExp;
            rewards.Add([supply.Genre, supply.Detail, supply.Particular, supply.Level, (uint)Math.Min(count, 99999)]);
        }
        return rewards;
    }

    private static List<IReadOnlyList<uint>> ParseRewards(JToken? token)
    {
        if (token == null) return [];

        if (token is JArray outerArray)
        {
            var rewards = new List<IReadOnlyList<uint>>();
            foreach (var element in outerArray)
            {
                if (element is JArray inner && inner.Count >= 4)
                {
                    var reward = inner.Select(x => x.Value<uint>()).ToArray();
                    if (reward.Length < 5)
                        reward = [.. reward, 1];
                    rewards.Add(reward);
                }
            }
            return rewards;
        }

        return [];
    }

    private static int GetItemColor(BaseGameItemInfo item)
    {
        if (item.ItemType == ItemTypeEnum.TYPE_WEAPON)
        {
            var t = GameData.WeaponData.Values.FirstOrDefault(x =>
                GameResourceTemplateId.FromGdpl(x.Genre, x.Detail, x.Particular, x.Level) == item.TemplateId);
            return t?.Color ?? 0;
        }
        if (item.ItemType == ItemTypeEnum.TYPE_SUPPORT)
        {
            var t = GameData.SupportCardData.FirstOrDefault(x => x.TemplateId == item.TemplateId);
            return (int)(t?.Color ?? 0);
        }
        return 0;
    }

    private static Dictionary<int, ulong> BuildLevelExpTable(IEnumerable<(int Lv, uint NeedExp)> source)
    {
        var table = new Dictionary<int, ulong>();
        ulong accumulated = 0;
        foreach (var (lv, needExp) in source.OrderBy(x => x.Lv))
        {
            table[lv] = accumulated;
            accumulated += needExp;
        }
        return table;
    }

    private static uint GetUInt(JToken? token) => token?.Type switch
    {
        JTokenType.Integer => token.Value<uint>(),
        JTokenType.Float => (uint)Math.Max(0, token.Value<decimal>()),
        JTokenType.String when uint.TryParse(token.Value<string>(), out var r) => r,
        _ => 0
    };

    private static decimal GetDecimal(JToken? token) => token?.Type switch
    {
        JTokenType.Integer => token.Value<decimal>(),
        JTokenType.Float => token.Value<decimal>(),
        JTokenType.String when decimal.TryParse(token.Value<string>(), out var r) => r,
        _ => 0m
    };
}

internal readonly record struct RecycleEntry(
    List<IReadOnlyList<uint>> FixedRewards,
    uint RecycleBase,
    decimal RecycleRatio);

internal readonly record struct SupplyTemplate(uint Genre, uint Detail, uint Particular, uint Level, uint ProvideExp);

public sealed class ItemRecycleParam
{
    [JsonPropertyName("tbItems")]
    public List<int> TbItems { get; set; } = [];
}
