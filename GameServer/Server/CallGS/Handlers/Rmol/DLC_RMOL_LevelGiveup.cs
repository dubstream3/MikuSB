using MikuSB.Util;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rmol;

/// <summary>
/// DLC 绘梦沙盘：放弃当前关卡。客户端只等 <c>DLC_RMOL_LevelGiveup_Return(Level)</c> 把界面收起来，
/// 棋盘状态全在本地，服务端没有需要回滚的东西，所以这里只做参数校验与回显。
/// </summary>
[CallGSApi("DLC_RMOL_LevelGiveup")]
public class DLC_RMOL_LevelGiveup : CallGSHandler<DlcRmolLevelParam>
{
    private static readonly Logger Log = new("DLC_RMOL");

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, DlcRmolLevelParam req)
    {
        if (req == null || req.Level <= 0)
        {
            return Task.FromResult(CallGSResult.Ok(new JsonObject { ["Err"] = "error.BadParam" }.ToJsonString()));
        }

        Log.Info($"DLC_RMOL_LevelGiveup: uid={context.Connection?.Player?.Uid} level={req.Level}.");

        var response = new JsonObject { ["Level"] = req.Level };
        return Task.FromResult(CallGSResult.Ok(response.ToJsonString()));
    }
}

public sealed class DlcRmolLevelParam
{
    [JsonPropertyName("Level")]
    public int Level { get; set; }

    [JsonPropertyName("Session")]
    public int Session { get; set; }
}
