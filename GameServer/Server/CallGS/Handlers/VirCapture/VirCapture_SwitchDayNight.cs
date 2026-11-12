using MikuSB.Database;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// Day/night switch for the open VirCapture maps (palwild_01 = 5810, palwild_02 = 325).
/// The client sends only {nMapId} and reads the resulting state back from the map block (OffDayNight),
/// so the request is a toggle: day(1) -> night(2) -> day(1). Repeated submits flip again; there is no
/// request id to distinguish a retry from a new intent (see docs/progress/073 §5).
/// </summary>
[CallGSApi("VirCapture_SwitchDayNight")]
public class VirCapture_SwitchDayNight : CallGSHandler<VirCaptureSwitchDayNightParam>
{
    private const uint DayValue = 1;
    private const uint NightValue = 2;
    private static readonly Logger Logger = new("VirCapture.SwitchDayNight");

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, VirCaptureSwitchDayNightParam req)
    {
        if (req == null || req.MapId <= 0)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var mapId = (uint)req.MapId;

        if (!GameData.VirCaptureCaptureRegionData.TryGetValue(mapId, out var region))
            return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));

        var act = VirCaptureStateHelper.ResolveCurrentAct(context.Connection.Player!);
        if (act == null || !act.CaptureRegionId.Contains(mapId))
            return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));

        // 时代优先：pin 了 DLC 时代时，在该时代内结束的抓捕区仍然可切昼夜。
        if (!VirCaptureEraTime.IsOpen(region.StartTime, region.EndTime))
            return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));

        // The blueprint island (326) runs the build/operate loop and has no light switch;
        // the client refuses to switch there (VirCapture.lua:2771). Keep the server authoritative.
        if (GameData.VirCaptureOperatingData.ContainsKey(mapId))
            return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));

        var player = context.Connection.Player!;
        if (!VirCaptureConditionHelper.Check(player, region.Condition, $"SwitchDayNight.region:{mapId}"))
            return Task.FromResult(CallGSResult.Error("error.condition_limit"));
        if (!VirCaptureConditionHelper.Check(player, region.LightSwitchCondition, $"SwitchDayNight.light:{mapId}"))
            return Task.FromResult(CallGSResult.Error("error.condition_limit"));

        var sync = new NtfSyncPlayer();
        if (VirCaptureStateHelper.FindOrAllocateMapSlot(player, mapId) == 0)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        VirCaptureStateHelper.EnsureBaseMapState(player, mapId, sync);

        var current = VirCaptureStateHelper.GetMapOffset(player, mapId, VirCaptureStateHelper.OffDayNight);
        var next = current == NightValue ? DayValue : NightValue;
        VirCaptureStateHelper.SetSignedMapOffset(player, mapId, VirCaptureStateHelper.OffDayNight, (int)next, sync);
        DatabaseHelper.SaveDatabaseType(player.Data);

        Logger.Info($"map {mapId} day/night {current} -> {next}");

        var response = new JsonObject
        {
            ["nMapId"] = req.MapId,
            ["nDayNight"] = next
        };

        return Task.FromResult(CallGSResult.Ok(response.ToJsonString(), sync));
    }

}

public sealed class VirCaptureSwitchDayNightParam
{
    [JsonPropertyName("nMapId")]
    public int MapId { get; set; }
}
