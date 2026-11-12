using MikuSB.Util;
using System.Text.Json.Nodes;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// 爬塔失败上报（VirCaptureTower.lua:660-673）.
///
/// The client sends `{nID, tbLog}` and registers no s2c callback at all, so answering would leave the
/// client's script dispatcher with a response nobody consumes. The handler exists so the failure is
/// recorded instead of hitting CallGSRouter's "No handler" fallback, and it answers nothing.
/// </summary>
[CallGSApi("VirCaptureTower_LevelFail")]
public class VirCaptureTower_LevelFail : CallGSHandler
{
    private static readonly Logger Logger = new("VirCapture.Tower.LevelFail");

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var levelId = ReadLevelId(param);
        Logger.Info($"tower level fail level={levelId} uid={context.Connection.Player?.Uid ?? 0}");
        return Task.FromResult(CallGSResult.NoResponse());
    }

    internal static int ReadLevelId(string param)
    {
        try
        {
            var node = JsonNode.Parse(param);
            return node?["nID"]?.GetValue<int>() ?? 0;
        }
        catch
        {
            return 0;
        }
    }
}
