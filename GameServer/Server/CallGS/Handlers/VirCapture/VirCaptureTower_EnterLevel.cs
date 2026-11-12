using MikuSB.Data;
using MikuSB.GameServer.Game.Player;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

[CallGSApi("VirCaptureTower_EnterLevel")]
public class VirCaptureTower_EnterLevel : CallGSHandler<VirCaptureTowerEnterLevelParam>
{
    private static readonly Random Random = new();

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, VirCaptureTowerEnterLevelParam req)
    {

        if (req == null || req.LevelId <= 0 || req.TeamId <= 0)
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        if (!GameData.VirCaptureTowerData.TryGetValue((uint)req.LevelId, out var levelCfg))
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var player = context.Connection.Player!;
        if (!VirCaptureConditionHelper.Check(player, levelCfg.ConditionRaw, $"Tower.EnterLevel:{req.LevelId}"))
        {
            return Task.FromResult(CallGSResult.Error("tip.LevelLocked"));
        }

        return Task.FromResult(CallGSResult.Ok($"{{\"nSeed\":{Random.Next(1, 1_000_000_000)}}}"));
    }
}

public sealed class VirCaptureTowerEnterLevelParam
{
    [JsonPropertyName("nID")]
    public int LevelId { get; set; }

    [JsonPropertyName("nTeamID")]
    public int TeamId { get; set; }
}
