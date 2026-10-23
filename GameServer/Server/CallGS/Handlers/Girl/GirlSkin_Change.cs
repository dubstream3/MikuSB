using MikuSB.Proto;
using System.Text.Json;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

[CallGSApi("GirlSkin_Change")]
public class GirlSkin_Change : CallGSHandler<ChangeSkinParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, ChangeSkinParam girlSkinData)
    {
        var player = context.Connection.Player!;

        var cardData = player.CharacterManager.GetCharacterByGUID((uint)girlSkinData!.CardId);
        if (cardData == null) return Task.FromResult(CallGSResult.NoResponse());
        cardData.SkinId = (uint)girlSkinData.Id;

        var sync = new NtfSyncPlayer
        {
            Items = { cardData.ToProto() }
        };
        return Task.FromResult(CallGSResult.Ok("{}", sync));
    }
}
