using MikuSB.Util;
using System.Text.Json.Nodes;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// 试炼失败上报（VirCaptureTrial.lua:378-392）.
///
/// Same contract as the tower failure: the client registers no s2c callback, so the handler only
/// records the run and closes the request with NoResponse() instead of the router's generic
/// ui.TxtNotOpen fallback (which would look like "the mode is closed" in the logs).
/// </summary>
[CallGSApi("VirCaptureTrial_LevelFail")]
public class VirCaptureTrial_LevelFail : CallGSHandler
{
    private static readonly Logger Logger = new("VirCapture.Trial.LevelFail");

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var levelId = VirCaptureTower_LevelFail.ReadLevelId(param);
        Logger.Info($"trial level fail level={levelId} uid={context.Connection.Player?.Uid ?? 0}");
        return Task.FromResult(CallGSResult.NoResponse());
    }
}
