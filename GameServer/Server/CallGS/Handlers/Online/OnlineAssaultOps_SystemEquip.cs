using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Online;

// 进击行动 一键应用系统推荐.
//
// Client origin: OnlineAssaultOps:ReqSystemEquip (Launch/Online/Classes/OnlineAssaultOps.lua:1534-1574)
// sends { nOnlineId, nFormationId, nSysModIndx, tbEquipList = OnlineAssaultOps:GetSystemRecommendList(nSysModIndx) }
// where every row of tbEquipList is [position, chipId]; the callback (:1550-1574) behaves like the
// equip one and refreshes the panel from the attributes.
//
// Request:  { "nFormationId": 1..3, "nSysModIndx": <scheme>, "tbEquipList": [[pos, chipId], ...] }
// Response: { "bFormation": true } (+ attribute sync of the formation and the scheme flag)
[CallGSApi("OnlineAssaultOps_SystemEquip")]
public class OnlineAssaultOps_SystemEquip : CallGSHandler
{
    private static readonly Logger Log = new(nameof(OnlineAssaultOps_SystemEquip));

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player!;
        var request = CallGSParam.Parse(param);

        var formationId = CallGSParam.ReadUint(request?["nFormationId"]);
        var schemeIndex = CallGSParam.ReadUint(request?["nSysModIndx"]);

        if (formationId < 1 || formationId > AttrIds.AssaultOps.FormationCount)
        {
            Log.Warn($"Assault ops system-equip with unknown formation {formationId} from uid={player.Uid}.");
            return Task.FromResult(CallGSParam.Failure("error.BadParam"));
        }

        var sync = new NtfSyncPlayer();
        var applied = 0;
        var skipped = 0;

        if (request?["tbEquipList"] is JsonArray rows)
        {
            foreach (var row in rows)
            {
                if (row is not JsonArray pair || pair.Count < 2)
                    continue;

                var position = CallGSParam.ReadUint(pair[0]);
                var chipId = CallGSParam.ReadUint(pair[1]);
                if (!AssaultOpsBag.IsValidSlot(formationId, position))
                    continue;

                // The recommendation is generated from the shipped table and may name chips this
                // account does not own: skip those instead of failing the whole request.
                if (chipId != 0 && AssaultOpsBag.GetChipLevel(player, chipId) == 0)
                {
                    skipped++;
                    continue;
                }

                AssaultOpsBag.SetFormationChip(player, formationId, position, chipId, sync);
                applied++;
            }
        }

        AssaultOpsBag.SetFormationSysFlag(player, formationId, schemeIndex, sync);
        AssaultOpsBag.Save(player);
        Log.Info(
            $"Assault ops formation {formationId} filled from system scheme {schemeIndex} by uid={player.Uid}: " +
            $"{applied} position(s), {skipped} skipped.");

        return Task.FromResult(CallGSResult.Ok(new JsonObject { ["bFormation"] = true }, sync));
    }
}
