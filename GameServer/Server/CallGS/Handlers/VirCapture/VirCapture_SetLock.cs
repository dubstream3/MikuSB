using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.Enums.Item;
using MikuSB.Proto;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// Locks or unlocks one monster card. The client flips Item.FLAG_LOCK and expects the server to own
/// the flag (VirCapture.lua:2816-2840 - the callback re-reads pMonster:HasFlag from its own item
/// copy, so the reply has to carry the item through the sync block).
/// Reply keeps the fields the formation list reads: MonsterId + nRefreshType=2
/// (umg_vircapture_formation.lua:1037-1055).
/// </summary>
[CallGSApi("VirCapture_SetLock")]
public class VirCapture_SetLock : CallGSHandler<VirCaptureSetLockParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, VirCaptureSetLockParam req)
    {
        if (req == null || req.MonsterId <= 0)
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var player = context.Connection.Player!;
        var item = player.InventoryManager.GetNormalItem((uint)req.MonsterId);
        if (item == null || item.ItemType != ItemTypeEnum.TYPE_MONSTER_CARD)
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var sync = new NtfSyncPlayer();
        if (req.Lock)
            item.Flag |= ItemFlagEnum.FLAG_LOCK;
        else
            item.Flag &= ~ItemFlagEnum.FLAG_LOCK;

        sync.Items.Add(item.ToProto());
        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);

        var rsp = new JsonObject
        {
            ["MonsterId"] = req.MonsterId,
            ["bLock"] = req.Lock,
            ["nRefreshType"] = 2
        };
        return Task.FromResult(CallGSResult.Ok(rsp.ToJsonString(), sync));
    }
}

public sealed class VirCaptureSetLockParam
{
    [JsonPropertyName("MonsterId")]
    public int MonsterId { get; set; }

    [JsonPropertyName("bLock")]
    public bool Lock { get; set; }
}
