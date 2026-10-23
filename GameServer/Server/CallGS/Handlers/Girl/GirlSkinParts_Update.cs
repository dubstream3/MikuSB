using MikuSB.Data;
using MikuSB.Database.Inventory;
using MikuSB.Proto;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

[CallGSApi("GirlSkinParts_Update")]
public class GirlSkinParts_Update : CallGSHandler<GirlSkinPartsUpdateParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, GirlSkinPartsUpdateParam req)
    {

        if (req == null)
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }
        var player = context.Connection.Player!;
        var data = new List<GameSkinInfo>();
        foreach(var partId in req.PartsId)
        {
            var partData = player.InventoryManager.GetNormalItem(partId);
            if (partData == null) continue;

            var partExcel = GameData.CardSkinPartsData.Values.FirstOrDefault(x => x.TemplateId == partData.TemplateId);
            if (partExcel == null) continue;

            var skinData = player.InventoryManager.GetSkinItem(req.SkinId);
            if (skinData == null) continue;

            skinData.PartSlots[partExcel.Detail] = partData.UniqueId;
            data.Add(skinData);
        }

        var sync = new NtfSyncPlayer 
        {
            Items = { data.Select(x => x.ToProto()) }
        };
        return Task.FromResult(CallGSResult.Ok("{}", sync));
    }
}

public sealed class GirlSkinPartsUpdateParam
{
    [JsonPropertyName("tbPartsID")]
    public List<uint> PartsId { get; set; } = [];

    [JsonPropertyName("nSkinId")]
    public uint SkinId { get; set; }
}
