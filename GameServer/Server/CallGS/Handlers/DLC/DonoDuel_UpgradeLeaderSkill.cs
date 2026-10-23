using MikuSB.Data;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

// 多诺-巅峰对决 队长技装备.
//
// Client origin: DonoDuel:DoUpgradeLeaderSkill (DLC/DonoDuel/DonoDuel.lua:2401-2411) sends
// { nSkillId }; the callback (:2413-2437) only reacts to tbParam.sError and otherwise refreshes the
// formation / bag panels.
//
// Equipping is a single attribute write: DonoDuel:GetLeaderSkill (DonoDuel.lua:223-225) reads
// (185, 4) and uw_donoduel_arena_main_skill.lua:86-109 highlights the row whose LeaderskillID
// matches it - so the server must write and sync that slot, an empty reply is not enough.
// donoduel_leaderskill.json holds the six rows and their unlock conditions
// (1 = story mission 104 claimed, 2..6 = arena levels 5506..5510 cleared).
//
// Request:  { "nSkillId": 1..6 }
// Response: { } on success, { "sError": "..." } otherwise (plus the NtfSyncAttr push).
[CallGSApi("DonoDuel_UpgradeLeaderSkill")]
public class DonoDuel_UpgradeLeaderSkill : CallGSHandler
{
    private static readonly Logger Log = new(nameof(DonoDuel_UpgradeLeaderSkill));

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player!;

        var root = DonoDuelGrowth.ParseObject(param);
        var skillId = DonoDuelGrowth.ReadUint(root?["nSkillId"]);

        if (skillId == 0 || !GameData.DonoDuelLeaderSkillData.TryGetValue(skillId, out var leaderSkill))
        {
            Log.Warn($"Unknown Donoduel leader skill {skillId} from uid={player.Uid}.");
            return DonoDuelGrowth.Bad();
        }

        var pairs = leaderSkill.ConditionPairs();
        for (var index = 0; index + 1 < pairs.Count; index += 2)
        {
            var condition = new[] { pairs[index], pairs[index + 1] };
            if (DonoDuelGrowth.ConditionMet(player, condition))
                continue;

            Log.Info($"Donoduel leader skill {skillId} still locked for uid={player.Uid}.");
            return DonoDuelGrowth.Bad("error.condition_limit");
        }

        var sync = new NtfSyncPlayer();
        DonoDuelBag.SetSlot(player, AttrIds.DonoDuel.LeaderSkillSid, skillId, sync);
        DonoDuelBag.Save(player);
        await player.SendPacket(CmdIds.NtfSyncAttr, sync);

        Log.Info($"Donoduel leader skill {skillId} equipped by uid={player.Uid}.");

        return DonoDuelGrowth.Ok();
    }
}
