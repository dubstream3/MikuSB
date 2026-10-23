using MikuSB.Database.Player;
using MikuSB.Proto;
using MikuSB.GameServer.Game.Player;
using System.Text.Json;
using System.Text.Json.Serialization;

using MikuSB.Data;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Scene;

// Response:{sErr:true or false}
[CallGSApi("ChangeMainScene")]
public class ChangeMainScene : CallGSHandler<ChangeMainSceneParam>
{
    private const uint MainSceneGID = AttrIds.Scene.MainGid;
    private const uint MainSceneSID = AttrIds.Scene.MainSid;

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, ChangeMainSceneParam req)
    {
        string rsp = $"{{\"sErr\":false}}";

        if (req == null) 
        {
            return Task.FromResult(CallGSResult.Ok(rsp));
        } 

        var player = context.Connection.Player!;
        var mainSceneAttr = player.Attributes.GetOrCreate(MainSceneGID, MainSceneSID);
        var sync = new NtfSyncPlayer();
        mainSceneAttr.Val = req.Id;

        player.Attributes.SyncTo(sync, mainSceneAttr);
        return Task.FromResult(CallGSResult.Ok(rsp, sync));
    }
}

public sealed class ChangeMainSceneParam
{
    [JsonPropertyName("nId")]
    public uint Id { get; set; }
}
