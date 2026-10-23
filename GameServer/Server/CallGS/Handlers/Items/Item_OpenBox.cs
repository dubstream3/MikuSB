using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.Enums.Item;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.Packet.Send.Misc;
using MikuSB.Proto;
using MikuSB.Util;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Items;

// Open box request. The client sends it from three places, all with the bag's unique item id as
// "Id" (never a GDPL, so the server has to resolve the template from the inventory row):
//   - umg_common_itemselect.lua:73/89/111 -> a Select box, payload is
//     { Id, Count, tbSelect: [ { nGroup, tbGDPLN: [G,D,P,L,N] } ] }
//   - umg_common_purchase_energy.lua:139/258 -> a plain box, payload is { Id, Count }
//   - umg_bag.lua:106 -> the monthly card box, payload is { Id, Count = 1 }
// Request: { Id, Count, tbSelect?: [ { nGroup, tbGDPLN } ] }
// Response: { nId, tbAward: [ [G,D,P,L,N], ... ] } -- the client handler Item.lua:1131-1139 reads
// tbParam.tbAward and hands it to Item.Gain (Item.lua:638-644), which feeds the GainItem UI; it
// reads nothing else from the response.
[CallGSApi("Item_OpenBox")]
public class Item_OpenBox : CallGSHandler<ItemOpenBoxParam>
{
    private static readonly Logger Log = new(nameof(Item_OpenBox));

    // One use of a stack should never open an unbounded number of boxes.
    private const uint MaxOpenCount = 99;

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, ItemOpenBoxParam request)
    {
        var player = context.Connection.Player!;

        if (request.Id == 0 || request.Count > MaxOpenCount)
            return CallGSResult.Error("error.BadParam");

        var inventory = player.InventoryManager.InventoryData;
        if (!inventory.Items.TryGetValue(request.Id, out var boxItem) || boxItem.ItemCount == 0)
            return CallGSResult.Error("tip.NotEnoughItem");

        if (!TryGetBoxTemplate(boxItem.TemplateId, out var boxIndex, out var luaType))
        {
            Log.Warn($"Item {request.Id} of uid={player.Uid} is not a box (template={boxItem.TemplateId}).");
            return CallGSResult.Error("error.BadParam");
        }

        // Count == 0 is what the client sends when its numeric stepper sits on 1; treat it as one box.
        var openCount = Math.Min(request.Count == 0 ? 1u : request.Count, boxItem.ItemCount);
        if (openCount == 0)
            return CallGSResult.Error("tip.NotEnoughItem");

        if (!GameData.BoxData.TryGetValue(boxIndex, out var rows))
        {
            Log.Warn($"Box {boxIndex} ({luaType}) has no rows in item/box.json.");
            return CallGSResult.Error("error.BadParam");
        }

        var selectionByGroup = BuildSelection(request.TbSelect);
        var awardTotals = new Dictionary<AwardKey, ulong>();
        var opened = 0u;

        for (var i = 0u; i < openCount; i++)
        {
            var awards = SelectAwards(rows, boxIndex, selectionByGroup);
            if (awards.Count == 0)
            {
                Log.Warn($"Box {boxIndex} produced no award rows; stopping after {opened} box(es).");
                break;
            }

            foreach (var row in awards)
                AddAward(awardTotals, row);

            opened++;
        }

        if (opened == 0)
            return CallGSResult.Error("error.BadParam");

        // Even a fallback award (a Select box that arrived without tbSelect) consumes the box, so the
        // charge and the grant always happen together.
        boxItem.ItemCount -= opened;

        var sync = new NtfSyncPlayer();

        // Always echo the row: the client updates the bag from the sync
        // (umg_common_itemselect.lua:247-300 waits for Event.GetBoxItem and then calls
        // BagUI:UpdatePage), and a Count of 0 is how a stack is removed.
        sync.Items.Add(ItemTemplateInfo.ToSyncProto(boxItem));

        var rewardRows = new List<IReadOnlyList<uint>>();
        foreach (var (key, count) in awardTotals)
        {
            uint[] row = [key.Genre, key.Detail, key.Particular, key.Level, (uint)count];
            rewardRows.Add(row);
        }

        var granted = await player.RewardManager.GrantConfiguredRewardsAsync(rewardRows, sync);

        DatabaseHelper.SaveDatabaseType(player.Data);
        DatabaseHelper.SaveDatabaseType(inventory);
        DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);

        var payload = new JsonObject
        {
            ["nId"] = (int)boxItem.UniqueId,
            ["tbAward"] = granted
        };

        Log.Info(
            $"Item_OpenBox uid={player.Uid} opened box {boxIndex} x{opened} " +
            $"(item={boxItem.UniqueId}, left {boxItem.ItemCount}, granted {granted.Count} row(s)).");

        return CallGSResult.Ok(payload.ToJsonString(), sync);
    }

    /// <summary>
    /// Resolves the box index of an "itembox" template. The client tests
    /// <c>LuaType == "itembox" and Param1 ~= nil</c> (Item.lua:861-869 Item.IsItemBox) and then reads
    /// <c>Item.tbBox[Param1]</c> (Item.lua:833-845 / umg_common_itemselect.lua:152). Box templates live
    /// in both item/templates/suplies.json (e.g. supplies.month_card, Param1=99) and
    /// item/templates/others.json (e.g. supplies.giftbox1, Param1=1), so both tables are checked.
    /// </summary>
    private static bool TryGetBoxTemplate(ulong templateId, out uint boxIndex, out string luaType)
    {
        boxIndex = 0;
        luaType = "";

        if (!ItemTemplateInfo.TryResolve(templateId, out var template))
            return false;

        luaType = template.LuaType;
        boxIndex = template.Param1;
        return template.IsBox;
    }

    /// <summary>Keeps the first pick of every group; the client only ever sends one.</summary>
    private static Dictionary<uint, uint[]> BuildSelection(List<ItemOpenBoxSelect>? selections)
    {
        var result = new Dictionary<uint, uint[]>();
        foreach (var selection in selections ?? [])
        {
            var row = BoxExcel.ParseGdpln(selection.TbGDPLN);
            if (row == null)
                continue;

            result.TryAdd(selection.NGroup == 0 ? row[0] : selection.NGroup, row);
        }

        return result;
    }

    /// <summary>Decides which rows one opened box grants.</summary>
    private static List<uint[]> SelectAwards(
        List<BoxExcel> rows,
        uint boxIndex,
        Dictionary<uint, uint[]> selectionByGroup)
    {
        var awards = new List<uint[]>();

        // Select: exactly one row per group. The client validates the pick against the same table
        // (umg_common_itemselect.lua:155-202), so a row that is not part of the box is refused and
        // replaced by the box's own default instead of being granted. A Select box that arrives
        // without tbSelect (the counting UI at umg_common_purchase_energy.lua:254-258) still opens,
        // using the first row of every group.
        foreach (var group in rows.Where(x => x.IsSelect).GroupBy(x => x.Group))
        {
            var candidates = group.ToList();
            var chosen = candidates[0];
            if (selectionByGroup.TryGetValue(group.Key, out var wanted))
            {
                var picked = candidates.FirstOrDefault(x => SameRow(x.Reward, wanted));
                if (picked != null)
                {
                    chosen = picked;
                }
                else
                {
                    Log.Warn(
                        $"Box {boxIndex} group {group.Key}: client pick {Describe(wanted)} is not in " +
                        $"the table; granting the default {Describe(chosen.Reward)} instead.");
                }
            }

            awards.Add(chosen.Reward.ToArray());
        }

        foreach (var row in rows.Where(x => x.IsFix))
            awards.Add(row.Reward.ToArray());

        // Random: one weighted pick per group. Groups are separate weight spaces (index 101 carries
        // two groups of five rows, 2000 each), so the roll happens inside every group.
        foreach (var group in rows.Where(x => x.IsRandom).GroupBy(x => x.Group))
        {
            var candidates = group.ToList();
            var total = candidates.Aggregate(0UL, (sum, x) => sum + x.Weight);
            if (total == 0)
            {
                awards.Add(candidates[0].Reward.ToArray());
                continue;
            }

            var roll = (ulong)Random.Shared.NextInt64(0, (long)total);
            var cumulative = 0UL;
            var selected = candidates[^1];
            foreach (var candidate in candidates)
            {
                cumulative += candidate.Weight;
                if (roll < cumulative)
                {
                    selected = candidate;
                    break;
                }
            }

            awards.Add(selected.Reward.ToArray());
        }

        // Prob: every row rolls on its own; Weight is in ten-thousandths (coupon indexes 777-779 use
        // 1000/1500/2000, the two full indexes use 2000 per row over 10 rows == 20000 == 2 draws).
        foreach (var row in rows.Where(x => x.IsProb))
        {
            if (row.Weight == 0 || Random.Shared.Next(10000) < row.Weight)
                awards.Add(row.Reward.ToArray());
        }

        return awards;
    }

    private static void AddAward(Dictionary<AwardKey, ulong> totals, IReadOnlyList<uint> row)
    {
        var key = new AwardKey(row[0], row[1], row[2], row[3]);
        totals[key] = totals.GetValueOrDefault(key) + row[4];
    }

    private static bool SameRow(IReadOnlyList<uint> left, IReadOnlyList<uint> right) =>
        left.Count >= 4 && right.Count >= 4 &&
        left[0] == right[0] && left[1] == right[1] && left[2] == right[2] && left[3] == right[3];

    private static string Describe(IReadOnlyList<uint> row) =>
        $"{row[0]}-{row[1]}-{row[2]}-{row[3]}";

    private readonly record struct AwardKey(uint Genre, uint Detail, uint Particular, uint Level)
    {
        public override int GetHashCode() =>
            unchecked((int)(Genre * 31_337u + Detail * 7_919u + Particular * 131u + Level));
    }
}

public sealed class ItemOpenBoxParam
{
    /// <summary>Unique id of the bag row to use (the client's <c>pItem:Id()</c>).</summary>
    [JsonPropertyName("Id")] public uint Id { get; set; }

    /// <summary>How many boxes of the stack to open; 0 from the client means one.</summary>
    [JsonPropertyName("Count")] public uint Count { get; set; }

    /// <summary>Only sent for Select boxes.</summary>
    [JsonPropertyName("tbSelect")] public List<ItemOpenBoxSelect>? TbSelect { get; set; }
}

public sealed class ItemOpenBoxSelect
{
    [JsonPropertyName("nGroup")] public uint NGroup { get; set; }

    /// <summary>The picked award row; a real array from the client, but parsed leniently anyway.</summary>
    [JsonPropertyName("tbGDPLN")] public Newtonsoft.Json.Linq.JToken? TbGDPLN { get; set; }
}
