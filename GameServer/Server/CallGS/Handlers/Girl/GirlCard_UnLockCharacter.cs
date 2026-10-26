using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.Enums.Item;
using MikuSB.Proto;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

/// <summary>
/// 用角色碎片解锁角色。
/// 客户端入口：RoleCard.Req_UnLockPlayer（umg_role.lua 的解锁按钮）→ GirlCard_UnLockCharacter，参数只有 {g,d,p,l}。
/// 碎片需求来自 item/templates/card.json 的 Pieces 字段：[[Genre,Detail,Particular,Level,Count]]，
/// 基础角色是 30 片，后续装甲是 60 片。
/// </summary>
[CallGSApi("GirlCard_UnLockCharacter")]
public class GirlCard_UnLockCharacter : CallGSHandler<GirlCardUnLockCharacterParam>
{
    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, GirlCardUnLockCharacterParam req)
    {
        var player = context.Connection.Player!;

        if (req == null || req.Genre == 0 || req.Detail == 0 || req.Particular == 0 || req.Level == 0)
            return CallGSResult.Error("error.BadParam");

        var card = GameData.CardData.Values.FirstOrDefault(x =>
            x.Genre == req.Genre
            && x.Detail == req.Detail
            && x.Particular == req.Particular
            && x.Level == req.Level);
        if (card == null)
            return CallGSResult.Error("error.BadParam");

        if (player.CharacterManager.GetCharacterGDPL((ItemTypeEnum)req.Genre, (int)req.Detail, (int)req.Particular) != null)
            return CallGSResult.Error("ui.TxtOverGet");

        // 没有 Pieces 的卡（girl044 这类）在数据层面就没有解锁路径，直接拒绝而不是扣错东西。
        var pieces = card.Pieces.FirstOrDefault(x => x.Count >= 5);
        if (pieces == null)
            return CallGSResult.Error("ui.TxtNotOpen");

        var pieceTemplateId = GameResourceTemplateId.FromGdpl(
            (uint)pieces[0], (uint)pieces[1], (uint)pieces[2], (uint)pieces[3]);
        var needed = (uint)pieces[4];

        var pieceItem = player.InventoryManager.InventoryData.Items.Values
            .FirstOrDefault(x => x.TemplateId == pieceTemplateId);
        if (pieceItem == null || pieceItem.ItemCount < needed)
            return CallGSResult.Error("tip.material_not_enough");

        var syncItems = new List<Item>();
        pieceItem.ItemCount -= needed;
        if (pieceItem.ItemCount == 0)
        {
            player.InventoryManager.InventoryData.Items.Remove(pieceItem.UniqueId);
            syncItems.Add(BuildRemovedProto(pieceItem));
        }
        else
        {
            syncItems.Add(pieceItem.ToProto());
        }

        var character = await player.CharacterManager.AddCharacter(
            (ItemTypeEnum)req.Genre, req.Detail, req.Particular, 1, card.InitBreak, false);
        if (character == null)
            return CallGSResult.Error("error.BadParam");

        syncItems.Add(character.ToProto());

        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
        DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);

        var sync = new NtfSyncPlayer();
        sync.Items.AddRange(syncItems);

        return CallGSResult.Ok("{}", sync);
    }

    private static Item BuildRemovedProto(BaseGameItemInfo item)
    {
        var proto = item.ToProto();
        proto.Count = 0;
        return proto;
    }
}

public sealed class GirlCardUnLockCharacterParam
{
    [JsonPropertyName("g")]
    public uint Genre { get; set; }

    [JsonPropertyName("d")]
    public uint Detail { get; set; }

    [JsonPropertyName("p")]
    public uint Particular { get; set; }

    [JsonPropertyName("l")]
    public uint Level { get; set; }
}
