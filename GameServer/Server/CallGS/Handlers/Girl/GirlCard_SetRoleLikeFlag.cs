using MikuSB.Enums.Item;
using MikuSB.Proto;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

[CallGSApi("GirlCard_SetRoleLikeFlag")]
public class GirlCard_SetRoleLikeFlag : CallGSHandler<SetRoleLikeFlagParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, SetRoleLikeFlagParam girlData)
    {
        var player = context.Connection.Player!;

        if (girlData == null) return Task.FromResult(CallGSResult.NoResponse());
        var cardData = player.CharacterManager.GetCharacterByGUID(girlData.CardId);
        if (cardData == null) return Task.FromResult(CallGSResult.NoResponse());
        cardData.Flag = girlData.Flag == 1
            ? ItemFlagEnum.FLAG_ROLE_LIKE
            : ItemFlagEnum.FLAG_READED;

        var sync = new NtfSyncPlayer
        {
            Items = { cardData.ToProto() }
        };

        return Task.FromResult(CallGSResult.Ok("{}", sync));
    }
}

public sealed class SetRoleLikeFlagParam
{
    [JsonPropertyName("nFlag")]
    public int Flag { get; set; }

    [JsonPropertyName("nCardID")]
    public uint CardId { get; set; }
}
