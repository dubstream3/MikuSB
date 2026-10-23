using MikuSB.Database;
using MikuSB.Proto;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

[CallGSApi("VirCaptureLevel_SaveFightData")]
public class VirCaptureLevel_SaveFightData : CallGSHandler<VirCaptureSaveFightDataParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, VirCaptureSaveFightDataParam req)
    {

        if (req == null || req.LevelId == 0 || req.RegionId == 0)
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var player = context.Connection.Player!;
        var sync = new NtfSyncPlayer();
        VirCaptureStateHelper.SetPointState(player, (uint)req.LevelId, (uint)req.RegionId, 2u, sync);

        DatabaseHelper.SaveDatabaseType(player.Data);

        var response = new JsonObject
        {
            ["nLevelID"] = req.LevelId,
            ["nRegionId"] = req.RegionId,
            ["tbRewards"] = new JsonArray()
        };

        return Task.FromResult(CallGSResult.Ok(response.ToJsonString(), sync));
    }
}

public sealed class VirCaptureSaveFightDataParam
{
    [JsonPropertyName("nLevelID")]
    public int LevelId { get; set; }

    [JsonPropertyName("nRegionId")]
    public int RegionId { get; set; }
}
