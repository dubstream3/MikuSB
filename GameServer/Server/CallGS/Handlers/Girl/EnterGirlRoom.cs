using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

[CallGSApi("EnterGirlRoom")]
public class EnterGirlRoom : CallGSHandler<EnterGirlRoomParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, EnterGirlRoomParam req)
    {

        var response = new JsonObject
        {
            ["nCardId"] = 0,
            ["nSkinId"] = 0,
            ["bOpen"] = false
        };
        if (req == null)
        {
            return Task.FromResult(CallGSResult.Ok(response.ToJsonString()));
        }

        response["nCardId"] = req.CardId;
        response["nSkinId"] = req.SkinId;
        response["bOpen"] = true;
        return Task.FromResult(CallGSResult.Ok(response.ToJsonString()));
    }
}

public sealed class EnterGirlRoomParam
{
    [JsonPropertyName("nSkinId")]
    public int SkinId { get; set; }

    [JsonPropertyName("nCardId")]
    public uint CardId { get; set; }
}
