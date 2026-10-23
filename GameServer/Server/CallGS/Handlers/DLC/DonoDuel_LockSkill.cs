using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

// 多诺-巅峰对决 技能栏锁定 (≠ 整卡锁定, that is DonoDuel_SetLock).
//
// Client origin: DonoDuel:DoLockSkill (DLC/DonoDuel/DonoDuel.lua:2113-2136) sends
// { nUniqueId, nLockSkillPos = <1..3>, bLock = <the flipped value> }; the callback (:2138-2161)
// stops on tbParam.sError WITHOUT clearing the pending request, so a rejected call must leave the
// attribute untouched. It then re-reads the card (DonoDuel:GetBagItemAndConfig) - hence the sync.
//
// The lock of skill slot i is flag bit i (DonoDuel.lua:107-112 Enum_Flag.LockSkill1..3, read back by
// IsSkillLocked :455-462); locked slots survive a reshape (DonoDuel_UpgradeSkill) and raise its price.
//
// Request:  { "nUniqueId": <1..350>, "nLockSkillPos": 1|2|3, "bLock": true|false }
// Response: { } on success, { "sError": "..." } otherwise (plus the NtfSyncAttr push).
[CallGSApi("DonoDuel_LockSkill")]
public class DonoDuel_LockSkill : CallGSHandler
{
    private static readonly Logger Log = new(nameof(DonoDuel_LockSkill));

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player!;

        var root = DonoDuelGrowth.ParseObject(param);
        var uniqueId = DonoDuelGrowth.ReadUint(root?["nUniqueId"]);
        var position = DonoDuelGrowth.ReadUint(root?["nLockSkillPos"]);
        var locked = DonoDuelGrowth.ReadBool(root?["bLock"]);

        if (!DonoDuelBag.HasCard(player, uniqueId) || position is < 1 or > (uint)DonoDuelGrowth.SkillSlots)
        {
            Log.Warn($"Donoduel skill lock for card {uniqueId} slot {position} from uid={player.Uid}.");
            return DonoDuelGrowth.Bad();
        }

        var flags = DonoDuelBag.GetFlags(player, uniqueId);
        var bit = 1u << (int)position;
        flags = locked ? flags | bit : flags & ~bit;

        var sync = new NtfSyncPlayer();
        DonoDuelBag.SetFlags(player, uniqueId, flags, sync);
        DonoDuelBag.Save(player);
        await player.SendPacket(CmdIds.NtfSyncAttr, sync);

        Log.Info(
            $"Donoduel card {uniqueId} skill slot {position} {(locked ? "locked" : "unlocked")} " +
            $"by uid={player.Uid} (flags={flags}).");

        return DonoDuelGrowth.Ok(new JsonObject
        {
            ["nUniqueId"] = uniqueId,
            ["nLockSkillPos"] = position
        });
    }
}
