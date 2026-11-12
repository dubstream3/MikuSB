using MikuSB.Database;
using MikuSB.Proto;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// Reports one progress step for a blueprint island mission. The client already matched the
/// mission locally and sends the id it resolved as <c>nCheckTaskId</c>; the server re-validates
/// the mission type and prerequisites before advancing the 163 progress bits.
/// </summary>
[CallGSApi("VirCaptureTask_DoSomething")]
public class VirCaptureTask_DoSomething : CallGSHandler<VirCaptureTaskDoSomethingParam>
{
    private const uint TypeGotoPoint = 8;
    private const uint TypeFinishTask = 9;
    private const uint TypeFinishActor = 10;
    private const uint TypeCostTask = 23;
    private const uint TypeFinishTaskEnd = 24;

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, VirCaptureTaskDoSomethingParam req)
    {
        if (req == null || req.MapId <= 0 || req.CheckTaskId <= 0)
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var type = (uint)Math.Max(0, req.Type);
        if (type is not (TypeGotoPoint or TypeFinishTask or TypeFinishActor or TypeCostTask or TypeFinishTaskEnd))
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var player = context.Connection.Player!;
        var mapId = (uint)req.MapId;
        if (VirCaptureStateHelper.FindMapSlot(player, mapId) == 0)
        {
            return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));
        }

        var cfg = VirCaptureTaskState.GetTask((uint)req.CheckTaskId);
        if (cfg == null || cfg.Type != type)
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        if (VirCaptureTaskState.IsReceived(player, cfg.Id))
        {
            // Already claimed, nothing to advance.
            return Task.FromResult(CallGSResult.Ok(new JsonObject { ["nTaskId"] = cfg.Id }.ToJsonString()));
        }

        if (!VirCaptureConditionHelper.Check(player, cfg.Condition, $"Task.DoSomething:{cfg.Id}"))
            return Task.FromResult(CallGSResult.Error("error.condition_limit"));

        if (!VirCaptureTaskState.PreReceived(player, cfg))
        {
            return Task.FromResult(CallGSResult.Error("error.condition_limit"));
        }

        var sync = new NtfSyncPlayer();
        var progress = VirCaptureTaskState.AddProgress(player, cfg.Id, 1, cfg.CheckValue, sync);
        DatabaseHelper.SaveDatabaseType(player.Data);

        var rsp = new JsonObject
        {
            ["nTaskId"] = cfg.Id,
            ["nProgress"] = progress
        };
        return Task.FromResult(CallGSResult.Ok(rsp.ToJsonString(), sync));
    }
}

public sealed class VirCaptureTaskDoSomethingParam
{
    [JsonPropertyName("nType")] public int Type { get; set; }
    [JsonPropertyName("nMapId")] public int MapId { get; set; }
    [JsonPropertyName("nTaskId")] public int TaskId { get; set; }
    [JsonPropertyName("nCheckTaskId")] public int CheckTaskId { get; set; }
}
