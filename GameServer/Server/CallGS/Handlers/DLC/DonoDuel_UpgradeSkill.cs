using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

// 多诺-巅峰对决 卡牌技能学习 / 一键重塑.
//
// Client origin: DonoDuel:DoUpgradeSkill (DLC/DonoDuel/DonoDuel.lua:2345-2362) sends
// { nUniqueId, nSkillId, nPos, tbGiveUpSkill } and, when both nSkillId and nPos are 0, adds
// bResetAll = true (the "一键重塑" button). The callback (:2370-2398) only reacts to
// tbParam.sError and otherwise refreshes the bag / culture / formation panels.
//
// The slot encoding is (DonoDuel.lua:56-67 Enum_DataOffset + :387-388 GetBagItemTableInfo):
//   +5 = (skill1 << 16) | skill2   and   +6 = skill3 << 16.
// The stored value is the raw SkillID of donoduel_herocard_skill.json (101..193) - the client maps it
// to a SkillInfoId at battle time. There are no skill levels. The candidate list itself (three random
// skills out of all 30) is rolled and cached client side, so the server simply accepts the pick.
//
// Slot i requires the card to be at SkillUnloked[i] stars ([2, 3, 4]). The shipped client never
// charges anything for a single learn (donoduel_config ConfigID 4 has no call site), so - like the
// official server - this handler does not spend either; reshaping costs ConfigID 5, indexed by the
// number of locked slots (GetSkillLockedNum() + 1), and keeps the locked slots untouched.
//
// Request:  { "nUniqueId": <1..350>, "nSkillId": <101..193>, "nPos": 1|2|3, "bResetAll": bool? }
// Response: { } on success, { "sError": "..." } otherwise (plus the NtfSyncAttr push).
[CallGSApi("DonoDuel_UpgradeSkill")]
public class DonoDuel_UpgradeSkill : CallGSHandler
{
    private static readonly Logger Log = new(nameof(DonoDuel_UpgradeSkill));

    /// <summary>DonoDuel.Enum_Flag.LockSkill1 (DonoDuel.lua:107-112): the skill-slot locks are bits 1..3.</summary>
    private const uint SkillLockBitBase = 1;

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player!;

        var root = DonoDuelGrowth.ParseObject(param);
        var uniqueId = DonoDuelGrowth.ReadUint(root?["nUniqueId"]);
        var skillId = DonoDuelGrowth.ReadUint(root?["nSkillId"]);
        var position = DonoDuelGrowth.ReadUint(root?["nPos"]);
        var resetAll = DonoDuelGrowth.ReadBool(root?["bResetAll"]);

        if (!DonoDuelBag.HasCard(player, uniqueId))
        {
            Log.Warn($"Donoduel skill learn for unknown card {uniqueId} from uid={player.Uid}.");
            return DonoDuelGrowth.Bad();
        }

        var templateId = DonoDuelBag.GetTemplateId(player, uniqueId);
        if (!GameData.DonoDuelHeroCardData.TryGetValue(templateId, out var heroCard))
            return DonoDuelGrowth.Bad();

        var skills = new[]
        {
            DonoDuelBag.GetSkill(player, uniqueId, 1),
            DonoDuelBag.GetSkill(player, uniqueId, 2),
            DonoDuelBag.GetSkill(player, uniqueId, 3)
        };

        var flags = DonoDuelBag.GetFlags(player, uniqueId);
        var sync = new NtfSyncPlayer();

        if (resetAll)
        {
            var lockedSlots = 0u;
            for (var slot = 0; slot < DonoDuelGrowth.SkillSlots; slot++)
            {
                if ((flags & (1u << (int)(SkillLockBitBase + (uint)slot))) != 0)
                    lockedSlots++;
                else
                    skills[slot] = 0;
            }

            var cost = DonoDuelConfig.SkillReshapingCost(lockedSlots);
            if (cost > 0 &&
                !CurrencyService.TrySpend(player, DonoDuelGrowth.SeasonCurrency(player), cost, sync, out var resetError))
                return DonoDuelGrowth.Bad(resetError);

            Log.Info(
                $"Donoduel card {uniqueId} skill reshape ({lockedSlots} locked slot(s), cost {cost}) " +
                $"for uid={player.Uid}.");
        }
        else
        {
            if (position is < 1 or > (uint)DonoDuelGrowth.SkillSlots)
                return DonoDuelGrowth.Bad();

            if (!GameData.DonoDuelHeroCardSkillData.ContainsKey(skillId))
            {
                Log.Warn($"Donoduel skill {skillId} does not exist (uid={player.Uid}).");
                return DonoDuelGrowth.Bad();
            }

            var star = DonoDuelBag.GetStar(player, uniqueId);
            var required = heroCard.SkillUnlockStar((int)position);
            if (required != 0 && star < required)
            {
                Log.Info(
                    $"Donoduel card {uniqueId} skill slot {position} needs star {required} (has {star}).");
                return DonoDuelGrowth.Bad("error.condition_limit");
            }

            skills[(int)position - 1] = skillId;
        }

        DonoDuelBag.SetSkills(player, uniqueId, skills[0], skills[1], skills[2], sync);
        DonoDuelBag.Save(player);
        await player.SendPacket(CmdIds.NtfSyncAttr, sync);

        Log.Info(
            $"Donoduel card {uniqueId} skills [{skills[0]}, {skills[1]}, {skills[2]}] " +
            $"{(resetAll ? "(reshaped)" : $"slot {position} learned {skillId}")} for uid={player.Uid}.");

        return DonoDuelGrowth.Ok();
    }
}
