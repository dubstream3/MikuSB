using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

using MikuSB.Data;
using MikuSB.Data.Excel;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

internal static class VirCaptureStateHelper
{
    public const uint GroupId = AttrIds.VirCapture.Gid;
    public const uint MapDataStart = AttrIds.VirCapture.MapDataStartSid;
    public const uint MapDataEnd = 19000;
    public const uint MaxMapCount = 3;
    public const uint MaxMapDataLen = AttrIds.VirCapture.MaxMapDataLength;
    public const uint MaxPatrolPoint = 500;
    public const uint MaxOtherPoint = 2500;
    public const uint MinMaterialId = 50000;
    public const uint MaxMaterialId = 51500;

    public const uint OffMapId = 1;
    public const uint OffTurnNum = 2;
    public const uint OffPosX = 3;
    public const uint OffPosY = 4;
    public const uint OffPosZ = 5;
    public const uint OffToward = 6;
    public const uint OffDayNight = 7;
    public const uint OffMapLevel = 8;
    public const uint OffPatrolStart = 51;
    public const uint OffPatrolEnd = 1000;
    public const uint OffOtherStart = 1001;
    public const uint OffOtherEnd = 1500;
    public const uint OffMaterialStart = 1501;
    public const uint OffMaterialEnd = 3000;

    public static uint FindOrAllocateMapSlot(PlayerInstance player, uint levelId)
    {
        uint? emptySlot = null;
        for (uint i = 0; i < MaxMapCount; i++)
        {
            var slotStart = MapDataStart + (i * MaxMapDataLen);
            var mapIdAttr = player.Attributes.Get(GroupId, slotStart + OffMapId);
            if (mapIdAttr?.Val == levelId)
                return slotStart;

            if (emptySlot == null && (mapIdAttr == null || mapIdAttr.Val == 0))
                emptySlot = slotStart;
        }

        return emptySlot ?? 0;
    }

    public static uint FindMapSlot(PlayerInstance player, uint levelId)
    {
        for (uint i = 0; i < MaxMapCount; i++)
        {
            var slotStart = MapDataStart + (i * MaxMapDataLen);
            if (player.Attributes.Get(GroupId, slotStart + OffMapId)?.Val == levelId)
                return slotStart;
        }

        return 0;
    }

    /// <summary>Read-only access to a map-block offset (e.g. OffDayNight). Returns 0 when the map has no slot.</summary>
    public static uint GetMapOffset(PlayerInstance player, uint levelId, uint offset)
    {
        var slotStart = FindMapSlot(player, levelId);
        return slotStart == 0 ? 0 : player.Attributes.GetValue(GroupId, slotStart + offset);
    }

    /// <summary>Read-only mirror of <see cref="SetPointState"/> (condition 23 needs "has this point been captured").</summary>
    public static uint GetPointState(PlayerInstance player, uint levelId, uint pointId)
    {
        var slotStart = FindMapSlot(player, levelId);
        if (slotStart == 0 || pointId == 0)
            return 0;

        if (pointId <= MaxPatrolPoint)
            return player.Attributes.GetValue(GroupId, slotStart + (OffPatrolStart - 1) + pointId);

        if (pointId <= MaxOtherPoint)
        {
            var relative = pointId - MaxPatrolPoint;
            var sid = slotStart + (uint)Math.Floor(relative / 30d) + OffOtherStart;
            if (sid > slotStart + OffOtherEnd)
                return 0;

            var bit = (int)(relative % 30);
            return (player.Attributes.GetValue(GroupId, sid) >> bit) & 1u;
        }

        if (pointId > MinMaterialId && pointId <= MaxMaterialId)
        {
            var sid = slotStart + (OffMaterialStart - 1) + (pointId - MinMaterialId);
            if (sid >= slotStart + OffMaterialEnd)
                return 0;

            return player.Attributes.GetValue(GroupId, sid);
        }

        return 0;
    }

    public static void EnsureBaseMapState(PlayerInstance player, uint levelId, NtfSyncPlayer sync)
    {
        var slotStart = FindOrAllocateMapSlot(player, levelId);
        if (slotStart == 0)
            return;

        EnsureUnsignedAttr(player, slotStart + OffMapId, levelId, sync);
        EnsureUnsignedAttr(player, slotStart + OffDayNight, 1, sync);
        EnsureUnsignedAttr(player, slotStart + OffMapLevel, 1, sync);
    }

    public static void SetSignedMapOffset(PlayerInstance player, uint levelId, uint offset, int value, NtfSyncPlayer sync)
    {
        var slotStart = FindOrAllocateMapSlot(player, levelId);
        if (slotStart == 0)
            return;

        EnsureBaseMapState(player, levelId, sync);
        SetUnsignedAttr(player, slotStart + offset, unchecked((uint)value), sync);
    }

    public static void SetPointState(PlayerInstance player, uint levelId, uint pointId, uint value, NtfSyncPlayer sync)
    {
        var slotStart = FindOrAllocateMapSlot(player, levelId);
        if (slotStart == 0 || pointId == 0)
            return;

        EnsureBaseMapState(player, levelId, sync);

        if (pointId <= MaxPatrolPoint)
        {
            var sid = slotStart + (OffPatrolStart - 1) + pointId;
            SetUnsignedAttr(player, sid, value, sync);
            return;
        }

        if (pointId <= MaxOtherPoint)
        {
            var relative = pointId - MaxPatrolPoint;
            var sid = slotStart + (uint)Math.Floor(relative / 30d) + OffOtherStart;
            if (sid > slotStart + OffOtherEnd)
                return;

            var bit = (int)(relative % 30);
            var attr = player.Attributes.GetOrCreate(GroupId, sid);
            var next = value > 0
                ? attr.Val | (1u << bit)
                : attr.Val & ~(1u << bit);
            if (next != attr.Val)
            {
                attr.Val = next;
                player.Attributes.SyncTo(sync, attr);
            }
            return;
        }

        if (pointId > MinMaterialId && pointId <= MaxMaterialId)
        {
            var sid = slotStart + (OffMaterialStart - 1) + (pointId - MinMaterialId);
            if (sid >= slotStart + OffMaterialEnd)
                return;

            SetUnsignedAttr(player, sid, value, sync);
        }
    }

    public static void EnsureUnsignedAttr(PlayerInstance player, uint sid, uint minValue, NtfSyncPlayer sync)
    {
        var attr = player.Attributes.GetOrCreate(GroupId, sid);
        if (attr.Val < minValue)
        {
            attr.Val = minValue;
            player.Attributes.SyncTo(sync, attr);
        }
    }

    public static void SetUnsignedAttr(PlayerInstance player, uint sid, uint value, NtfSyncPlayer sync)
    {
        var attr = player.Attributes.GetOrCreate(GroupId, sid);
        if (attr.Val != value)
        {
            attr.Val = value;
            player.Attributes.SyncTo(sync, attr);
        }
    }

    /// <summary>How many monster cards the player owns - the number the bag limit is checked on.</summary>
    /// <summary>
    /// Refreshes the counters every card acquisition touches: bag size (gid 128 sid 5), the per-quality
    /// maximum (sid 11 + color - 1) and the per-monster Riki counter (gid 135), which the codex reads to
    /// decide which rikiawardlist tiers are claimable.
    /// </summary>
    public static void SyncCardCounters(PlayerInstance player, IEnumerable<ulong> templateIds, NtfSyncPlayer sync)
    {
        var bagCount = CountMonsterCards(player);
        SetUnsignedAttr(player, AttrIds.VirCapture.BagNumSid, bagCount, sync);

        foreach (var templateId in templateIds)
        {
            if (!GameData.MonsterCardData.TryGetValue(templateId, out var monsterCard) || monsterCard.RikiId == 0)
                continue;

            var colorSid = AttrIds.VirCapture.ColorMaxStartSid + Math.Max(0u, monsterCard.Color - 1u);
            var nextColorValue = player.Attributes.GetValue(GroupId, colorSid) + 1;
            SetUnsignedAttr(player, colorSid, nextColorValue, sync);

            var rikiAttr = player.Attributes.GetOrCreate(AttrIds.VirCapture.RikiGid, monsterCard.RikiId);
            rikiAttr.Val += 1;
            player.Attributes.SyncTo(sync, rikiAttr);
        }
    }

    public static uint CountMonsterCards(PlayerInstance player) =>
        (uint)player.InventoryManager.InventoryData.Items.Values
            .Count(x => x.ItemType == MikuSB.Enums.Item.ItemTypeEnum.TYPE_MONSTER_CARD);

    /// <summary>
    /// Distinct monsters unlocked in the capture book: gid 135 has one entry per RikiId and the
    /// client counts the entries above zero (VirCapture.lua:455-470).
    /// </summary>
    public static uint CountUnlockedRiki(PlayerInstance player) =>
        (uint)player.Attributes.All.Count(x =>
            x.Gid == AttrIds.VirCapture.RikiGid && x.Val > 0);

    /// <summary>
    /// The activity row (dlc/vircapture/timelist.json) the player is currently in: the act id from
    /// gid 128 sid 1 wins, otherwise the latest row whose window is open under the shared era rule
    /// (<see cref="VirCaptureEraTime"/>): a period that closed inside the served DLC era stays
    /// current while that era is pinned, exactly like the DLC module's own windows.
    /// </summary>
    public static VirCaptureTimeExcel? ResolveCurrentAct(PlayerInstance player)
    {
        var actId = player.Attributes.GetValue(GroupId, AttrIds.VirCapture.ActivitySid);
        if (actId > 0 && GameData.VirCaptureTimeData.TryGetValue(actId, out var act))
            return act;

        return VirCaptureEraTime.ResolveCurrent(
            GameData.VirCaptureTimeData.Values, x => x.StartTime, x => x.EndTime);
    }

    /// <summary>Parses a "[202407111000]" style config timestamp.</summary>
    public static DateTime? ParseConfigTime(string? raw)
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
