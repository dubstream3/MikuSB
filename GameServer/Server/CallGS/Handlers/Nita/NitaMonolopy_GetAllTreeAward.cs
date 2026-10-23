using System.Text.Json.Nodes;
using MikuSB.Database;
using MikuSB.Data;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Nita;

// 妮塔大富翁 tree house bulk claim (UMG/NitaMonopoly/umg_nitamonolopy_reward.lua:7). The request is
// the literal string "{}" and the callback loops both reply arrays with ipairs:
//
//     ret.tbTreeIdx  -> mark those tree levels as claimed in the panel
//     ret.tbRewards  -> Item.Gain(ret.tbRewards)
//
// Both must be present even when empty: ipairs(nil) throws "bad argument #1 to 'ipairs' (table
// expected, got nil)", which kills the whole reward panel. Reward rows are [G,D,P,L,Count].
//
// Eligibility mirrors the client (Activity/NitaMonolopy.lua:377): the row must carry EX > 0 and the
// player's TreeEX must have reached it, and the claim bit (TreeRewardGot + index) must still be 0.
// tree.json row 1 has EX = 0 and no rewards, so index 1 can never be claimed.
[CallGSApi("NitaMonolopy_GetAllTreeAward")]
public class NitaMonolopy_GetAllTreeAward : CallGSHandler<NitaMonolopyEmptyParam>
{
    private static readonly Logger Log = new(nameof(NitaMonolopy_GetAllTreeAward));

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, NitaMonolopyEmptyParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return CallGSResult.Error("error.BadParam");

        var sync = new NtfSyncPlayer();
        var claimedIdx = new JsonArray();
        var rows = new List<uint[]>();

        var currentEx = player.Attributes.GetValue(
            AttrIds.Activity.NitaMonopolyGroupId,
            AttrIds.Activity.NitaMonopolyTreeExpSid);

        var trees = GameData.NitaTreeData;
        for (var i = 0; i < trees.Count; i++)
        {
            var nodeIndex = (uint)(i + 1);
            var node = trees[i];
            if (node.Ex == 0 || node.Ex > currentEx)
                continue;

            var claimSid = AttrIds.Activity.NitaMonopolyTreeRewardStartSid + nodeIndex;
            if (player.Attributes.GetValue(AttrIds.Activity.NitaMonopolyGroupId, claimSid) != 0)
                continue;

            var nodeRows = DlcRuntimeOverrides.ReadRewardRows(node.RewardsRaw);
            if (nodeRows.Count == 0)
                continue;

            rows.AddRange(nodeRows);
            var attr = player.Attributes.Set(AttrIds.Activity.NitaMonopolyGroupId, claimSid, 1);
            player.Attributes.SyncTo(sync, attr);
            claimedIdx.Add(nodeIndex);
        }

        if (claimedIdx.Count == 0)
        {
            return CallGSResult.Ok(new JsonObject
            {
                ["tbTreeIdx"] = claimedIdx,
                ["tbRewards"] = new JsonArray()
            });
        }

        var granted = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);
        DatabaseHelper.SaveDatabaseType(player.Data);

        Log.Info($"Nita tree bulk claim: {claimedIdx.Count} node(s), {granted.Count} reward row(s) " +
                 $"for uid={player.Uid}.");

        return CallGSResult.Ok(new JsonObject
        {
            ["tbTreeIdx"] = claimedIdx,
            ["tbRewards"] = granted
        }, sync);
    }
}

public sealed class NitaMonolopyEmptyParam
{
}
