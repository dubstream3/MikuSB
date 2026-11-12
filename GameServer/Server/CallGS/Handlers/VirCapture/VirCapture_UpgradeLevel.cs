using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database.Inventory;
using MikuSB.Enums.Item;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// Upgrades one skill affix of a monster card. The client validates the price locally
/// (uw_vircapture_skillitem.lua:214-237 reads skillcost Level{N}) and then never touches the
/// inventory, so the charge and the level write both happen here.
/// The request names the skill id, not the slot; the slot is resolved from the card's affix list
/// (4..6, VirCapture.lua:116-118).
/// </summary>
[CallGSApi("VirCapture_UpgradeLevel")]
public class VirCapture_UpgradeLevel : CallGSHandler<VirCaptureUpgradeLevelParam>
{
    private const int SkillFirstSlot = 4;
    private const int SkillLastSlot = 6;

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, VirCaptureUpgradeLevelParam req)
    {
        if (req == null || req.Id <= 0 || req.SkillId == 0)
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var player = context.Connection.Player!;
        var item = player.InventoryManager.GetNormalItem((uint)req.Id);
        if (item == null || item.ItemType != ItemTypeEnum.TYPE_MONSTER_CARD)
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        if (!GameData.VirCaptureSkillListData.TryGetValue((uint)req.SkillId, out var skill))
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var slot = FindSkillSlot(item, skill.Id);
        if (slot < 0)
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var levelIndex = (slot - 1) * 2 + 1;
        var level = item.CardAffixs[levelIndex];
        if (level == 0 || level >= skill.MaxLevel)
        {
            return Task.FromResult(CallGSResult.Error("error.condition_limit"));
        }

        if (!GameData.VirCaptureSkillCostData.TryGetValue(skill.CostId, out var cost))
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var rows = cost.CostFor(level);
        if (rows.Count == 0)
        {
            // No Level{N} row means the affix is already at its cap.
            return Task.FromResult(CallGSResult.Error("error.condition_limit"));
        }

        foreach (var row in rows)
        {
            if (CurrencyService.GetItemCount(player, row) < row[4])
                return Task.FromResult(CallGSResult.Error("tip.not_material"));
        }

        var sync = new NtfSyncPlayer();
        foreach (var row in rows)
        {
            if (!CurrencyService.TrySpendItem(player, row, row[4], out _))
                return Task.FromResult(CallGSResult.Error("tip.not_material"));
            SyncWallet(player, row, sync);
        }

        item.CardAffixs[levelIndex] = level + 1;
        sync.Items.Add(item.ToProto());

        CurrencyService.Persist(player);
        MikuSB.Database.DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);

        var rsp = new JsonObject
        {
            ["nItemId"] = req.Id,
            ["nSkillId"] = skill.Id,
            ["nSkillLevel"] = level + 1
        };
        return Task.FromResult(CallGSResult.Ok(rsp.ToJsonString(), sync));
    }

    private static int FindSkillSlot(BaseGameItemInfo item, uint skillId)
    {
        for (var slot = SkillFirstSlot; slot <= SkillLastSlot; slot++)
        {
            var index = (slot - 1) * 2;
            if (item.CardAffixs.Count <= index + 1)
                break;
            if (item.CardAffixs[index] == skillId && item.CardAffixs[index + 1] > 0)
                return slot;
        }

        return -1;
    }

    private static void SyncWallet(PlayerInstance player, uint[] row, NtfSyncPlayer sync)
    {
        var templateId = GameResourceTemplateId.FromGdpl(row[0], row[1], row[2], row[3]);
        var wallet = player.InventoryManager.InventoryData.Items.Values
            .FirstOrDefault(x => x.TemplateId == templateId);
        if (wallet != null)
            sync.Items.Add(wallet.ToProto());
    }
}

public sealed class VirCaptureUpgradeLevelParam
{
    [JsonPropertyName("nId")]
    public int Id { get; set; }

    [JsonPropertyName("nSkillId")]
    public uint SkillId { get; set; }
}
