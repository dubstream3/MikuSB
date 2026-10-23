using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using System.Text.Json;
using System.Text.Json.Serialization;

using MikuSB.Data;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Misc;

[CallGSApi("Adjust_Record")]
public class Adjust_Record : CallGSHandler<AdjustRecordParam>
{
    private const uint GroupId = AttrIds.Adjust.Gid;

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, AdjustRecordParam req)
    {

        if (req == null || req.Type == 0)
        {
            return Task.FromResult(CallGSResult.Ok("null"));
        }

        var player = context.Connection.Player!;
        var sync = new NtfSyncPlayer();
        var attr = player.Attributes.GetOrCreate(GroupId, req.Type);

        if (attr.Val == 0)
        {
            attr.Val = 1;
            player.Attributes.SyncTo(sync, attr);
            DatabaseHelper.SaveDatabaseType(player.Data);
        }

        return Task.FromResult(CallGSResult.Ok("null", sync));
    }

}

public sealed class AdjustRecordParam
{
    [JsonPropertyName("nType")]
    public uint Type { get; set; }
}
