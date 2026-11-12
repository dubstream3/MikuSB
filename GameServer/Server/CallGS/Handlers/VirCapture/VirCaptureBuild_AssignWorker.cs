using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// Puts a staff member on a building worker slot, takes it off, or moves it there.
/// The slot is stored in gid 161 (worker 1..5) and the building id in gid 162, so both sides of
/// the relation stay consistent; a staff member can only stand on one building at a time.
/// <c>nStaffState</c>: 1 = assign, 2 = remove, 3 = move from another building.
/// </summary>
[CallGSApi("VirCaptureBuild_AssignWorker")]
public class VirCaptureBuild_AssignWorker : CallGSHandler<VirCaptureBuildAssignWorkerParam>
{
    private static readonly Logger Log = new("VirCapture.Build.AssignWorker");

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, VirCaptureBuildAssignWorkerParam req)
    {
        if (req == null || req.Id <= 0 || req.StaffId <= 0 || req.StaffState is < 1 or > 3)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var player = context.Connection.Player!;
        var buildId = (uint)req.Id;
        var staffId = (uint)req.StaffId;
        if (VirCaptureBuildState.GetBuilding(buildId) == null ||
            VirCaptureStaffState.Config(staffId) == null ||
            VirCaptureStaffState.Level(player, staffId) <= 0)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var sync = new NtfSyncPlayer();
        if (req.StaffState == 2)
        {
            VirCaptureStaffState.Detach(player, staffId, sync);
            DatabaseHelper.SaveDatabaseType(player.Data);
            Log.Info($"staff {staffId} removed from building {buildId}");
            return Task.FromResult(CallGSResult.Ok(new JsonObject
            {
                ["nId"] = (int)buildId,
                ["nStaffId"] = (int)staffId
            }.ToJsonString(), sync));
        }

        var current = VirCaptureStaffState.WorkBuilding(player, staffId);
        if (current == buildId)
        {
            return Task.FromResult(CallGSResult.Ok(new JsonObject
            {
                ["nId"] = (int)buildId,
                ["nStaffId"] = (int)staffId
            }.ToJsonString(), sync));
        }

        if (current != 0)
        {
            if (req.StaffState != 3)
                return Task.FromResult(CallGSResult.Error("error.condition_limit"));
            VirCaptureStaffState.Detach(player, staffId, sync);
        }

        var slot = VirCaptureStaffState.FindFreeSlot(player, buildId);
        if (slot == 0)
            return Task.FromResult(CallGSResult.Error("error.condition_limit", sync));

        VirCaptureBuildState.SetBuildValue(player, buildId,
            VirCaptureBuildState.OffWorker1 + slot - 1, staffId, sync);
        VirCaptureStaffState.Set(player, staffId, VirCaptureStaffState.OffWorkBuilding, buildId, sync);
        DatabaseHelper.SaveDatabaseType(player.Data);

        Log.Info($"staff {staffId} assigned to building {buildId} slot {slot} (state {req.StaffState})");
        return Task.FromResult(CallGSResult.Ok(new JsonObject
        {
            ["nId"] = (int)buildId,
            ["nStaffId"] = (int)staffId,
            ["nSlot"] = (int)slot
        }.ToJsonString(), sync));
    }
}

public class VirCaptureBuildAssignWorkerParam
{
    [JsonPropertyName("nId")] public int Id { get; set; }
    [JsonPropertyName("nStaffId")] public int StaffId { get; set; }
    [JsonPropertyName("nStaffState")] public int StaffState { get; set; }
}
