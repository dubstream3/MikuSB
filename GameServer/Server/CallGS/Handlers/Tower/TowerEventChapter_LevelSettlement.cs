using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using MikuSB.Data;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Tower;

[CallGSApi("TowerEventChapter_LevelSettlement")]
public class TowerEventChapter_LevelSettlement : CallGSHandler
{
    private const uint LevelStateGroupId = AttrIds.Tower.LevelStateGid;
    private const uint LaunchPassGroupId = AttrIds.Tower.PassGid;
    private const uint PassedFlagMask = (1u << 8) | 0b111u;
    private static readonly Logger Logger = new("TowerEvent");

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var (response, sync) = HandleSettlement(context.Connection.Player!, JsonNode.Parse(param));
        return Task.FromResult(CallGSResult.Ok(response.ToJsonString(), sync));
    }

    public static (JsonNode Response, NtfSyncPlayer Sync) HandleSettlement(PlayerInstance player, JsonNode? tbParam)
    {
        var req = tbParam?.Deserialize<TowerEventSettlementParam>();
        if (req == null || req.LevelId == 0 || req.ChapterId == 0)
        {
            Logger.Error($"Invalid tower event settlement payload: {tbParam?.ToJsonString() ?? "null"}");
            return (new JsonObject { ["sErr"] = "error.BadParam" }, new NtfSyncPlayer());
        }

        var sync = new NtfSyncPlayer();

        var levelStateAttr = player.Attributes.GetOrCreate(LevelStateGroupId, (uint)req.LevelId);
        levelStateAttr.Val |= PassedFlagMask;
        player.Attributes.SyncTo(sync, levelStateAttr);

        var passAttr = player.Attributes.GetOrCreate(LaunchPassGroupId, (uint)req.LevelId);
        passAttr.Val = Math.Max(1u, passAttr.Val + 1);
        player.Attributes.SyncTo(sync, passAttr);

        Logger.Info(
            $"TowerEvent settlement saved. uid={player.Uid} chapterId={req.ChapterId} levelId={req.LevelId} " +
            $"levelStateVal={levelStateAttr.Val} passVal={passAttr.Val}");

        DatabaseHelper.SaveDatabaseType(player.Data);
        return (new JsonObject(), sync);
    }

}

internal sealed class TowerEventSettlementParam
{
    [JsonPropertyName("nID")]
    public int LevelId { get; set; }

    [JsonPropertyName("nChapterID")]
    public int ChapterId { get; set; }
}
