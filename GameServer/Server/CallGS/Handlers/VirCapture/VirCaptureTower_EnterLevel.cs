using MikuSB.Data;
using MikuSB.GameServer.Game.Player;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

[CallGSApi("VirCaptureTower_EnterLevel")]
public class VirCaptureTower_EnterLevel : CallGSHandler<VirCaptureTowerEnterLevelParam>
{
    private const uint LaunchPassGroupId = AttrIds.Tower.PassGid;
    private const uint VirCaptureGroupId = AttrIds.VirCapture.Gid;
    private const uint VirCaptureLevelSid = AttrIds.VirCapture.CurrentLevelSid;
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
        if (!CheckConditions(player, levelCfg.Condition))
        {
            return Task.FromResult(CallGSResult.Error("tip.LevelLocked"));
        }

        return Task.FromResult(CallGSResult.Ok($"{{\"nSeed\":{Random.Next(1, 1_000_000_000)}}}"));
    }

    private static bool CheckConditions(PlayerInstance player, IReadOnlyDictionary<int, uint> conditions)
    {
        foreach (var (key, value) in conditions)
        {
            switch (key)
            {
                case 1:
                    if (player.Data.Level < value)
                        return false;
                    break;
                case 2:
                {
                    var pass = player.Attributes.GetValue(LaunchPassGroupId, value);
                    if (pass == 0)
                        return false;
                    break;
                }
                case 20:
                {
                    var virLevel = player.Attributes.GetValue(VirCaptureGroupId, VirCaptureLevelSid);
                    if (virLevel < value)
                        return false;
                    break;
                }
            }
        }

        return true;
    }
}

public sealed class VirCaptureTowerEnterLevelParam
{
    [JsonPropertyName("nID")]
    public int LevelId { get; set; }

    [JsonPropertyName("nTeamID")]
    public int TeamId { get; set; }
}
