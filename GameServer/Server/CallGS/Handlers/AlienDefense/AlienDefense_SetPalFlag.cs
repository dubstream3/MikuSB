using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.AlienDefense;

// 异星守护 (alien defense / Mow) "new unlock seen" flags. The roster widgets collect every entry
// whose red dot the player just looked at and flush them on close:
//
//     uw_mow_team.lua:86-89 / :115-118  ->  { nIdx, nType = 1 }
//     uw_mow_girl.lua:70-73 / :102-105  ->  { nIdx, nType, bGirl = true }
//     umg_mow_skill.lua:77-102          ->  { nIdx, nType = 2 | 3 }
//
// AlienDefense.SetPalFlag (DLC/AlienDefense/AlienDefense.lua:1082-1089) forwards that list as a BARE
// JSON array, not an object, which is why TRequest is a List here.
//
// nType is the unlock slot: 1 = the pal/girl itself, 2 = active skill, 3 = passive skill. Each
// roster entry keeps two bits per slot in (143, 251+nIdx) for pals and (143, 551+nIdx) for girls
// (:29-33, GetPalUnlockState :329-359): bit (nType-1)*2 is "unlocked", bit (nType-1)*2+1 is the
// same fact already seen. CheckPalRed (:376-401) shows the red dot while unlocked && not seen, so
// this api only ever SETS the seen bit.
//
// The client answers with UI.Call2('MowMain', 'RefreshRed'), so the reply payload is unused, but it
// must be sent for that refresh to happen.
//
// Request:  [ { nIdx, nType, bGirl } ]
// Response: {}
[CallGSApi("AlienDefense_SetPalFlag")]
public class AlienDefense_SetPalFlag : CallGSHandler<List<AlienDefensePalFlagEntry>>
{
    private static readonly Logger Log = new(nameof(AlienDefense_SetPalFlag));

    protected override Task<CallGSResult> HandleAsync(
        CallGSContext context,
        List<AlienDefensePalFlagEntry> request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return Task.FromResult(BadParam());

        var sync = new NtfSyncPlayer();
        var flagged = 0;

        foreach (var entry in request)
        {
            if (entry.NType is < 1 or > 3)
            {
                Log.Warn($"Alien defense seen flag: unknown unlock type {entry.NType} for idx {entry.Idx} (uid={player.Uid}).");
                continue;
            }

            var maxIdx = entry.Girl ? AttrIds.AlienDefense.MaxGirlNum : AttrIds.AlienDefense.MaxPalNum;
            var startSid = entry.Girl ? AttrIds.AlienDefense.GirlUnlockStartSid : AttrIds.AlienDefense.PalUnlockStartSid;
            var endSid = entry.Girl ? AttrIds.AlienDefense.GirlUnlockEndSid : AttrIds.AlienDefense.PalUnlockEndSid;

            if (entry.Idx == 0 || entry.Idx > maxIdx || startSid + entry.Idx > endSid)
            {
                Log.Warn($"Alien defense seen flag: idx {entry.Idx} out of range (girl={entry.Girl}, uid={player.Uid}).");
                continue;
            }

            var flagBit = (int)((entry.NType - 1) * 2 + 1);
            var attr = player.Attributes.GetOrCreate(AttrIds.AlienDefense.Gid, startSid + entry.Idx);
            var updated = attr.Val | (1u << flagBit);
            if (updated == attr.Val)
                continue;

            attr.Val = updated;
            player.Attributes.SyncTo(sync, attr);
            flagged++;
        }

        if (flagged > 0)
            DatabaseHelper.SaveDatabaseType(player.Data);

        Log.Info($"Alien defense seen flags: {flagged}/{request.Count} marked for uid={player.Uid}.");

        return Task.FromResult(CallGSResult.Ok(new JsonObject(), sync));
    }
}

public sealed class AlienDefensePalFlagEntry
{
    [JsonPropertyName("nIdx")] public uint Idx { get; set; }
    [JsonPropertyName("nType")] public uint NType { get; set; }
    [JsonPropertyName("bGirl")] public bool Girl { get; set; }
}
