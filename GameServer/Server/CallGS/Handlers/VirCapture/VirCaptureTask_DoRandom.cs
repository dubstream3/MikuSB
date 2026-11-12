using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// Rolls one random mission into a free mission slot of the build island.
/// Weight comes from questpool.json at the current island level; missions already on the map,
/// already received or blocked by prerequisites are skipped.
/// </summary>
[CallGSApi("VirCaptureTask_DoRandom")]
public class VirCaptureTask_DoRandom : CallGSHandler<VirCaptureTaskDoRandomParam>
{
    private static readonly Logger Log = new("VirCapture.Task.DoRandom");

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, VirCaptureTaskDoRandomParam req)
    {
        if (req == null || req.MapId <= 0)
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var player = context.Connection.Player!;
        var mapId = (uint)req.MapId;
        if (!VirCaptureTaskState.HostsMissions(mapId) ||
            !GameData.VirCaptureCaptureRegionData.TryGetValue(mapId, out var region))
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var mapLevel = VirCaptureTaskState.GetMapLevel(player, mapId);
        if (mapLevel <= 0)
        {
            return Task.FromResult(CallGSResult.Error("error.condition_limit"));
        }

        var maxPos = Math.Min(region.MaxRandomQuest, (uint)VirCaptureTaskState.MaxSlots);
        var pos = VirCaptureTaskState.FindFreeSlot(player, mapId, 2, maxPos);
        if (pos == 0)
        {
            return Task.FromResult(CallGSResult.Error("error.condition_limit"));
        }

        var occupied = new HashSet<uint>();
        for (var i = 1u; i <= VirCaptureTaskState.MaxSlots; i++)
        {
            var taskId = VirCaptureTaskState.GetSlotTask(player, mapId, i);
            if (taskId > 0)
                occupied.Add(taskId);
        }

        var candidates = VirCaptureTaskState.GetPoolCandidates(region.QuestPoolId, mapId, mapLevel)
            .Where(candidate =>
            {
                if (occupied.Contains(candidate.MissionId))
                    return false;

                var cfg = VirCaptureTaskState.GetTask(candidate.MissionId);
                if (cfg == null)
                    return false;

                return !VirCaptureTaskState.IsReceived(player, cfg.Id) && VirCaptureTaskState.PreReceived(player, cfg);
            })
            .ToList();

        if (candidates.Count == 0)
        {
            Log.Info($"map {mapId} level {mapLevel} pool {region.QuestPoolId}: no random mission candidate");
            return Task.FromResult(CallGSResult.Error("error.condition_limit"));
        }

        var totalWeight = candidates.Sum(c => c.GetWeight(mapLevel));
        var roll = Random.Shared.Next(1, (int)Math.Max(1u, totalWeight) + 1);
        var picked = candidates[^1];
        var acc = 0u;
        foreach (var candidate in candidates)
        {
            acc += candidate.GetWeight(mapLevel);
            if (roll <= acc)
            {
                picked = candidate;
                break;
            }
        }

        var sync = new NtfSyncPlayer();
        VirCaptureTaskState.SetSlotTask(player, mapId, pos, picked.MissionId, sync);
        DatabaseHelper.SaveDatabaseType(player.Data);

        Log.Info($"map {mapId} level {mapLevel}: slot {pos} -> mission {picked.MissionId}");
        var rsp = new JsonObject
        {
            ["nMapId"] = mapId,
            ["nPos"] = pos,
            ["nTaskId"] = picked.MissionId
        };
        return Task.FromResult(CallGSResult.Ok(rsp.ToJsonString(), sync));
    }
}

public sealed class VirCaptureTaskDoRandomParam
{
    [JsonPropertyName("nMapId")] public int MapId { get; set; }
}
