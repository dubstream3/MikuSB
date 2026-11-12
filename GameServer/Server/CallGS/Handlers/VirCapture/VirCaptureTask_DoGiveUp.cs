using MikuSB.Database;
using MikuSB.Proto;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// Drops a random mission from the build island. The mission slot and its 163 progress row are
/// cleared so the same mission can be rolled again later.
/// </summary>
[CallGSApi("VirCaptureTask_DoGiveUp")]
public class VirCaptureTask_DoGiveUp : CallGSHandler<VirCaptureTaskDoGiveUpParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, VirCaptureTaskDoGiveUpParam req)
    {
        if (req == null || req.MissionId <= 0 || req.MapId <= 0)
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var player = context.Connection.Player!;
        var mapId = (uint)req.MapId;
        var missionId = (uint)req.MissionId;

        var pos = VirCaptureTaskState.FindSlotOfTask(player, mapId, missionId);
        if (pos == 0)
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var sync = new NtfSyncPlayer();
        VirCaptureTaskState.SetSlotTask(player, mapId, pos, 0, sync);
        VirCaptureTaskState.Clear(player, missionId, sync);
        DatabaseHelper.SaveDatabaseType(player.Data);

        return Task.FromResult(CallGSResult.Ok("{}", sync));
    }
}

public sealed class VirCaptureTaskDoGiveUpParam
{
    [JsonPropertyName("nMissionId")] public int MissionId { get; set; }
    [JsonPropertyName("nMapId")] public int MapId { get; set; }
}
