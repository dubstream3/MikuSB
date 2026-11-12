using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Proto;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// Clears (or re-raises) the "new monster in the bag" red dot stored in gid 128 sid 4.
/// The client call site is fire-and-forget: its callback only closes the connection
/// (VirCapture.lua:2345-2365), so the reply is a plain success packet whose value is the synced
/// attribute - sending nothing would leave UI.ShowConnection spinning.
/// </summary>
[CallGSApi("VirCapture_ChangeBagRedFlag")]
public class VirCapture_ChangeBagRedFlag : CallGSHandler<VirCaptureChangeBagRedFlagParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, VirCaptureChangeBagRedFlagParam req)
    {
        var player = context.Connection.Player!;
        var value = req.Flag ? 1u : 0u;

        var sync = new NtfSyncPlayer();
        var attr = player.Attributes.GetOrCreate(AttrIds.VirCapture.Gid, AttrIds.VirCapture.BagRedFlagSid);
        if (attr.Val != value)
        {
            attr.Val = value;
            player.Attributes.SyncTo(sync, attr);
            DatabaseHelper.SaveDatabaseType(player.Data);
        }

        return Task.FromResult(CallGSResult.Ok("{}", sync));
    }
}

public sealed class VirCaptureChangeBagRedFlagParam
{
    [JsonPropertyName("bFlag")]
    public bool Flag { get; set; }
}
