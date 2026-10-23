using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Tower;

[CallGSApi("ClimbTowerLogic_SetLevelDiff")]
public class ClimbTowerLogic_SetLevelDiff : CallGSHandler<ClimbTowerSetLevelDiffParam>
{
    private const uint TowerGroupId = AttrIds.Tower.Gid;
    private const uint DiffSid = AttrIds.Tower.DiffSid;
    private const uint HisDiffSid = AttrIds.Tower.HistoryDiffSid;

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, ClimbTowerSetLevelDiffParam req)
    {
        var player = context.Connection.Player!;

        if (req == null || req.Diff <= 0)
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        if (!GameData.ClimbTowerDiffData.ContainsKey((uint)req.Diff))
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var hisDiff = player.Attributes.GetValue(TowerGroupId, HisDiffSid);
        if (req.Diff > hisDiff + 1)
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var diffAttr = player.Attributes.GetOrCreate(TowerGroupId, DiffSid);
        diffAttr.Val = (uint)req.Diff;

        var sync = new NtfSyncPlayer();
        player.Attributes.SyncTo(sync, diffAttr);

        DatabaseHelper.SaveDatabaseType(player.Data);
        return Task.FromResult(CallGSResult.Ok("{}", sync));
    }

}

public sealed class ClimbTowerSetLevelDiffParam
{
    [JsonPropertyName("nDiff")]
    public int Diff { get; set; }
}
