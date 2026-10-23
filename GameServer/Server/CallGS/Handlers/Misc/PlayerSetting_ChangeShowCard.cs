using MikuSB.Database;
using MikuSB.Enums.Player;
using MikuSB.Proto;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Misc;

[CallGSApi("PlayerSetting_ChangeShowCard")]
public class PlayerSetting_ChangeShowCard : CallGSHandler<ChangeShowCardParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, ChangeShowCardParam req)
    {
        var player = context.Connection.Player!;

        if (req == null)
            return Task.FromResult(CallGSResult.NoResponse());
        var card = player.CharacterManager.GetCharacterByGUID(req.Id);
        if (card == null)
        {
            return Task.FromResult(CallGSResult.Ok("{}"));
        }
        player.SetShowItem((int)ProfileShowItemTypeEnum.SHOWITEM_GIRL, card.Guid);
        DatabaseHelper.SaveDatabaseType(player.Data);
        var sync = new NtfSyncPlayer();
        sync.ShowItems.AddRange(player.Data.ShowItems);
        return Task.FromResult(CallGSResult.Ok("{}", sync));
    }
}

public sealed class ChangeShowCardParam
{
    [JsonPropertyName("nID")]
    public uint Id { get; set; }
}
