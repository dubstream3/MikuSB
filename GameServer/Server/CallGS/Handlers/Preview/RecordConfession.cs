using MikuSB.Database.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Misc;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using System.Text.Json;
using System.Text.Json.Serialization;

using MikuSB.Data;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Preview;

[CallGSApi("RecordConfession")]
public class RecordConfession : CallGSHandler<RecordConfessionParam>
{
    private const uint MainSceneGID = AttrIds.Scene.MainGid;
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, RecordConfessionParam req)
    {

        if (req == null) return Task.FromResult(CallGSResult.NoResponse());
        var sid = req.Id + 10;
        var player = context.Connection.Player!;
        var attr = player.Attributes.Set(MainSceneGID, sid, 1);
        var sync = new NtfSyncPlayer();
        player.Attributes.SyncTo(sync, attr);
        return Task.FromResult(CallGSResult.Ok("{}", sync));
    }
}

public sealed class RecordConfessionParam
{
    [JsonPropertyName("nIdx")]
    public uint Id { get; set; }
}
