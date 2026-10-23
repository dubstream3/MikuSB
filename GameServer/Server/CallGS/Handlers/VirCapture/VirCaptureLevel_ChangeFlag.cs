using MikuSB.Database;
using MikuSB.Proto;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

[CallGSApi("VirCaptureLevel_ChangeFlag")]
public class VirCaptureLevel_ChangeFlag : CallGSHandler<VirCaptureChangeFlagParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, VirCaptureChangeFlagParam req)
    {

        if (req == null || req.LevelId == 0 || req.RegionId == 0)
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var player = context.Connection.Player!;
        var sync = new NtfSyncPlayer();
        VirCaptureStateHelper.SetPointState(player, (uint)req.LevelId, (uint)req.RegionId, req.Clean ? 0u : 1u, sync);

        DatabaseHelper.SaveDatabaseType(player.Data);
        var rsp = $"{{\"nLevelID\":{req.LevelId},\"nRegionId\":{req.RegionId},\"bClean\":{req.Clean.ToString().ToLowerInvariant()}}}";
        return Task.FromResult(CallGSResult.Ok(rsp, sync));
    }
}

public sealed class VirCaptureChangeFlagParam
{
    [JsonPropertyName("nLevelID")]
    public int LevelId { get; set; }

    [JsonPropertyName("nRegionId")]
    public int RegionId { get; set; }

    [JsonPropertyName("bClean")]
    public bool Clean { get; set; }
}
