using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// Attribute layout of the island staff layer (gid 162): staff <c>id</c> owns
/// <c>100 + id*20 + offset</c> with level / flag / work building.
/// </summary>
internal static class VirCaptureStaffState
{
    public const uint GroupId = AttrIds.VirCapture.StaffGroupId;
    public const uint DataStart = 100;
    public const uint DataCount = 20;

    public const uint OffLevel = 1;
    public const uint OffFlag = 2;
    public const uint OffWorkBuilding = 3;

    public static uint Sid(uint staffId, uint offset) => DataStart + staffId * DataCount + offset;

    public static uint Get(PlayerInstance player, uint staffId, uint offset) =>
        player.Attributes.GetValue(GroupId, Sid(staffId, offset));

    public static void Set(PlayerInstance player, uint staffId, uint offset, uint value, NtfSyncPlayer sync)
    {
        var attr = player.Attributes.GetOrCreate(GroupId, Sid(staffId, offset));
        attr.Val = value;
        player.Attributes.SyncTo(sync, attr);
    }

    public static uint Level(PlayerInstance player, uint staffId) => Get(player, staffId, OffLevel);

    public static uint WorkBuilding(PlayerInstance player, uint staffId) => Get(player, staffId, OffWorkBuilding);

    public static VirCaptureStaffExcel? Config(uint staffId) =>
        GameData.VirCaptureStaffData.TryGetValue(staffId, out var cfg) ? cfg : null;

    /// <summary>Finds the worker slot of <paramref name="buildId"/> that holds <paramref name="staffId"/>.</summary>
    public static uint FindSlot(PlayerInstance player, uint buildId, uint staffId)
    {
        for (var slot = 1u; slot <= VirCaptureBuildState.MaxWorker; slot++)
        {
            if (VirCaptureBuildState.Worker(player, buildId, slot) == staffId)
                return slot;
        }

        return 0;
    }

    public static uint FindFreeSlot(PlayerInstance player, uint buildId)
    {
        for (var slot = 1u; slot <= VirCaptureBuildState.MaxWorker; slot++)
        {
            if (VirCaptureBuildState.Worker(player, buildId, slot) == 0)
                return slot;
        }

        return 0;
    }

    /// <summary>Removes the staff from whatever building currently holds it.</summary>
    public static void Detach(PlayerInstance player, uint staffId, NtfSyncPlayer sync)
    {
        var current = WorkBuilding(player, staffId);
        if (current == 0)
            return;

        var slot = FindSlot(player, current, staffId);
        if (slot != 0)
            VirCaptureBuildState.SetBuildValue(player, current, VirCaptureBuildState.OffWorker1 + slot - 1, 0, sync);
        Set(player, staffId, OffWorkBuilding, 0, sync);
    }

    public static uint CountAssigned(PlayerInstance player, uint buildId)
    {
        var count = 0u;
        for (var slot = 1u; slot <= VirCaptureBuildState.MaxWorker; slot++)
        {
            if (VirCaptureBuildState.Worker(player, buildId, slot) > 0)
                count++;
        }

        return count;
    }
}
