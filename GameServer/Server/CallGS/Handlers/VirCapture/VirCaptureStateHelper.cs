using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

using MikuSB.Data;

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

}
