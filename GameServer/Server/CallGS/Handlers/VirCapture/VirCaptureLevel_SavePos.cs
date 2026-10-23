using MikuSB.Database;
using MikuSB.Proto;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

[CallGSApi("VirCaptureLevel_SavePos")]
public class VirCaptureLevel_SavePos : CallGSHandler<VirCaptureSavePosParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, VirCaptureSavePosParam req)
    {

        if (req == null || req.LevelId == 0)
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var player = context.Connection.Player!;
        var sync = new NtfSyncPlayer();
        VirCaptureStateHelper.SetSignedMapOffset(player, (uint)req.LevelId, VirCaptureStateHelper.OffPosX, req.PosX, sync);
        VirCaptureStateHelper.SetSignedMapOffset(player, (uint)req.LevelId, VirCaptureStateHelper.OffPosY, req.PosY, sync);
        VirCaptureStateHelper.SetSignedMapOffset(player, (uint)req.LevelId, VirCaptureStateHelper.OffPosZ, req.PosZ, sync);
        VirCaptureStateHelper.SetSignedMapOffset(player, (uint)req.LevelId, VirCaptureStateHelper.OffToward, req.Toward, sync);

        DatabaseHelper.SaveDatabaseType(player.Data);
        return Task.FromResult(CallGSResult.Ok("{}", sync));
    }
}

public sealed class VirCaptureSavePosParam
{
    [JsonPropertyName("nLevelID")]
    public int LevelId { get; set; }

    [JsonPropertyName("nPosX")]
    public int PosX { get; set; }

    [JsonPropertyName("nPosY")]
    public int PosY { get; set; }

    [JsonPropertyName("nPosZ")]
    public int PosZ { get; set; }

    [JsonPropertyName("nToward")]
    public int Toward { get; set; }
}
