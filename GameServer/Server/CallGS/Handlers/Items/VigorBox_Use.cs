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

// Stamina potion request. The client never sends a GDPL or an amount: it only expands the potion
// stacks it wants to drink into bag row ids and lets the server do the math
//   - umg_common_purchase_energy.lua:120-127 -> me:CallGS('VigorBox_Use', json.encode({ tbList = tbList }))
//     with tbList = Item.GetExpirationItemID({ G, D, P, L }, nCount) (Item.lua:680-716), i.e. the
//     soonest-expiring stacks first, at most nCount rows
//   - umg_common_purchase_exchange.lua:169 uses the same shape for a stamina box
// The per-potion stamina is the template's Param1 (Item.lua:871-879 Item.IsVigorItem tests
// LuaType == "vigor_box"; the UI shows nCount * pTemp.Param1 at umg_common_purchase_energy.lua:210-214).
// Request: { tbList: [ rowId, ... ] }
// Response: { tbAward: [] } -- the client handler Item.lua:1142-1152 fires Event.GetBoxItem /
// Event.ItemExpiration and only calls Item.Gain when tbAward is set; the new stamina arrives through
// the packet's ExtraSync (PlayerInstance.AddCurrency writes sync.Core[Vigor]).
[CallGSApi("VigorBox_Use")]
public class VigorBox_Use : CallGSHandler<VigorBoxUseParam>
{
    private static readonly Logger Log = new(nameof(VigorBox_Use));

    // One use should never drain an unbounded number of stacks.
    private const int MaxUseCount = 99;

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, VigorBoxUseParam request)
    {
        var player = context.Connection.Player!;

        if (request.TbList == null || request.TbList.Count == 0 || request.TbList.Count > MaxUseCount)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var inventory = player.InventoryManager.InventoryData;

        // Resolve and validate every row before touching anything, so a bad row cannot grant stamina
        // and then abort.
        var uses = new List<(BaseGameItemInfo Item, uint Amount)>();
        foreach (var rowId in request.TbList)
        {
            if (!inventory.Items.TryGetValue(rowId, out var item) || item.ItemCount == 0)
            {
                Log.Warn($"VigorBox_Use uid={player.Uid}: row {rowId} is missing or empty; nothing granted.");
                return Task.FromResult(CallGSResult.Error("tip.NotEnoughItem"));
            }

            if (!ItemTemplateInfo.TryResolve(item.TemplateId, out var template) || !template.IsVigorBox)
            {
                Log.Warn(
                    $"VigorBox_Use uid={player.Uid}: row {rowId} (template={item.TemplateId}) is not a " +
                    "vigor_box; nothing granted.");
                return Task.FromResult(CallGSResult.Error("error.BadParam"));
            }

            uses.Add((item, template.Param1));
        }

        var sync = new NtfSyncPlayer();
        var totalVigor = 0u;

        foreach (var (item, amount) in uses)
        {
            // One row of tbList is one potion of the stack, so only a single unit is consumed and the
            // potion's own Param1 decides the stamina.
            item.ItemCount -= 1;

            // The client only refreshes the bag when the row it used comes back in the sync
            // (umg_common_purchase_energy.lua:470-506 waits for Event.GetBoxItem and then calls
            // BagUI:UpdatePage), so always echo the row, not just when it empties.
            sync.Items.Add(ItemTemplateInfo.ToSyncProto(item));

            totalVigor += amount;
            Log.Info(
                $"VigorBox_Use uid={player.Uid} used row {item.UniqueId} (template={item.TemplateId}) " +
                $"for {amount} vigor, row left {item.ItemCount}.");
        }

        // Vigor is not a group-1 attribute: VigorService.Add routes it to Data.Vigor and pushes both
        // the value and the recovery clock into sync.Core, which is what the client's me:Vigor() /
        // me:LastVigorTime() read (Script/Cash/Cash.lua:55-63 -> me:Vigor() for Cash.MoneyType_Vigour).
        // Potions respect the level cap and restart the natural-recovery countdown, exactly like the
        // client's own display does (Player.GetMaxVigor from player/levels.json MaxVigor).
        VigorService.Add(player, totalVigor, sync);

        DatabaseHelper.SaveDatabaseType(player.Data);
        DatabaseHelper.SaveDatabaseType(inventory);

        var payload = new JsonObject
        {
            ["tbAward"] = new JsonArray(),
            ["nVigor"] = (int)player.Data.Vigor
        };

        Log.Info(
            $"VigorBox_Use uid={player.Uid} used {uses.Count} potion(s) for {totalVigor} vigor, " +
            $"vigor is now {player.Data.Vigor}.");

        return Task.FromResult(CallGSResult.Ok(payload.ToJsonString(), sync));
    }
}

public sealed class VigorBoxUseParam
{
    /// <summary>Bag row ids to consume, at most one potion per row.</summary>
    [JsonPropertyName("tbList")] public List<uint>? TbList { get; set; }
}
