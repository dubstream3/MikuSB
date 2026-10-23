using MikuSB.Proto;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

[CallGSApi("RoleCard_SetSupporterTeamIndex")]
public class RoleCard_SetSupporterTeamIndex : CallGSHandler<SetSupporterTeamIndexParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, SetSupporterTeamIndexParam req)
    {

        if (req == null)
        {
            return Task.FromResult(CallGSResult.Ok("{\"err\":\"error.BadParam\"}"));
        }
        var player = context.Connection.Player!;
        var cardData = player.CharacterManager.GetCharacterByGUID(req.CardId);
        if (cardData == null) return Task.FromResult(CallGSResult.NoResponse());
        cardData.SupportTeamIndex = req.Index;
        var sync = new NtfSyncPlayer
        {
            Items = { cardData.ToProto() }
        };
        return Task.FromResult(CallGSResult.Ok("null", sync));
    }
}

public sealed class SetSupporterTeamIndexParam
{
    [JsonPropertyName("Id")]
    public uint CardId { get; set; }
    public uint Index { get; set; }
}
