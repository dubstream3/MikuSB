using MikuSB.Database;
using MikuSB.Enums.Player;
using MikuSB.Proto;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Misc;

[CallGSApi("PlayerSetting_SetProfileFace")]
public class PlayerSetting_SetProfileFace : CallGSHandler<SetProfileFaceParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, SetProfileFaceParam req)
    {
        var player = context.Connection.Player!;

        if (req == null)
            return Task.FromResult(CallGSResult.NoResponse());
        if (req.HeadItemId > 0)
        {
            var item = player.InventoryManager.GetNormalItem(req.HeadItemId);
            if (item == null)
            {
                return Task.FromResult(CallGSResult.Ok("{\"err\":\"error.BadParam\"}"));
            }
            player.SetShowItem((int)ProfileShowItemTypeEnum.SHOWITEM_FACE, item.UniqueId);
        }
        if (req.FrameItemId > 0)
        {
            var item = player.InventoryManager.GetNormalItem(req.FrameItemId);
            if (item == null)
            {
                return Task.FromResult(CallGSResult.Ok("{\"err\":\"error.BadParam\"}"));
            }
            player.SetShowItem((int)ProfileShowItemTypeEnum.SHOWITEM_FRAME, item.UniqueId);
        }
        DatabaseHelper.SaveDatabaseType(player.Data);
        var sync = new NtfSyncPlayer();
        sync.ShowItems.AddRange(player.Data.ShowItems);
        return Task.FromResult(CallGSResult.Ok("null", sync));
    }
}

public sealed class SetProfileFaceParam
{
    [JsonPropertyName("nHeadItemID")] public uint HeadItemId { get; set; }
    [JsonPropertyName("nFrameItemID")] public uint FrameItemId { get; set; }
}
