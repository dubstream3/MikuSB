using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Database;
using MikuSB.Data;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Nita;

// 妮塔大富翁 round settlement (UMG/NitaMonopoly/umg_nitamonolopy_settlement.lua:101-106). The UI
// raises a full screen connection overlay right after the request (:107) and only drops it when the
// s2c callback runs (Activity/Activity.lua:1702-1709), so a missing handler freezes the panel.
//
//     { nMoney: int, nSumThrowCount: int, nSumCellTriger: int, sLog: string }
//
// The client never converts leaf coins into anything by itself: CurMoney only moves around inside the
// board (Activity/NitaMonolopy.lua:2121-2132, :2182) and no Item.Gain follows the settlement, so the
// server owns the tree house exp. Since the client has no formula either (the settlement widget
// animates from its own locally cached nLastEx, settlement.lua:92-113, and ignores the reply
// arguments), 1 leaf coin = 1 exp is the only defensible mapping; it puts the 10k..180k tree levels
// (activity/nita_monopoly/tree.json) a few rounds apart, which matches the pacing of the table.
//
// Response: { nMoney, nEX } -- both ignored by the current client, kept for future widgets.
[CallGSApi("NitaMonolopy_GameSettlement")]
public class NitaMonolopy_GameSettlement : CallGSHandler<NitaMonolopyGameSettlementParam>
{
    private static readonly Logger Log = new(nameof(NitaMonolopy_GameSettlement));

    protected override Task<CallGSResult> HandleAsync(
        CallGSContext context,
        NitaMonolopyGameSettlementParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        const uint gid = AttrIds.Activity.NitaMonopolyGroupId;
        var sync = new NtfSyncPlayer();

        var exAttr = player.Attributes.Add(gid, AttrIds.Activity.NitaMonopolyTreeExpSid, request.Money);
        player.Attributes.SyncTo(sync, exAttr);

        var roundAttr = player.Attributes.Add(gid, AttrIds.Activity.NitaMonopolySumRoundSid, 1);
        player.Attributes.SyncTo(sync, roundAttr);

        DatabaseHelper.SaveDatabaseType(player.Data);

        Log.Info($"Nita settlement: money={request.Money}, throws={request.SumThrowCount}, " +
                 $"cells={request.SumCellTriger} -> exp={exAttr.Val}, rounds={roundAttr.Val} " +
                 $"for uid={player.Uid}.");

        return Task.FromResult(CallGSResult.Ok(new JsonObject
        {
            ["nMoney"] = request.Money,
            ["nEX"] = exAttr.Val
        }, sync));
    }
}

public sealed class NitaMonolopyGameSettlementParam
{
    [JsonPropertyName("nMoney")] public uint Money { get; set; }
    [JsonPropertyName("nSumThrowCount")] public uint SumThrowCount { get; set; }
    [JsonPropertyName("nSumCellTriger")] public uint SumCellTriger { get; set; }
    [JsonPropertyName("sLog")] public string? Log { get; set; }
}
