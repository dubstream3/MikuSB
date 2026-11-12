using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// Buys one staff member for the blueprint island and writes level 1 into gid 162.
/// Recruitment is a directed purchase (not a draw): the offer comes from recruitment_staff.json
/// and costs <c>Money</c> in the island coin of the offer's map.
/// </summary>
[CallGSApi("VirCaptureStaff_Recruit")]
public class VirCaptureStaff_Recruit : CallGSHandler<VirCaptureStaffRecruitParam>
{
    private static readonly Logger Log = new("VirCapture.Staff.Recruit");

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, VirCaptureStaffRecruitParam req)
    {
        if (req == null || req.StaffId <= 0)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var player = context.Connection.Player!;
        var staffId = (uint)req.StaffId;
        if (VirCaptureStaffState.Config(staffId) == null ||
            !GameData.VirCaptureRecruitData.TryGetValue(staffId, out var offer))
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        if (VirCaptureStaffState.Level(player, staffId) > 0)
            return Task.FromResult(CallGSResult.Error("error.condition_limit"));

        if (!GameData.VirCaptureOperatingData.TryGetValue(offer.MapId, out var operate))
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        if (VirCaptureBuildState.GetMapLevel(player, offer.MapId) <= 0)
            return Task.FromResult(CallGSResult.Error("error.condition_limit"));

        var sync = new NtfSyncPlayer();
        if (!VirCaptureBuildState.TryPay(player, operate.CashId, offer.Money, sync, out var error))
        {
            Log.Info($"staff {staffId} recruit denied: {error} (need {offer.Money})");
            return Task.FromResult(CallGSResult.Error(error, sync));
        }

        VirCaptureStaffState.Set(player, staffId, VirCaptureStaffState.OffLevel, 1, sync);
        DatabaseHelper.SaveDatabaseType(player.Data);
        Log.Info($"staff {staffId} recruited on map {offer.MapId} for {offer.Money}");
        return Task.FromResult(CallGSResult.Ok(new JsonObject { ["nStaffId"] = (int)staffId }.ToJsonString(), sync));
    }
}

public class VirCaptureStaffRecruitParam
{
    [JsonPropertyName("nStaffId")] public int StaffId { get; set; }
}
