using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using System.Text.Json;
using System.Text.Json.Serialization;

using MikuSB.Data;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Shop;

[CallGSApi("IBLogic_GoodsRedDot")]
public class IBLogic_GoodsRedDot : CallGSHandler<IbGoodsRedDotParam>
{
    private const uint RedGroupId = AttrIds.Shop.RedDotGid;

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, IbGoodsRedDotParam req)
    {

        if (req?.GoodsIds == null || req.GoodsIds.Count == 0)
        {
            return Task.FromResult(CallGSResult.Ok("null"));
        }

        var player = context.Connection.Player!;
        var sync = new NtfSyncPlayer();
        var changed = false;

        foreach (var goodsId in req.GoodsIds.Where(x => x > 0).Distinct())
        {
            var attr = player.Attributes.GetOrCreate(RedGroupId, goodsId);
            if (attr.Val > 0)
                continue;

            attr.Val = 1;
            player.Attributes.SyncTo(sync, attr);
            changed = true;
        }

        if (changed)
            DatabaseHelper.SaveDatabaseType(player.Data);

        return Task.FromResult(CallGSResult.Ok("null", sync));
    }

}

public sealed class IbGoodsRedDotParam
{
    [JsonPropertyName("tbList")]
    public List<uint> GoodsIds { get; set; } = [];
}
