using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using Newtonsoft.Json.Linq;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// Blueprint island mission state. Mission progress lives in the VirCapture task attribute
/// group (gid 163): sid = mission id, bit0 = reward received, bits 1..31 = progress. Random
/// mission slots store a mission id in sid 10 + mapIndex * 10 + (pos - 1); pos 1 is reserved
/// for the cost mission (client VirCaptureTask.RANDOM_POS_COST).
/// </summary>
internal static class VirCaptureTaskState
{
    public const uint GroupId = AttrIds.VirCapture.TaskGroupId;
    public const uint RandomIdStart = 10;
    public const uint RandomIdLen = 10;
    public const uint RandomPosCost = 1;
    public const int MaxSlots = 10;

    private const uint ProgressMask = 0x7FFFFFFFu;

    private static readonly Logger Log = new("VirCapture.Task");

    /// <summary>Client keeps a map index table for the mission slots, the build island is index 0.</summary>
    private static readonly Dictionary<uint, int> MapIndex = new() { [326] = 0 };

    public static bool HostsMissions(uint mapId) => MapIndex.ContainsKey(mapId);

    public static uint GetRaw(PlayerInstance player, uint taskId) =>
        player.Attributes.GetValue(GroupId, taskId);

    public static uint GetProgress(PlayerInstance player, uint taskId) =>
        (GetRaw(player, taskId) >> 1) & ProgressMask;

    public static bool IsReceived(PlayerInstance player, uint taskId) =>
        (GetRaw(player, taskId) & 1u) != 0;

    public static bool IsFinished(PlayerInstance player, VirCaptureTaskExcel cfg) =>
        GetProgress(player, cfg.Id) >= cfg.CheckValue;

    public static void SetProgress(PlayerInstance player, uint taskId, uint progress, NtfSyncPlayer sync)
    {
        var attr = player.Attributes.GetOrCreate(GroupId, taskId);
        var received = attr.Val & 1u;
        attr.Val = received | ((Math.Min(progress, ProgressMask)) << 1);
        player.Attributes.SyncTo(sync, attr);
    }

    public static uint AddProgress(PlayerInstance player, uint taskId, uint delta, uint cap, NtfSyncPlayer sync)
    {
        var current = GetProgress(player, taskId);
        var next = Math.Min(current + Math.Max(1u, delta), cap);
        if (next != current)
            SetProgress(player, taskId, next, sync);

        return next;
    }

    public static void MarkReceived(PlayerInstance player, uint taskId, NtfSyncPlayer sync)
    {
        var attr = player.Attributes.GetOrCreate(GroupId, taskId);
        attr.Val |= 1u;
        player.Attributes.SyncTo(sync, attr);
    }

    public static void Clear(PlayerInstance player, uint taskId, NtfSyncPlayer sync)
    {
        if (player.Attributes.Remove(GroupId, taskId))
            player.Attributes.SyncTo(sync, GroupId, taskId, 0);
    }

    public static uint GetSlotSid(uint mapId, uint pos)
    {
        if (!MapIndex.TryGetValue(mapId, out var index) || pos == 0)
            return 0;

        return RandomIdStart + (uint)index * RandomIdLen + (pos - 1);
    }

    public static uint GetSlotTask(PlayerInstance player, uint mapId, uint pos)
    {
        var sid = GetSlotSid(mapId, pos);
        return sid == 0 ? 0 : player.Attributes.GetValue(GroupId, sid);
    }

    public static void SetSlotTask(PlayerInstance player, uint mapId, uint pos, uint taskId, NtfSyncPlayer sync)
    {
        var sid = GetSlotSid(mapId, pos);
        if (sid == 0)
            return;

        var attr = player.Attributes.GetOrCreate(GroupId, sid);
        attr.Val = taskId;
        player.Attributes.SyncTo(sync, attr);
    }

    /// <summary>Finds the random slot position currently holding <paramref name="taskId"/>.</summary>
    public static uint FindSlotOfTask(PlayerInstance player, uint mapId, uint taskId)
    {
        for (var pos = 1u; pos <= MaxSlots; pos++)
        {
            if (GetSlotTask(player, mapId, pos) == taskId)
                return pos;
        }

        return 0;
    }

    /// <summary>First empty random slot at or after <paramref name="fromPos"/>, 0 when full.</summary>
    public static uint FindFreeSlot(PlayerInstance player, uint mapId, uint fromPos, uint maxPos)
    {
        var last = Math.Min(maxPos, (uint)MaxSlots);
        for (var pos = Math.Max(1u, fromPos); pos <= last; pos++)
        {
            if (GetSlotTask(player, mapId, pos) == 0)
                return pos;
        }

        return 0;
    }

    public static VirCaptureTaskExcel? GetTask(uint taskId) =>
        GameData.VirCaptureTaskData.TryGetValue(taskId, out var cfg) ? cfg : null;

    /// <summary>Every prerequisite mission must have been received.</summary>
    public static bool PreReceived(PlayerInstance player, VirCaptureTaskExcel cfg)
    {
        if (cfg.PreId is not JArray preIds)
            return true;

        foreach (var token in preIds)
        {
            var preId = VirCaptureRewardHelper.ToUInt(token);
            if (preId == 0)
                continue;

            if (!IsReceived(player, preId))
            {
                Log.Info($"task {cfg.Id} blocked: prerequisite {preId} not received");
                return false;
            }
        }

        return true;
    }

    public static List<VirCaptureTaskExcel> GetTasksByType(uint type) =>
        GameData.VirCaptureTaskData.Values.Where(t => t.Type == type).OrderBy(t => t.Id).ToList();

    /// <summary>Weighted random candidate list for the map / pool at the given island level.</summary>
    public static List<VirCaptureQuestPoolExcel> GetPoolCandidates(uint poolId, uint mapId, uint mapLevel) =>
        GameData.VirCaptureQuestPoolData
            .Where(p => p.PoolId == poolId && p.MapId == mapId && p.GetWeight(mapLevel) > 0)
            .ToList();

    public static uint GetMapLevel(PlayerInstance player, uint mapId) =>
        VirCaptureStateHelper.GetMapOffset(player, mapId, VirCaptureStateHelper.OffMapLevel);
}
