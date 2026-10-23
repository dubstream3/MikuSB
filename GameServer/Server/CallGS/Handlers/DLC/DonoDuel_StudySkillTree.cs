using MikuSB.Data;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

// 多诺-巅峰对决 技能树("科技树")节点研习.
//
// Client origin: DonoDuel:DoStudySkillTree (DLC/DonoDuel/DonoDuel.lua:2440-2450) sends
// { nSkillId }; the callback (:2452-2471) only reacts to tbParam.sError and otherwise refreshes
// DonoDuelSkillTree. The panel (umg_donoduel_skilltree.lua:205-244) checks the balance locally but
// never spends - the server owns both the charge and the node.
//
// Storage: a bit array, node N = bit (N-1)%30 of attribute (185, 91 + floor((N-1)/30))
// (DonoDuel.lua:318-330 GetSkillTreeFlag, AttrIds.DonoDuel.SkillTreeStartSid). The client has no
// write path at all. Cost = donoduel_skilltree.json Consume = [63, price] (5000 for node 1 and
// +100 per node) and a node requires its Prerequisite to be lit first (DonoDuel:CheckSkillTreePre).
// Three malformed rows in the shipped table carry no numeric id and are dropped by the loader.
//
// Request:  { "nSkillId": <skill tree node id> }
// Response: { } on success, { "sError": "..." } otherwise (plus the NtfSyncAttr push).
[CallGSApi("DonoDuel_StudySkillTree")]
public class DonoDuel_StudySkillTree : CallGSHandler
{
    private static readonly Logger Log = new(nameof(DonoDuel_StudySkillTree));

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player!;

        var root = DonoDuelGrowth.ParseObject(param);
        var nodeId = DonoDuelGrowth.ReadUint(root?["nSkillId"]);

        if (nodeId == 0 || !GameData.DonoDuelSkillTreeData.TryGetValue(nodeId, out var node))
        {
            Log.Warn($"Unknown Donoduel skill tree node {nodeId} from uid={player.Uid}.");
            return DonoDuelGrowth.Bad();
        }

        if (HasNode(player, nodeId))
            return DonoDuelGrowth.Bad("error.condition_limit");

        if (node.Prerequisite != 0 && !HasNode(player, node.Prerequisite))
        {
            Log.Info($"Donoduel skill tree node {nodeId} needs node {node.Prerequisite} first (uid={player.Uid}).");
            return DonoDuelGrowth.Bad("error.condition_limit");
        }

        var (moneyType, amount) = node.Consume;
        if (amount == 0)
            return DonoDuelGrowth.Bad();

        var sync = new NtfSyncPlayer();
        if (!CurrencyService.TrySpend(player, moneyType == 0 ? DonoDuelGrowth.SeasonCurrency(player) : moneyType,
                amount, sync, out var error))
            return DonoDuelGrowth.Bad(error);

        var sid = SidOf(nodeId);
        var bit = BitOf(nodeId);
        var current = player.Attributes.GetValue(AttrIds.DonoDuel.GroupId, sid);
        DonoDuelBag.SetSlot(player, sid, current | (1u << bit), sync);

        DonoDuelBag.Save(player);
        await player.SendPacket(CmdIds.NtfSyncAttr, sync);

        Log.Info($"Donoduel skill tree node {nodeId} studied (cost {amount}) for uid={player.Uid}.");

        return DonoDuelGrowth.Ok();
    }

    private static bool HasNode(PlayerInstance player, uint nodeId)
    {
        var value = player.Attributes.GetValue(AttrIds.DonoDuel.GroupId, SidOf(nodeId));
        return (value & (1u << BitOf(nodeId))) != 0;
    }

    private static uint SidOf(uint nodeId)
        => AttrIds.DonoDuel.SkillTreeStartSid + (nodeId - 1) / AttrIds.DonoDuel.SkillTreePerSid;

    private static int BitOf(uint nodeId) => (int)((nodeId - 1) % AttrIds.DonoDuel.SkillTreePerSid);
}
