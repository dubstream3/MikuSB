using MikuSB.Database;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Enums.Item;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using Newtonsoft.Json.Linq;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

[CallGSApi("VirCaptureLevel_SaveCapture")]
public class VirCaptureLevel_SaveCapture : CallGSHandler<VirCaptureSaveCaptureParam>
{
    private const uint VirCaptureGroupId = AttrIds.VirCapture.Gid;
    private const uint CurExpSid = AttrIds.VirCapture.CurrentExpSid;
    private const uint CurLevelSid = AttrIds.VirCapture.CurrentLevelSid;
    private const uint BagNumSid = AttrIds.VirCapture.BagNumSid;
    private const uint DailyExpSid = AttrIds.VirCapture.DailyExpSid;
    private const uint DailyExpDateSid = AttrIds.VirCapture.DailyExpDateSid;
    private const uint ColorMaxStartSid = AttrIds.VirCapture.ColorMaxStartSid;
    private const uint RikiGroupId = AttrIds.VirCapture.RikiGid;

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, VirCaptureSaveCaptureParam req)
    {

        if (req == null || req.LevelId == 0 || req.RegionId == 0)
        {
            return CallGSResult.Error("error.BadParam");
        }

        var player = context.Connection.Player!;
        var sync = new NtfSyncPlayer();

        if (!GameData.VirCaptureCaptureRegionData.TryGetValue((uint)req.LevelId, out var captureRegion))
        {
            return CallGSResult.Error("error.BadParam");
        }

        var rewardGdpl = VirCaptureCaptureRewardResolver.ResolveGdpl(captureRegion, (uint)req.RegionId);
        if (rewardGdpl == null || rewardGdpl.Count < 4 || rewardGdpl[0] != (uint)ItemTypeEnum.TYPE_MONSTER_CARD)
        {
            return CallGSResult.Error("error.BadParam", sync);
        }

        var act = ResolveCurrentAct(player);
        if (!CheckBagLimit(player, act))
        {
            // Same key the client uses for its local pre-check (VirCapture.lua:2263).
            return CallGSResult.Error("vircapture.vircapture_base22", sync);
        }

        var rewardColor = ResolveRewardColor(rewardGdpl);
        if (IsColorLimitReached(player, act, rewardColor))
        {
            var colorError = new JsonObject
            {
                ["sErr"] = "error.condition_limit",
                ["nColor"] = rewardColor
            };
            return CallGSResult.Ok(colorError.ToJsonString(), sync);
        }

        VirCaptureStateHelper.SetPointState(player, (uint)req.LevelId, (uint)req.RegionId, 2u, sync);

        var grantedItem = await player.InventoryManager.AddMonsterCardItem(
            rewardGdpl[1],
            rewardGdpl[2],
            rewardGdpl[3],
            sendPacket: false);
        if (grantedItem == null)
        {
            return CallGSResult.Error("error.BadParam", sync);
        }

        sync.Items.Add(grantedItem.ToProto());
        SyncVirCaptureCounters(player, grantedItem.TemplateId, sync);
        ApplyCaptureExp(player, grantedItem.TemplateId, sync);

        DatabaseHelper.SaveDatabaseType(player.Data);
        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);

        var response = new JsonObject
        {
            ["nLevelID"] = req.LevelId,
            ["nRegionId"] = req.RegionId,
            ["nAddItemId"] = grantedItem.UniqueId,
            ["tbGDPL"] = new JsonArray(rewardGdpl.Select(x => JsonValue.Create((int)x)).ToArray())
        };

        return CallGSResult.Ok(response.ToJsonString(), sync);
    }

    private static void SyncVirCaptureCounters(MikuSB.GameServer.Game.Player.PlayerInstance player, ulong templateId, NtfSyncPlayer sync)
    {
        var bagCount = (uint)player.InventoryManager.InventoryData.Items.Values.Count(x => x.ItemType == ItemTypeEnum.TYPE_MONSTER_CARD);
        VirCaptureStateHelper.SetUnsignedAttr(player, BagNumSid, bagCount, sync);

        if (!GameData.MonsterCardData.TryGetValue(templateId, out var monsterCard) || monsterCard.RikiId == 0)
            return;

        var colorSid = ColorMaxStartSid + Math.Max(0u, monsterCard.Color - 1u);
        var nextColorValue = player.Attributes.GetValue(VirCaptureGroupId, colorSid) + 1;
        VirCaptureStateHelper.SetUnsignedAttr(player, colorSid, nextColorValue, sync);

        var rikiAttr = player.Attributes.GetOrCreate(RikiGroupId, monsterCard.RikiId);
        rikiAttr.Val += 1;
        player.Attributes.SyncTo(sync, rikiAttr);
    }

    private static void ApplyCaptureExp(MikuSB.GameServer.Game.Player.PlayerInstance player, ulong templateId, NtfSyncPlayer sync)
    {
        if (!GameData.MonsterCardData.TryGetValue(templateId, out var monsterCard) || monsterCard.Exp == 0)
            return;

        EnsureDailyExpReset(player, sync);

        var curLevelAttr = player.Attributes.GetOrCreate(VirCaptureGroupId, CurLevelSid);
        var curExpAttr = player.Attributes.GetOrCreate(VirCaptureGroupId, CurExpSid);
        var dailyExpAttr = player.Attributes.GetOrCreate(VirCaptureGroupId, DailyExpSid);

        var maxLevel = GameData.VirCaptureLevelListData.Count == 0 ? 1u : GameData.VirCaptureLevelListData.Keys.Max();
        var curLevel = Math.Max(1u, curLevelAttr.Val);
        if (curLevel >= maxLevel)
            return;

        var baseExp = monsterCard.Exp;
        if (GameData.VirCaptureLevelListData.TryGetValue(curLevel, out var currentLevelCfg) && currentLevelCfg.ExpUp > 1d)
            baseExp = (uint)Math.Floor(baseExp * currentLevelCfg.ExpUp);

        var maxDailyExp = ResolveCurrentAct(player)?.MaxExp ?? 0u;
        if (maxDailyExp > 0 && dailyExpAttr.Val >= maxDailyExp)
            return;

        var gainExp = baseExp;
        if (maxDailyExp > 0)
            gainExp = Math.Min(gainExp, maxDailyExp - dailyExpAttr.Val);

        if (gainExp == 0)
            return;

        dailyExpAttr.Val += gainExp;
        SyncVirCaptureAttr(player, DailyExpSid, dailyExpAttr.Val, sync);

        var pendingExp = curExpAttr.Val + gainExp;
        while (GameData.VirCaptureLevelListData.TryGetValue(curLevel, out var levelCfg) && curLevel < maxLevel)
        {
            if (pendingExp < levelCfg.Exp)
                break;

            pendingExp -= levelCfg.Exp;
            curLevel++;
        }

        curLevelAttr.Val = curLevel;
        curExpAttr.Val = curLevel >= maxLevel
            ? GameData.VirCaptureLevelListData.GetValueOrDefault(maxLevel)?.Exp ?? pendingExp
            : pendingExp;

        SyncVirCaptureAttr(player, CurLevelSid, curLevelAttr.Val, sync);
        SyncVirCaptureAttr(player, CurExpSid, curExpAttr.Val, sync);
    }

    /// <summary>
    /// (128,8) is a *daily* exp pool, but nothing reset it: it only ever grew, so a player hit the
    /// 144000 act cap once and then stopped earning forever. (128,10) stores the yyyyMMdd of the last reset.
    /// </summary>
    private static void EnsureDailyExpReset(MikuSB.GameServer.Game.Player.PlayerInstance player, NtfSyncPlayer sync)
    {
        var now = ServerClock.Now;
        var today = (uint)(now.Year * 10000 + now.Month * 100 + now.Day);
        if (player.Attributes.GetValue(VirCaptureGroupId, DailyExpDateSid) == today)
            return;

        VirCaptureStateHelper.SetUnsignedAttr(player, DailyExpDateSid, today, sync);
        VirCaptureStateHelper.SetUnsignedAttr(player, DailyExpSid, 0, sync);
    }

    private static uint CountMonsterCards(MikuSB.GameServer.Game.Player.PlayerInstance player) =>
        (uint)player.InventoryManager.InventoryData.Items.Values.Count(x => x.ItemType == ItemTypeEnum.TYPE_MONSTER_CARD);

    private static bool CheckBagLimit(MikuSB.GameServer.Game.Player.PlayerInstance player, VirCaptureTimeExcel? act)
    {
        if (act == null || act.BagLimit == 0)
            return true;

        return CountMonsterCards(player) < act.BagLimit;
    }

    private static uint ResolveRewardColor(IReadOnlyList<uint> gdpl)
    {
        var templateId = GameResourceTemplateId.FromGdpl(gdpl);
        return GameData.MonsterCardData.TryGetValue(templateId, out var card) ? card.Color : 0u;
    }

    private static bool IsColorLimitReached(MikuSB.GameServer.Game.Player.PlayerInstance player, VirCaptureTimeExcel? act, uint color)
    {
        if (act == null || color == 0)
            return false;

        var limit = ResolveColorLimit(act, color);
        if (limit == 0)
            return false;

        var current = player.Attributes.GetValue(VirCaptureGroupId, ColorMaxStartSid + (color - 1u));
        return current >= limit;
    }

    private static uint ResolveColorLimit(VirCaptureTimeExcel act, uint color)
    {
        if (act.ColorLimit is not JObject limits)
            return 0;

        var token = limits[color.ToString(CultureInfo.InvariantCulture)];
        if (token == null || token.Type == JTokenType.Null)
            return 0;

        return token.Type == JTokenType.Integer
            ? token.Value<uint>()
            : uint.TryParse(token.ToString(), out var parsed) ? parsed : 0u;
    }

    private static void SyncVirCaptureAttr(MikuSB.GameServer.Game.Player.PlayerInstance player, uint sid, uint value, NtfSyncPlayer sync)
    {
        player.Attributes.SyncTo(sync, VirCaptureGroupId, sid, value);
    }

    private static MikuSB.Data.Excel.VirCaptureTimeExcel? ResolveCurrentAct(MikuSB.GameServer.Game.Player.PlayerInstance player)
    {
        var actId = player.Attributes.GetValue(VirCaptureGroupId, 1);
        if (actId > 0 && GameData.VirCaptureTimeData.TryGetValue(actId, out var act))
            return act;

        var now = ServerClock.Now;
        return GameData.VirCaptureTimeData.Values
            .Select(x => new { Config = x, Start = ParseConfigTime(x.StartTime), End = ParseConfigTime(x.EndTime) })
            .Where(x => x.Start.HasValue && x.End.HasValue && x.Start <= now && now < x.End)
            .OrderBy(x => x.Start)
            .Select(x => x.Config)
            .FirstOrDefault();
    }

    private static DateTime? ParseConfigTime(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var normalized = raw.Trim().Trim('[', ']');
        if (normalized.Length != 12)
            return null;

        return DateTime.TryParseExact(
            normalized,
            "yyyyMMddHHmm",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out var value)
            ? value
            : null;
    }
}

public sealed class VirCaptureSaveCaptureParam
{
    [JsonPropertyName("nLevelID")]
    public int LevelId { get; set; }

    [JsonPropertyName("nRegionId")]
    public int RegionId { get; set; }
}
