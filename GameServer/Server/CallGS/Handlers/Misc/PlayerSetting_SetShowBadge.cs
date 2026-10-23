using MikuSB.Database;
using MikuSB.Enums.Player;
using MikuSB.Proto;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Misc;

[CallGSApi("PlayerSetting_SetShowBadge")]
public class PlayerSetting_SetShowBadge : CallGSHandler<SetShowBadgeParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, SetShowBadgeParam req)
    {
        var player = context.Connection.Player!;

        if (req == null)
        {
            return Task.FromResult(CallGSResult.Ok("{\"err\":\"error.BadParam\"}"));
        }

        var slots = new[]
        {
            ProfileShowItemTypeEnum.SHOWITEM_BADGE1,
            ProfileShowItemTypeEnum.SHOWITEM_BADGE2,
            ProfileShowItemTypeEnum.SHOWITEM_BADGE3
        };
        for (int i = 0; i < slots.Length; i++)
        {
            var uniqueId = i < req.Badges.Count ? req.Badges[i] : 0;
            player.SetShowItem((int)slots[i], uniqueId);
        }

        DatabaseHelper.SaveDatabaseType(player.Data);

        var sync = new NtfSyncPlayer();
        sync.ShowItems.AddRange(player.Data.ShowItems);
        return Task.FromResult(CallGSResult.Ok("null", sync));
    }
}

public sealed class SetShowBadgeParam
{
    [JsonPropertyName("tbBadge")]
    public List<uint> Badges { get; set; } = [];
}
