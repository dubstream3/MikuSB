using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Database;
using MikuSB.Data;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Nita;

// 妮塔大富翁 single tree house claim (UMG/NitaMonopoly/umg_nitamonolopy_reward.lua:275-278).
//
//     Request:  { nTreeIdx: int }   -- 1-based index into TreeConfigs, i.e. tree.json order
//     Response: { nTreeIdx }        -- the client only checks sErr and reads back nTreeIdx; the
//                                      reward toast is built from its own local
//                                      TreeConfigs[nTreeIdx].Rewards (reward.lua:126)
//
// So the pick itself is purely a server decision: validate the index, make sure TreeEX covered the
// row, make sure the claim bit is still clear, grant the configured rows, then flip the bit.
[CallGSApi("NitaMonolopy_GetTreeAward")]
public class NitaMonolopy_GetTreeAward : CallGSHandler<NitaMonolopyGetTreeAwardParam>
{
    private static readonly Logger Log = new(nameof(NitaMonolopy_GetTreeAward));

    protected override async Task<CallGSResult> HandleAsync(
        CallGSContext context,
        NitaMonolopyGetTreeAwardParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return CallGSResult.Error("error.BadParam");

        var index = request.TreeIdx;
        if (index == 0 || index > GameData.NitaTreeData.Count)
            return CallGSResult.Error("error.BadParam");

        var node = GameData.NitaTreeData[(int)index - 1];
        var currentEx = player.Attributes.GetValue(
            AttrIds.Activity.NitaMonopolyGroupId,
            AttrIds.Activity.NitaMonopolyTreeExpSid);

        if (node.Ex == 0 || node.Ex > currentEx)
            return CallGSResult.Error("ui.TxtNotOpen");

        var claimSid = AttrIds.Activity.NitaMonopolyTreeRewardStartSid + index;
        if (player.Attributes.GetValue(AttrIds.Activity.NitaMonopolyGroupId, claimSid) != 0)
            return CallGSResult.Error("achievement.RewardGeted");

        var rows = DlcRuntimeOverrides.ReadRewardRows(node.RewardsRaw);
        if (rows.Count == 0)
            return CallGSResult.Error("ui.TxtNotOpen");

        var sync = new NtfSyncPlayer();
        var granted = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);

        var attr = player.Attributes.Set(AttrIds.Activity.NitaMonopolyGroupId, claimSid, 1);
        player.Attributes.SyncTo(sync, attr);

        DatabaseHelper.SaveDatabaseType(player.Data);

        Log.Info($"Nita tree claim: idx={index}, ex={node.Ex}/{currentEx}, " +
                 $"{granted.Count} reward row(s) for uid={player.Uid}.");

        return CallGSResult.Ok(new JsonObject
        {
            ["nTreeIdx"] = index,
            ["tbRewards"] = granted
        }, sync);
    }
}

public sealed class NitaMonolopyGetTreeAwardParam
{
    /// <summary>1-based tree.json index; tree.json has no id column.</summary>
    [JsonPropertyName("nTreeIdx")] public uint TreeIdx { get; set; }
}
