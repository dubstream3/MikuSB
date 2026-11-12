using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database.Inventory;
using MikuSB.Enums.Item;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// Capture gacha. The client only draws the animation and shows the cost (umg_vircapture_gacha.lua:
/// 22-130); it never charges, so the server owns the price, the bag limit and the roll.
///
/// Roll: the banner splits between monsters the player has not unlocked yet (UnlockWeight) and
/// duplicates they already own (LockWeight), then picks inside that bucket by the pool weight of
/// pool/pool01.json. When a bucket is empty the other one is used, which keeps a fresh
/// account able to pull at all.
/// </summary>
[CallGSApi("VirCapture_GachaMonster")]
public class VirCapture_GachaMonster : CallGSHandler<VirCaptureGachaMonsterParam>
{
    private const uint MonsterCardGenre = (uint)ItemTypeEnum.TYPE_MONSTER_CARD;
    private const uint RikiGroupId = AttrIds.VirCapture.RikiGid;

    private static readonly Logger Log = new("VirCapture.Gacha");

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, VirCaptureGachaMonsterParam req)
    {
        var player = context.Connection.Player!;
        var banner = ResolveBanner(player);
        if (banner == null)
        {
            return CallGSResult.Error("ui.TxtNotOpen");
        }

        if (banner.Cost.Count < 5)
        {
            Log.Error($"gacha banner {banner.Id} has a malformed Cost column");
            return CallGSResult.Error("error.BadParam");
        }

        var pulls = req.Ten ? 10u : 1u;
        var costRow = banner.Cost;
        var need = Math.Max(1u, costRow[4]) * pulls;

        var act = VirCaptureStateHelper.ResolveCurrentAct(player);
        var bagLimit = act?.BagLimit ?? 0u;
        if (bagLimit > 0 && VirCaptureStateHelper.CountMonsterCards(player) + pulls > bagLimit)
        {
            // Same key as the client's own bag pre-check (VirCapture.lua:2260-2265).
            return CallGSResult.Error("vircapture.vircapture_base22");
        }

        if (MikuSB.GameServer.Game.Player.CurrencyService.GetItemCount(player, costRow) < need)
        {
            return CallGSResult.Error("error.gold_not_enough");
        }

        var beforeCount = CurrencyService.GetItemCount(player, costRow);
        if (!CurrencyService.TrySpendItem(player, costRow, need, out _))
        {
            return CallGSResult.Error("error.gold_not_enough");
        }

        var sync = new NtfSyncPlayer();
        SyncCostWallet(player, costRow, sync);

        var awarded = new JsonArray();
        var awardedTemplates = new List<ulong>();
        for (var i = 0u; i < pulls; i++)
        {
            var row = RollRow(player, banner);
            if (row == null || row.Gdpl.Count < 4)
                break;

            var item = await player.InventoryManager.AddMonsterCardItem(
                row.Gdpl[1],
                row.Gdpl[2],
                row.Gdpl[3],
                sendPacket: false);
            if (item == null)
                continue;

            sync.Items.Add(item.ToProto());
            awardedTemplates.Add(item.TemplateId);
            awarded.Add(new JsonArray(
                (int)MonsterCardGenre,
                (int)row.Gdpl[1],
                (int)row.Gdpl[2],
                (int)row.Gdpl[3],
                1));
        }

        if (awarded.Count == 0)
        {
            return CallGSResult.Error("error.BadParam");
        }

        VirCaptureStateHelper.SyncCardCounters(player, awardedTemplates, sync);

        CurrencyService.Persist(player);
        MikuSB.Database.DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);

        var rsp = new JsonObject
        {
            ["tbAwardList"] = awarded
        };
        return CallGSResult.Ok(rsp.ToJsonString(), sync);
    }

    private static VirCaptureGachaExcel? ResolveBanner(PlayerInstance player)
    {
        var now = ServerClock.Now;
        foreach (var banner in GameData.VirCaptureGachaData.Values.OrderBy(x => x.Id))
        {
            var start = VirCaptureStateHelper.ParseConfigTime(banner.StartTime);
            var end = VirCaptureStateHelper.ParseConfigTime(banner.EndTime);
            if (start == null || end == null || now < start || now >= end)
                continue;

            if (!VirCaptureConditionHelper.Check(player, banner.Condition, $"Gacha:{banner.Id}"))
                continue;

            return banner;
        }

        return null;
    }

    private static VirCaptureGachaPoolExcel? RollRow(PlayerInstance player, VirCaptureGachaExcel banner)
    {
        var pool = GameData.VirCaptureGachaPoolData.Where(x => x.Weight > 0 && x.Gdpl.Count >= 4).ToList();
        if (pool.Count == 0)
            return null;

        var unlocked = new List<VirCaptureGachaPoolExcel>();
        var locked = new List<VirCaptureGachaPoolExcel>();
        foreach (var row in pool)
        {
            var template = GameData.MonsterCardData.GetValueOrDefault(
                GameResourceTemplateId.FromGdpl(row.Gdpl[0], row.Gdpl[1], row.Gdpl[2], row.Gdpl[3]));
            var rikiId = template?.RikiId ?? 0u;
            var owned = rikiId > 0 && player.Attributes.GetValue(RikiGroupId, rikiId) > 0;
            (owned ? unlocked : locked).Add(row);
        }

        // UnlockWeight is the chance of drawing a monster the player has not unlocked yet (the
        // "unlock" side of the banner), LockWeight the chance of a duplicate. The client loader reads
        // UnLockRate/LockRate, which the table does not contain (VirCaptureConfig.lua:458-460), so the
        // split only exists server side; treat it as a design decision, not a client contract.
        var preferNew = VirCapturePoolParse.Roll(
            [("unlock", banner.UnlockWeight), ("lock", banner.LockWeight)]) == "unlock";
        var bucket = preferNew ? locked : unlocked;
        if (bucket.Count == 0)
            bucket = preferNew ? unlocked : locked;
        if (bucket.Count == 0)
            bucket = pool;

        return WeightedPick(bucket);
    }

    private static VirCaptureGachaPoolExcel? WeightedPick(List<VirCaptureGachaPoolExcel> rows)
    {
        var total = rows.Aggregate(0u, (sum, row) => sum + row.Weight);
        if (total == 0)
            return rows[0];

        var pick = (uint)Random.Shared.Next(1, (int)Math.Min(int.MaxValue, total + 1));
        foreach (var row in rows)
        {
            if (pick <= row.Weight)
                return row;
            pick -= row.Weight;
        }

        return rows[^1];
    }

    private static void SyncCostWallet(PlayerInstance player, List<uint> costRow, NtfSyncPlayer sync)
    {
        var templateId = GameResourceTemplateId.FromGdpl(costRow[0], costRow[1], costRow[2], costRow[3]);
        var wallet = player.InventoryManager.InventoryData.Items.Values
            .FirstOrDefault(x => x.TemplateId == templateId);
        if (wallet != null)
            sync.Items.Add(wallet.ToProto());
    }
}

public sealed class VirCaptureGachaMonsterParam
{
    [JsonPropertyName("bTen")]
    public bool Ten { get; set; }
}
