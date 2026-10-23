using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Support;
using MikuSB.Proto;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.SupporterCard;

[CallGSApi("SupporterCard_Upgrade")]
public class SupporterCard_Upgrade : CallGSHandler<SupporterCardUpgradeParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, SupporterCardUpgradeParam req)
    {
        var player = context.Connection.Player!;

        if (req == null || req.SupportCardUid == 0 || req.Materials == null || req.Materials.Count == 0)
        {
            return Task.FromResult(CallGSResult.Ok("{}", "Logistics_Upgrade"));
        }

        var supportCard = player.InventoryManager.GetSupportCardItem((uint)req.SupportCardUid);
        if (supportCard == null)
        {
            return Task.FromResult(CallGSResult.Ok("{}", "Logistics_Upgrade"));
        }

        var supportCardExcel = GameData.SupportCardData.FirstOrDefault(x => x.TemplateId == supportCard.TemplateId);
        var maxLevel = supportCardExcel?.MaxLevel ?? 10;

        // Validate all materials exist with sufficient count
        foreach (var mat in req.Materials)
        {
            var item = player.InventoryManager.InventoryData.Items.GetValueOrDefault((uint)mat.Id);
            if (item == null || item.ItemCount < mat.Num)
            {
                return Task.FromResult(CallGSResult.Ok("{}", "Logistics_Upgrade"));
            }
        }

        // Consume materials and accumulate exp
        var syncItems = new List<Item>();
        uint gainedExp = 0;

        foreach (var mat in req.Materials)
        {
            var item = player.InventoryManager.InventoryData.Items[(uint)mat.Id];

            // Look up ProvideExp: check SupportCardData first, then SuppliesData
            uint provideExp = 0;
            var scExcel = GameData.SupportCardData.FirstOrDefault(x => x.TemplateId == item.TemplateId);
            if (scExcel != null)
                provideExp = scExcel.ProvideExp;
            else if (GameData.SuppliesData.TryGetValue(item.TemplateId, out var supExcel))
                provideExp = supExcel.ProvideExp;
            gainedExp += provideExp * (uint)mat.Num;

            item.ItemCount -= (uint)mat.Num;
            var proto = item.ToProto();
            if (item.ItemCount == 0)
            {
                player.InventoryManager.InventoryData.Items.Remove(item.UniqueId);
                proto.Count = 0;
            }
            syncItems.Add(proto);
        }

        // Apply exp and level up
        if (supportCard.Level == 0) supportCard.Level = 1;
        supportCard.Exp += gainedExp;
        while (supportCard.Level < maxLevel)
        {
            var expNeeded = GetExpNeeded(supportCard.Level);
            if (expNeeded == 0 || supportCard.Exp < expNeeded) break;
            supportCard.Exp -= expNeeded;
            supportCard.Level++;
        }
        if (supportCard.Level >= maxLevel)
        {
            supportCard.Exp = 0;
            supportCard.Level = maxLevel;

            // Unlock next affix slot when reaching max level for the first time
            if (supportCardExcel != null)
            {
                var currentSlots = Enumerable.Range(1, SupportAffixStateService.ActiveThirdAffixSlot)
                    .Count(slot => SupportAffixStateService.HasAffix(supportCard, slot));
                var totalSlots = supportCardExcel.TotalAffixCount;
                if (currentSlots < totalSlots && currentSlots < supportCardExcel.AffixPool.Count)
                {
                    var poolId = supportCardExcel.AffixPool[currentSlots];
                    var (affixId, tier) = SupportAffixService.GenerateRandomAffix(poolId);
                    if (affixId > 0)
                        SupportAffixStateService.SetAffix(supportCard, currentSlots + 1, affixId, tier);
                }
            }
        }

        syncItems.Add(supportCard.ToProto());

        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);

        var sync = new NtfSyncPlayer();
        sync.Items.AddRange(syncItems);

        return Task.FromResult(CallGSResult.Ok("{}", sync, "Logistics_Upgrade"));
    }

    private static uint GetExpNeeded(uint level)
    {
        if (GameData.UpgradeExpData.TryGetValue((int)level, out var row))
            return row.SusNeedExp;
        return 0;
    }
}

public sealed class SupporterCardUpgradeParam
{
    [JsonPropertyName("Id")]
    public int SupportCardUid { get; set; }

    [JsonPropertyName("tbMaterials")]
    public List<UpgradeMaterial> Materials { get; set; } = [];
}

public sealed class UpgradeMaterial
{
    [JsonPropertyName("Id")]
    public int Id { get; set; }

    [JsonPropertyName("Num")]
    public uint Num { get; set; }
}
