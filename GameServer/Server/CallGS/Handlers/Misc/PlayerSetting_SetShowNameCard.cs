using MikuSB.Database;
using MikuSB.Enums.Player;
using MikuSB.Proto;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Misc;

[CallGSApi("PlayerSetting_SetShowNameCard")]
public class PlayerSetting_SetShowNameCard : CallGSHandler<SetShowNameCardParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, SetShowNameCardParam req)
    {
        var player = context.Connection.Player!;

        if (req == null)
            return Task.FromResult(CallGSResult.NoResponse());
        var item = player.InventoryManager.GetNormalItem(req.Id);
        if (item == null)
        {
            return Task.FromResult(CallGSResult.Ok("{\"err\":\"error.BadParam\"}"));
        }

        player.SetShowItem((int)ProfileShowItemTypeEnum.SHOWITEM_NAMECARD, item.UniqueId);
        DatabaseHelper.SaveDatabaseType(player.Data);

        var sync = new NtfSyncPlayer();
        sync.ShowItems.AddRange(player.Data.ShowItems);
        return Task.FromResult(CallGSResult.Ok("null", sync));
    }
}

public sealed class SetShowNameCardParam
{
    [JsonPropertyName("nID")]
    public uint Id { get; set; }
}
