using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.AlienDefense;

// 异星守护 (alien defense / Mow) partner skill upgrade. umg_mow_skill.lua:121-127 checks its own
// bCanBuy against the cost list before calling AlienDefense.UpgradeSkill(nPalIdx, nSkillIdx)
// (DLC/AlienDefense/AlienDefense.lua:1092-1096):
//
//     { nPalIdx, nSkillIdx }
//
// The callback (:1098-1104) needs BOTH fields echoed back -- MowSkill.OnRspUpgradeSkill
// (umg_mow_skill.lua:163-175) returns early without tbParam.nPalIdx / nSkillIdx -- and then reads
// the new level through AlienDefense.GetPalCostSkillLevel, i.e. straight from the local attribute,
// so the level has to be written before the answer goes out.
//
// Layout (:32-33, GetPalCostSkillLevel :362-373): subId = 301 + nPalIdx, one 4-bit field per skill
// starting at bit (nSkillIdx-1)*4.
//
// The currency cost is NOT charged: the price table lives in the client's AlienDefenseConfig
// (AlienDefenseConfig.lua:102-106, tbCostSkill built from the ExtraPassive0X / ExtraPassive0XCost
// columns of the pal CSV) and the server has no equivalent yet. The UI already refuses to send the
// request when the player cannot pay, so this only leaks the upgrade to a hand-crafted CallGS
// request. Tracked as a leftover in docs/progress/022.
//
// Request:  { nPalIdx, nSkillIdx }
// Response: { nPalIdx, nSkillIdx }
[CallGSApi("AlienDefense_UpgradeSkill")]
public class AlienDefense_UpgradeSkill : CallGSHandler<AlienDefenseUpgradeSkillParam>
{
    private const uint MaxSkillLevel = 15; // 4 bits per skill slot

    private static readonly Logger Log = new(nameof(AlienDefense_UpgradeSkill));

    protected override Task<CallGSResult> HandleAsync(
        CallGSContext context,
        AlienDefenseUpgradeSkillParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return Task.FromResult(BadParam());

        var palIdx = request.PalIdx;
        var skillIdx = request.SkillIdx;

        if (palIdx == 0 || palIdx > AttrIds.AlienDefense.MaxPalNum ||
            skillIdx == 0 || skillIdx > AttrIds.AlienDefense.MaxSkillNum ||
            AttrIds.AlienDefense.PalCostSkillStartSid + palIdx > AttrIds.AlienDefense.PalCostSkillEndSid)
        {
            Log.Warn($"Alien defense skill upgrade: pal {palIdx} / skill {skillIdx} out of range (uid={player.Uid}).");
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var bitStart = (skillIdx - 1) * 4;
        var attr = player.Attributes.GetOrCreate(
            AttrIds.AlienDefense.Gid,
            AttrIds.AlienDefense.PalCostSkillStartSid + palIdx);

        var level = (attr.Val >> (int)bitStart) & 0xFu;
        if (level >= MaxSkillLevel)
        {
            Log.Warn($"Alien defense skill upgrade: pal {palIdx} skill {skillIdx} already maxed (uid={player.Uid}).");
        }
        else
        {
            var newLevel = level + 1;
            attr.Val = (attr.Val & ~(0xFu << (int)bitStart)) | (newLevel << (int)bitStart);
            DatabaseHelper.SaveDatabaseType(player.Data);
            Log.Info($"Alien defense skill upgrade by uid={player.Uid}: pal {palIdx} skill {skillIdx} {level} -> {newLevel}.");
            level = newLevel;
        }

        var sync = new NtfSyncPlayer();
        player.Attributes.SyncTo(sync, attr);

        return Task.FromResult(CallGSResult.Ok(new JsonObject
        {
            ["nPalIdx"] = palIdx,
            ["nSkillIdx"] = skillIdx,
            ["nLevel"] = level
        }, sync));
    }
}

public sealed class AlienDefenseUpgradeSkillParam
{
    [JsonPropertyName("nPalIdx")] public uint PalIdx { get; set; }
    [JsonPropertyName("nSkillIdx")] public uint SkillIdx { get; set; }
}
