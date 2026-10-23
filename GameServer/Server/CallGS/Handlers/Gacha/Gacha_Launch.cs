using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.Enums.Item;
using MikuSB.GameServer.Game.Gacha;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using Newtonsoft.Json.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Gacha;

[CallGSApi("Gacha_Launch")]
public class Gacha_Launch : CallGSHandler<GachaLaunchParam>
{
    private const uint GachaGid = AttrIds.Gacha.Gid;
    private const uint GachaSgid = AttrIds.Gacha.StringGid;
    private const uint SidTotalTime = AttrIds.Gacha.TotalTimeSid;
    private const uint SidDailyTotalTime = AttrIds.Gacha.DailyTotalTimeSid;
    private const uint Interval = 10;
    private const uint SidTimeInheritStart = AttrIds.Gacha.TimeInheritStartSid;
    private const uint SidTimeNotInheritStart = AttrIds.Gacha.TimeNotInheritStartSid;
    private const uint SidAddTimeItem = AttrIds.Gacha.AddTimeItemSid;
    private const uint SidAddTimeProb = AttrIds.Gacha.AddTimeProbSid;
    private const uint SidAddProtectType = AttrIds.Gacha.AddProtectTypeSid;
    private const uint SidAddTotalTime = AttrIds.Gacha.AddTotalTimeSid;
    private const int UpSelectIndex = 0;
    private const int UpSelectGetFlagIndex = 1;
    private static readonly Random Rng = new();

    // A static logger keeps the module name readable: GetByClassName() inside an async method
    // resolves to the compiler-generated state machine.
    private static readonly Logger Log = new(nameof(Gacha_Launch));

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, GachaLaunchParam req)
    {
        var player = context.Connection.Player!;

        if (req == null || req.NId == 0 || req.NTime is not (1 or 10))
        {
            return CallGSResult.Error("error.BadParam");
        }

        if (!GameData.GachaData.TryGetValue((uint)req.NId, out var gachaCfg))
        {
            return CallGSResult.Error("error.BadParam");
        }

        var poolNames = (gachaCfg.Pool ?? [])
            .Where(GameData.GachaPoolData.ContainsKey)
            .ToList();
        var allPoolItems = poolNames
            .SelectMany(p => GameData.GachaPoolData[p])
            .ToList();

        if (allPoolItems.Count == 0 || !GameData.GachaProbabilityData.TryGetValue(gachaCfg.Probability, out var baseProbCfg))
        {
            return CallGSResult.Error("error.BadParam");
        }

        var pityState = LoadPityState(player, gachaCfg);
        var upSelectState = LoadUpSelectState(player, gachaCfg);
        var config = BuildRuntimeConfig(gachaCfg, poolNames);
        var awards = new List<List<uint>>();
        var tbNew = new List<int>();
        var tbTrigger = new List<bool>();
        var syncItems = new List<Item>();
        var sync = new NtfSyncPlayer();

        // Gacha.lua:443 already refused the request when the client's own CheckCost could not pay,
        // so a banner without a usable cost row is a data error, not a player problem: the price is
        // resolved before the roll so such a pull is rejected without touching any player state.
        var price = GachaPrice.Resolve(gachaCfg, (uint)req.NTime);
        if (price == null)
        {
            Log.Warn($"Gacha {req.NId} has no usable cast cost row for x{req.NTime}.");
            return CallGSResult.Error("error.BadParam");
        }

        var charged = false;

        for (int i = 0; i < req.NTime; i++)
        {
            var forceTopUp = config.UpTarget != null && pityState.ProtectType == 2;
            var hitHardPity = config.ProtectThreshold > 0 && pityState.ItemCount + 1 >= config.ProtectThreshold;
            var useTenGuarantee = gachaCfg.ProbabilityTen != 0
                && pityState.TenCount + 1 >= 10
                && !HasGuaranteedTenRarity(config, awards);

            GachaProbabilityExcel probCfg = baseProbCfg;
            if (useTenGuarantee && GameData.GachaProbabilityData.TryGetValue(gachaCfg.ProbabilityTen, out var tenProbCfg))
                probCfg = tenProbCfg;

            GachaPoolItem? item;
            bool trigger = false;

            if (hitHardPity)
            {
                item = PickGuaranteedItem(gachaCfg, config, preferUp: forceTopUp);
                trigger = item != null;
            }
            else
            {
                var rarity = RollRarity(probCfg);
                item = forceTopUp && config.UpTarget != null && rarity >= config.TopRarity
                    ? PickGuaranteedItem(gachaCfg, config, preferUp: true)
                    : PickItem(allPoolItems, rarity);
                trigger = forceTopUp && item != null && config.UpTarget != null && item.Rarity == config.UpTarget.Rarity;
            }

            if (item != null && upSelectState.SelectedItem != null && item.Rarity >= config.TopRarity)
            {
                bool forceSelected = upSelectState.GuaranteedNext;
                bool shouldSelect = forceSelected || Rng.Next(100) < 50;
                if (shouldSelect)
                {
                    var selectedItem = FindExactItem(allPoolItems, upSelectState.SelectedItem);
                    if (selectedItem != null && selectedItem.Rarity >= config.TopRarity)
                        item = selectedItem;
                }
            }

            if (item == null || item.GDPL.Count < 4)
            {
                tbTrigger.Add(false);
                continue;
            }

            // The pull is charged with the first award it is about to grant: nothing above this
            // point has touched the player state, so a pool that grants nothing (the awards.Count ==
            // 0 path below) stays free instead of taking the tickets for nothing.
            if (!charged)
            {
                if (price.Charge(player, sync) is { } priceError)
                    return CallGSResult.Error(priceError);

                charged = true;
            }

            var g = item.GDPL[0];
            var d = item.GDPL[1];
            var p = item.GDPL[2];
            var l = item.GDPL[3];

            awards.Add([g, d, p, l]);
            tbTrigger.Add(trigger);

            UpdatePityState(pityState, config, item);
            UpdateUpSelectState(upSelectState, config, item);

            var itemType = (ItemTypeEnum)g;
            switch (itemType)
            {
                case ItemTypeEnum.TYPE_CARD:
                {
                    var alreadyOwned = player.CharacterManager.GetCharacterGDPL(itemType, (int)d, (int)p) != null;
                    if (!alreadyOwned)
                    {
                        var charInfo = await player.CharacterManager.AddCharacter(itemType, d, p, sendPacket: false);
                        if (charInfo != null)
                        {
                            syncItems.Add(charInfo.ToProto());
                            tbNew.Add(awards.Count);
                        }
                    }
                    break;
                }
                case ItemTypeEnum.TYPE_WEAPON:
                {
                    var weaponInfo = await player.InventoryManager.AddWeaponItem(itemType, d, p, l, sendPacket: false);
                    if (weaponInfo != null) syncItems.Add(weaponInfo.ToProto());
                    break;
                }
                case ItemTypeEnum.TYPE_SUPPORT:
                {
                    var cardInfo = await player.InventoryManager.AddSupportCardItem(d, p, l, sendPacket: false);
                    if (cardInfo != null) syncItems.Add(cardInfo.ToProto());
                    break;
                }
            }
        }

        if (awards.Count == 0)
        {
            // Nothing was granted, and nothing was charged either (see the charge inside the loop).
            return CallGSResult.Error("error.BadParam");
        }

        SavePityState(player, gachaCfg, pityState, awards.Count, sync);
        SaveUpSelectState(player, gachaCfg, upSelectState, sync);
        DatabaseHelper.SaveDatabaseType(player.Data);
        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
        DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);

        Log.Info(
            $"Gacha {req.NId} pull x{req.NTime} launched by uid={player.Uid} " +
            $"(paid {price.Describe()}, granted {awards.Count}).");

        sync.Items.AddRange(syncItems);

        var rsp = BuildResponse(req.NId, awards, tbNew, tbTrigger);
        return CallGSResult.Ok(rsp, sync);
    }

    private static bool HasGuaranteedTenRarity(GachaRuntimeConfig config, List<List<uint>> awards)
    {
        if (awards.Count == 0)
            return false;

        int windowStart = awards.Count >= 9 ? awards.Count - 9 : 0;
        for (int i = windowStart; i < awards.Count; i++)
        {
            var award = awards[i];
            if (award.Count < 4)
                continue;

            var template = FindPoolItemByGdpl(config.AllPoolItems, award);
            if (template != null && template.Rarity >= config.TenGuaranteeRarity)
                return true;
        }

        return false;
    }

    private static GachaPoolItem? FindPoolItemByGdpl(List<GachaPoolItem> pool, List<uint> gdpl) =>
        pool.FirstOrDefault(x =>
            x.GDPL.Count >= 4 &&
            x.GDPL[0] == gdpl[0] &&
            x.GDPL[1] == gdpl[1] &&
            x.GDPL[2] == gdpl[2] &&
            x.GDPL[3] == gdpl[3]);

    private static GachaPoolItem? FindExactItem(List<GachaPoolItem> pool, uint[] gdpl) =>
        pool.FirstOrDefault(x =>
            x.GDPL.Count >= 4 &&
            x.GDPL[0] == gdpl[0] &&
            x.GDPL[1] == gdpl[1] &&
            x.GDPL[2] == gdpl[2] &&
            x.GDPL[3] == gdpl[3]);

    private static GachaRuntimeConfig BuildRuntimeConfig(GachaExcel gachaCfg, List<string> poolNames)
    {
        var allPoolItems = poolNames.SelectMany(name => GameData.GachaPoolData[name]).ToList();
        var protectPools = ParsePoolRarities(gachaCfg.ProtectNum);
        var upTarget = ParseSinglePoolRarity(gachaCfg.UpNum);
        var topRarity = new[] { upTarget?.Rarity ?? 0 }.Concat(protectPools.Select(x => x.Rarity)).Max();
        if (topRarity <= 0)
            topRarity = allPoolItems.Count == 0 ? 0 : allPoolItems.Max(x => x.Rarity);

        return new GachaRuntimeConfig
        {
            AllPoolItems = allPoolItems,
            ProtectThreshold = ParseThreshold(gachaCfg.ProtectNum),
            ProtectPools = protectPools,
            UpTarget = upTarget,
            TopRarity = topRarity,
            TenGuaranteeRarity = 4
        };
    }

    private static int ParseThreshold(JToken? token)
    {
        if (token is not JArray arr || arr.Count == 0)
            return 0;

        return arr[0]?.Value<int>() ?? 0;
    }

    private static List<PoolRarityRef> ParsePoolRarities(JToken? token)
    {
        var result = new List<PoolRarityRef>();
        if (token is not JArray arr || arr.Count < 2 || arr[1] is not JArray entries)
            return result;

        foreach (var entry in entries.OfType<JArray>())
        {
            if (entry.Count < 2)
                continue;

            var poolName = entry[0]?.Value<string>();
            var rarity = entry[1]?.Value<int>() ?? 0;
            if (string.IsNullOrWhiteSpace(poolName) || rarity <= 0)
                continue;

            result.Add(new PoolRarityRef(poolName, rarity));
        }

        return result;
    }

    private static PoolRarityRef? ParseSinglePoolRarity(JToken? token)
    {
        if (token is not JArray arr || arr.Count < 2 || arr[1] is not JArray entry || entry.Count < 2)
            return null;

        var poolName = entry[0]?.Value<string>();
        var rarity = entry[1]?.Value<int>() ?? 0;
        return string.IsNullOrWhiteSpace(poolName) || rarity <= 0 ? null : new PoolRarityRef(poolName, rarity);
    }

    private static GachaPityState LoadPityState(PlayerInstance player, GachaExcel gachaCfg)
    {
        var baseSid = GetBaseSid(gachaCfg);
        return new GachaPityState
        {
            ItemCount = (int)GetAttr(player, GachaGid, baseSid + SidAddTimeItem),
            TenCount = (int)GetAttr(player, GachaGid, baseSid + SidAddTimeProb),
            ProtectType = Math.Max(1, (int)GetAttr(player, GachaGid, baseSid + SidAddProtectType)),
            PoolTotalTime = (int)GetAttr(player, GachaGid, baseSid + SidAddTotalTime)
        };
    }

    private static void SavePityState(PlayerInstance player, GachaExcel gachaCfg, GachaPityState state, int drawCount, NtfSyncPlayer sync)
    {
        var baseSid = GetBaseSid(gachaCfg);

        SetAttr(player, sync, GachaGid, SidTotalTime, GetAttr(player, GachaGid, SidTotalTime) + (uint)drawCount);
        SetAttr(player, sync, GachaGid, SidDailyTotalTime, GetAttr(player, GachaGid, SidDailyTotalTime) + (uint)drawCount);
        SetAttr(player, sync, GachaGid, baseSid + SidAddTimeItem, (uint)state.ItemCount);
        SetAttr(player, sync, GachaGid, baseSid + SidAddTimeProb, (uint)state.TenCount);
        SetAttr(player, sync, GachaGid, baseSid + SidAddProtectType, (uint)Math.Max(1, state.ProtectType));
        SetAttr(player, sync, GachaGid, baseSid + SidAddTotalTime, (uint)(state.PoolTotalTime + drawCount));
    }

    private static GachaUpSelectState LoadUpSelectState(PlayerInstance player, GachaExcel gachaCfg)
    {
        if (gachaCfg.UpSelect != 1)
            return new GachaUpSelectState();

        var raw = player.Attributes.GetStringValue(GachaSgid, gachaCfg.ID);
        if (string.IsNullOrWhiteSpace(raw))
            return new GachaUpSelectState();

        try
        {
            var state = JArray.Parse(raw);
            uint[]? selected = null;
            if (state.Count > UpSelectIndex && state[UpSelectIndex] is JArray selectedArray && selectedArray.Count >= 4)
            {
                selected =
                [
                    selectedArray[0]?.Value<uint>() ?? 0,
                    selectedArray[1]?.Value<uint>() ?? 0,
                    selectedArray[2]?.Value<uint>() ?? 0,
                    selectedArray[3]?.Value<uint>() ?? 0
                ];
            }

            return new GachaUpSelectState
            {
                SelectedItem = selected,
                GuaranteedNext = state.Count > UpSelectGetFlagIndex && (state[UpSelectGetFlagIndex]?.Value<int>() ?? 0) == 1,
                RawState = state
            };
        }
        catch
        {
            return new GachaUpSelectState();
        }
    }

    private static void SaveUpSelectState(PlayerInstance player, GachaExcel gachaCfg, GachaUpSelectState state, NtfSyncPlayer sync)
    {
        if (gachaCfg.UpSelect != 1 || state.RawState == null)
            return;

        EnsureArraySize(state.RawState, 2);
        state.RawState[UpSelectGetFlagIndex] = state.GuaranteedNext ? 1 : 0;

        var value = state.RawState.ToString(Newtonsoft.Json.Formatting.None);
        var attr = player.Attributes.SetString(GachaSgid, gachaCfg.ID, value);
        player.Attributes.SyncTo(sync, attr);
    }

    private static uint GetBaseSid(GachaExcel gachaCfg)
    {
        if (gachaCfg.ProtectTag.HasValue)
            return SidTimeInheritStart + (gachaCfg.ProtectTag.Value * Interval);

        return SidTimeNotInheritStart + (gachaCfg.ID * Interval);
    }

    private static uint GetAttr(PlayerInstance player, uint gid, uint sid) =>
        player.Attributes.GetValue(gid, sid);

    private static void SetAttr(PlayerInstance player, NtfSyncPlayer sync, uint gid, uint sid, uint value)
    {
        var attr = player.Attributes.Set(gid, sid, value);
        player.Attributes.SyncTo(sync, attr);
    }

    private static void UpdatePityState(GachaPityState state, GachaRuntimeConfig config, GachaPoolItem item)
    {
        if (item.Rarity >= config.TenGuaranteeRarity)
            state.TenCount = 0;
        else
            state.TenCount++;

        if (item.Rarity >= config.TopRarity)
        {
            state.ItemCount = 0;
            if (config.UpTarget != null)
                state.ProtectType = IsFromPool(item, config.UpTarget) ? 1 : 2;
            else
                state.ProtectType = 1;
        }
        else
        {
            state.ItemCount++;
        }
    }

    private static void UpdateUpSelectState(GachaUpSelectState state, GachaRuntimeConfig config, GachaPoolItem item)
    {
        if (state.SelectedItem == null || item.Rarity < config.TopRarity)
            return;

        state.GuaranteedNext = !MatchesGdpl(item, state.SelectedItem);
    }

    private static bool MatchesGdpl(GachaPoolItem item, uint[] gdpl) =>
        item.GDPL.Count >= 4 &&
        item.GDPL[0] == gdpl[0] &&
        item.GDPL[1] == gdpl[1] &&
        item.GDPL[2] == gdpl[2] &&
        item.GDPL[3] == gdpl[3];

    private static void EnsureArraySize(JArray state, int size)
    {
        while (state.Count < size)
            state.Add(JValue.CreateNull());
    }

    private static bool IsFromPool(GachaPoolItem item, PoolRarityRef target) =>
        item.Rarity == target.Rarity &&
        GameData.GachaPoolData.TryGetValue(target.PoolName, out var pool) &&
        pool.Any(x => x.ID == item.ID);

    private static int RollRarity(GachaProbabilityExcel prob)
    {
        var weights = prob.Weights;
        int total = weights.Sum();
        int roll = Rng.Next(total);
        int cumulative = 0;
        for (int i = 0; i < weights.Length; i++)
        {
            cumulative += weights[i];
            if (roll < cumulative)
                return i + 1;
        }

        return 3;
    }

    private static GachaPoolItem? PickGuaranteedItem(GachaExcel gachaCfg, GachaRuntimeConfig config, bool preferUp)
    {
        if (preferUp && config.UpTarget != null)
        {
            var upItem = PickItemFromPool(config.UpTarget.PoolName, config.UpTarget.Rarity);
            if (upItem != null)
                return upItem;
        }

        foreach (var poolRef in config.ProtectPools)
        {
            var item = PickItemFromPool(poolRef.PoolName, poolRef.Rarity);
            if (item != null)
                return item;
        }

        return PickItem(config.AllPoolItems, config.TopRarity);
    }

    private static GachaPoolItem? PickItemFromPool(string poolName, int rarity)
    {
        if (!GameData.GachaPoolData.TryGetValue(poolName, out var pool))
            return null;

        return PickItem(pool, rarity);
    }

    private static GachaPoolItem? PickItem(List<GachaPoolItem> pool, int rarity)
    {
        var candidates = pool.Where(x => x.Rarity == rarity).ToList();
        if (candidates.Count == 0)
        {
            candidates = pool.Where(x => x.Rarity == rarity - 1).ToList();
            if (candidates.Count == 0)
                return pool.FirstOrDefault();
        }

        int total = candidates.Sum(x => x.Weight);
        if (total <= 0)
            return candidates[Rng.Next(candidates.Count)];

        int roll = Rng.Next(total);
        int cumulative = 0;
        foreach (var item in candidates)
        {
            cumulative += item.Weight;
            if (roll < cumulative)
                return item;
        }

        return candidates.Last();
    }

    private static string BuildResponse(int nId, List<List<uint>> awards, List<int> tbNew, List<bool> tbTrigger)
    {
        var sb = new StringBuilder();
        sb.Append("{\"nId\":");
        sb.Append(nId);
        sb.Append(",\"tbAwards\":[");
        for (int i = 0; i < awards.Count; i++)
        {
            if (i > 0)
                sb.Append(',');

            sb.Append('[');
            sb.Append(string.Join(',', awards[i]));
            sb.Append(']');
        }

        sb.Append("],\"nBoxCount\":0,\"tbNew\":[");
        sb.Append(string.Join(',', tbNew));
        sb.Append("],\"tbTrigger\":[");
        sb.Append(string.Join(',', tbTrigger.Select(b => b ? "true" : "false")));
        sb.Append("]}");
        return sb.ToString();
    }
}

public sealed class GachaLaunchParam
{
    [JsonPropertyName("nId")]
    public int NId { get; set; }

    [JsonPropertyName("bPickUp")]
    public bool BPickUp { get; set; }

    [JsonPropertyName("nTime")]
    public int NTime { get; set; }
}

internal sealed class GachaPityState
{
    public int ItemCount { get; set; }
    public int TenCount { get; set; }
    public int ProtectType { get; set; } = 1;
    public int PoolTotalTime { get; set; }
}

internal sealed class GachaRuntimeConfig
{
    public List<GachaPoolItem> AllPoolItems { get; set; } = [];
    public int ProtectThreshold { get; set; }
    public List<PoolRarityRef> ProtectPools { get; set; } = [];
    public PoolRarityRef? UpTarget { get; set; }
    public int TopRarity { get; set; }
    public int TenGuaranteeRarity { get; set; }
}

internal sealed class GachaUpSelectState
{
    public uint[]? SelectedItem { get; set; }
    public bool GuaranteedNext { get; set; }
    public JArray? RawState { get; set; } = new();
}

internal sealed record PoolRarityRef(string PoolName, int Rarity);
