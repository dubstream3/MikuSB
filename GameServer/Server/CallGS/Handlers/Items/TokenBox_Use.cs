using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Items;

// Memory fragment (记忆嵌片) request. The client uses this api for a "token_box" template exactly the
// way it uses a stamina potion: the shared exchange widget stacks the boxes by expiry and hands the
// server the bag rows to open, never a GDPL and never an amount
//   - umg_common_purchase_exchange.lua:161 tests LuaType == "token_box" right next to "vigor_box"
//   - :162 tbList = Item.GetExpirationItemID({ G, D, P, L }, nCount, nItemID) (Item.lua:680-718)
//     returns the soonest-expiring bag row ids first and at most one id per row, so one entry of
//     tbList is one box
//   - :171 me:CallGS('TokenBox_Use', json.encode({ tbList = tbList }))
// The contents come from the same item/box.json table every other box uses, indexed by the template's
// Param1: Item.lua:883-889 Item.IsCashBox only checks LuaType plus a non-nil Param1, while
// Item.tbBox[Param1] is built by Item.LoadBoxConf (Item.lua:1376-1395). The two shipped templates are
// 4/6/1/1 and 4/6/1/2 (others.json, Param1=7), whose four "Fix" rows hand out the weapon-break
// materials 5/5/1/1-4.
// Request: { tbList: [ rowId, ... ] }
// Response: { tbAward: [ [G,D,P,L,N], ... ] } and nothing else -- the client handler at
// Item.lua:1154-1164 triggers Event.GetBoxItem / Event.ItemExpiration and feeds tbParam.tbAward to
// Item.Gain; the consumed stacks and the granted rows also travel in the packet's ExtraSync, which is
// what the widget's Event.GetBoxItem callback waits for before it refreshes the bag
// (umg_common_purchase_exchange.lua:281-346).
[CallGSApi("TokenBox_Use")]
public class TokenBox_Use : CallGSHandler<TokenBoxUseParam>
{
    private static readonly Logger Log = new(nameof(TokenBox_Use));

    // One use should never drain an unbounded number of stacks.
    private const int MaxUseCount = 99;

    /// <summary>Client Lua type of a memory fragment box (Item.lua:883-889 Item.IsCashBox).</summary>
    private const string TokenBoxLuaType = "token_box";

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, TokenBoxUseParam request)
    {
        var player = context.Connection.Player!;

        if (request.TbList == null || request.TbList.Count == 0 || request.TbList.Count > MaxUseCount)
            return CallGSResult.Error("error.BadParam");

        var inventory = player.InventoryManager.InventoryData;

        // Resolve and validate every row before touching anything, so a bad row cannot consume the
        // boxes that were listed before it: the whole request either lands or changes nothing.
        var uses = new List<BoxUse>();
        var listed = new Dictionary<uint, uint>();
        foreach (var rowId in request.TbList)
        {
            // Items is this player's own inventory, so a row found here is one the player really owns;
            // a stale or forged id is simply absent from the dictionary. Counting how often tbList
            // names a row also keeps the later decrement from wrapping a uint on a forged packet.
            listed[rowId] = listed.GetValueOrDefault(rowId) + 1;
            if (!inventory.Items.TryGetValue(rowId, out var item) || item.ItemCount < listed[rowId])
            {
                Log.Warn(
                    $"TokenBox_Use uid={player.Uid}: row {rowId} is missing, empty, or listed more " +
                    "often than it holds; nothing consumed.");
                return CallGSResult.Error("error.BadParam");
            }

            // ItemTemplateInfo exposes IsBox / IsVigorBox only; the token_box test is spelled out here
            // because the client's own IsCashBox is just LuaType plus a non-nil Param1.
            if (!ItemTemplateInfo.TryResolve(item.TemplateId, out var template) ||
                template.LuaType != TokenBoxLuaType ||
                template.Param1 == 0)
            {
                Log.Warn(
                    $"TokenBox_Use uid={player.Uid}: row {rowId} (template={item.TemplateId}) is not a " +
                    "token_box; nothing consumed.");
                return CallGSResult.Error("error.BadParam");
            }

            // Param1 is the Item.tbBox index. A template pointing at an index the shipped table never
            // filled in has nothing to grant, so it is refused instead of being eaten -- the same
            // policy Item_OpenBox applies to the GDPLN-less rows of box.json.
            if (!GameData.BoxData.TryGetValue(template.Param1, out var rows))
            {
                Log.Warn(
                    $"TokenBox_Use uid={player.Uid}: token_box {item.TemplateId} points at box " +
                    $"{template.Param1}, which has no rows in item/box.json; nothing consumed.");
                return CallGSResult.Error("error.BadParam");
            }

            uses.Add(new BoxUse(item, template.Param1, rows));
        }

        // One box per tbList entry, resolved and summed before anything is consumed.
        var awardTotals = new Dictionary<AwardKey, ulong>();
        foreach (var use in uses)
        {
            var awards = SelectAwards(use.Rows);
            if (awards.Count == 0)
            {
                // Every row of the index rolled away; refuse like Item_OpenBox does rather than
                // consuming a box for nothing.
                Log.Warn(
                    $"TokenBox_Use uid={player.Uid}: box {use.BoxIndex} produced no award rows; " +
                    "nothing consumed.");
                return CallGSResult.Error("error.BadParam");
            }

            foreach (var row in awards)
                AddAward(awardTotals, row);
        }

        var sync = new NtfSyncPlayer();

        // One tbList entry is one box, so every entry drops its stack by one; a row named twice is
        // decremented once per entry and echoed to the client only once.
        var touched = new List<BaseGameItemInfo>();
        foreach (var use in uses)
        {
            use.Item.ItemCount -= 1;
            if (!touched.Contains(use.Item))
                touched.Add(use.Item);
        }

        // Always echo the rows that changed: the client refreshes the bag from the sync and waits for
        // Event.GetBoxItem before it calls BagUI:UpdatePage, and a count of 0 is how a spent stack is
        // removed (Item_Recycle.cs:79-89 uses the same idiom).
        foreach (var item in touched)
            sync.Items.Add(ItemTemplateInfo.ToSyncProto(item));

        var rewardRows = new List<IReadOnlyList<uint>>();
        foreach (var (key, count) in awardTotals)
        {
            uint[] row = [key.Genre, key.Detail, key.Particular, key.Level, (uint)count];
            rewardRows.Add(row);
        }

        // The same grant path Item_OpenBox uses; it returns exactly the [[G,D,P,L,N], ...] rows the
        // client's Item.Gain reads, and handing it the sync puts the new materials into the same
        // packet as the consumed stacks.
        var granted = await player.RewardManager.GrantConfiguredRewardsAsync(rewardRows, sync);

        if (granted.Count < rewardRows.Count)
        {
            Log.Warn(
                $"TokenBox_Use uid={player.Uid}: {rewardRows.Count - granted.Count} of " +
                $"{rewardRows.Count} award row(s) could not be granted; see the Reward log.");
        }

        DatabaseHelper.SaveDatabaseType(player.Data);
        DatabaseHelper.SaveDatabaseType(inventory);
        // The box table is data driven: a token_box index could hold a card row, and granting one
        // writes CharacterData, so it is persisted on every use just like Item_OpenBox does.
        DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);

        var payload = new JsonObject
        {
            ["tbAward"] = granted
        };

        Log.Info(
            $"TokenBox_Use uid={player.Uid} used {uses.Count} token box(es) from {touched.Count} " +
            $"stack(s) (box {uses[0].BoxIndex}) and granted {granted.Count} row(s): {Describe(granted)}.");

        return CallGSResult.Ok(payload.ToJsonString(), sync);
    }

    /// <summary>
    /// Decides which award rows one opened token box grants. There is no selection UI on this path:
    /// the client only ever sends the row ids (umg_common_purchase_exchange.lua:161-172 carries no
    /// tbSelect), so a "Select" group falls back to its first row exactly as Item_OpenBox treats a
    /// Select box that arrived without tbSelect. The other three kinds are the same as that
    /// handler's: "Fix" grants every row, "Random" is one weighted draw per group and "Prob" rolls
    /// every row on its own (weights in ten-thousandths).
    /// </summary>
    private static List<uint[]> SelectAwards(List<BoxExcel> rows)
    {
        var awards = new List<uint[]>();

        foreach (var group in rows.Where(x => x.IsSelect).GroupBy(x => x.Group))
            awards.Add(group.First().Reward.ToArray());

        foreach (var row in rows.Where(x => x.IsFix))
            awards.Add(row.Reward.ToArray());

        // Groups are separate weight spaces (index 101 carries two groups of five rows at weight
        // 2000 each), so the roll happens inside every group.
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

    /// <summary>Renders granted rows for the log line, e.g. "[5,5,1,1,10], [5,5,1,2,10]".</summary>
    private static string Describe(JsonArray rows) =>
        rows.Count == 0 ? "none" : string.Join(", ", rows.Select(x => x?.ToJsonString() ?? "null"));

    /// <summary>One box to open: the bag row, its Item.tbBox index and that index's rows.</summary>
    private readonly record struct BoxUse(
        BaseGameItemInfo Item,
        uint BoxIndex,
        List<BoxExcel> Rows);

    private readonly record struct AwardKey(uint Genre, uint Detail, uint Particular, uint Level)
    {
        public override int GetHashCode() =>
            unchecked((int)(Genre * 31_337u + Detail * 7_919u + Particular * 131u + Level));
    }
}

public sealed class TokenBoxUseParam
{
    /// <summary>Bag row ids to open, at most the one box each of them holds per entry.</summary>
    [JsonPropertyName("tbList")] public List<uint>? TbList { get; set; }
}
