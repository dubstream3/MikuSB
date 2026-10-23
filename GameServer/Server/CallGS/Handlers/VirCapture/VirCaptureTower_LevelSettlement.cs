using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using MikuSB.Data;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

[CallGSApi("VirCaptureTower_LevelSettlement")]
public class VirCaptureTower_LevelSettlement : CallGSHandler
{
    private const uint LaunchLevelStateGroupId = AttrIds.Tower.LevelStateGid;
    private const uint LaunchPassGroupId = AttrIds.Tower.PassGid;
    private const uint PassedFlagBit = 1u << 8;
    private static readonly Logger Logger = new("VirCaptureTower");

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var (response, sync) = HandleSettlement(context.Connection.Player!, JsonNode.Parse(param));
        return Task.FromResult(CallGSResult.Ok(response.ToJsonString(), sync));
    }

    public static (JsonNode Response, NtfSyncPlayer Sync) HandleSettlement(PlayerInstance player, JsonNode? tbParam)
    {
        var req = tbParam?.Deserialize<VirCaptureTowerSettlementParam>();
        if (req == null || req.LevelId == 0)
        {
            Logger.Error($"Invalid vircapture tower settlement payload: {tbParam?.ToJsonString() ?? "null"}");
            return (new JsonObject { ["sErr"] = "error.BadParam" }, new NtfSyncPlayer());
        }

        var sync = new NtfSyncPlayer();

        var levelStateAttr = player.Attributes.GetOrCreate(LaunchLevelStateGroupId, (uint)req.LevelId);
        levelStateAttr.Val |= MergeStarMask(req.StarMask) | PassedFlagBit;
        player.Attributes.SyncTo(sync, levelStateAttr);

        var passAttr = player.Attributes.GetOrCreate(LaunchPassGroupId, (uint)req.LevelId);
        passAttr.Val = Math.Max(1u, passAttr.Val + 1);
        player.Attributes.SyncTo(sync, passAttr);

        Logger.Info(
            $"VirCaptureTower settlement saved. uid={player.Uid} levelId={req.LevelId} starMask={req.StarMask} " +
            $"levelStateVal={levelStateAttr.Val} passVal={passAttr.Val}");

        DatabaseHelper.SaveDatabaseType(player.Data);
        return (new JsonObject(), sync);
    }

    private static uint MergeStarMask(int starMask)
    {
        uint result = 0;
        for (var i = 0; i < 3; i++)
        {
            if (((starMask >> i) & 1) != 0)
                result |= 1u << i;
        }

        return result;
    }

}

internal sealed class VirCaptureTowerSettlementParam
{
    [JsonPropertyName("nID")]
    public int LevelId { get; set; }

    [JsonPropertyName("nStar")]
    public int StarMask { get; set; }
}
