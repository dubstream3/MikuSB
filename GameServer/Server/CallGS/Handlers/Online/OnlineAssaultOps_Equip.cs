using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Online;

// 进击行动 装备/卸下芯片.
//
// Client origin: OnlineAssaultOps:ReqEquipChip (Launch/Online/Classes/OnlineAssaultOps.lua:1416-1451)
// sends { nOnlineId, nFormationId, nChipId, nPos, bUnEquip } behind UI.ShowConnection(); its s2c
// callback (:1433-1451) only checks sError and then refreshes the panel out of the attributes
// (umg_assaultops_buff.lua:664 OnReceiveUpdate ignores the payload), so the answer carries the new
// attribute values and nothing else.
//
// Request:  { "nFormationId": 1..3, "nPos": 1..10, "nChipId": <chip id>, "bUnEquip": bool? }
// Response: {} (+ attribute sync); failures use "sError", the key that callback reads.
[CallGSApi("OnlineAssaultOps_Equip")]
public class OnlineAssaultOps_Equip : CallGSHandler
{
    private static readonly Logger Log = new(nameof(OnlineAssaultOps_Equip));

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player!;
        var request = CallGSParam.Parse(param);

        var formationId = CallGSParam.ReadUint(request?["nFormationId"]);
        var position = CallGSParam.ReadUint(request?["nPos"]);
        var chipId = CallGSParam.ReadUint(request?["nChipId"]);
        var unequip = CallGSParam.ReadBool(request?["bUnEquip"]);

        if (!AssaultOpsBag.IsValidSlot(formationId, position))
        {
            Log.Warn(
                $"Assault ops equip with an invalid slot (formation {formationId}, position {position}) " +
                $"from uid={player.Uid}.");
            return Task.FromResult(CallGSParam.Failure("error.BadParam"));
        }

        var sync = new NtfSyncPlayer();
        if (unequip || chipId == 0)
        {
            AssaultOpsBag.SetFormationChip(player, formationId, position, 0, sync);
            Log.Info($"Assault ops formation {formationId} position {position} cleared by uid={player.Uid}.");
        }
        else
        {
            // Mirror of the client's own ownership filter (GetAllShowChip): a chip the account does not
            // own cannot be equipped, so a stale panel cannot inject one.
            if (AssaultOpsBag.GetChipLevel(player, chipId) == 0)
            {
                Log.Warn($"Assault ops equip of unowned chip {chipId} by uid={player.Uid}.");
                return Task.FromResult(CallGSParam.Failure("error.BadParam"));
            }

            AssaultOpsBag.SetFormationChip(player, formationId, position, chipId, sync);
            Log.Info(
                $"Assault ops formation {formationId} position {position} = chip {chipId} (uid={player.Uid}).");
        }

        AssaultOpsBag.Save(player);
        return Task.FromResult(CallGSResult.Ok("{}", sync));
    }
}
