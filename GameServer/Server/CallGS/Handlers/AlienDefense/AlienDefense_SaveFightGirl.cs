using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.AlienDefense;

// 异星守护 (alien defense / Mow) support-girl selection. uw_mow_girl.lua:70-73 OnClose calls
// AlienDefense.SaveFightGirl(tb[1] or 0) (DLC/AlienDefense/AlienDefense.lua:1106-1108):
//
//     { nGirlId }
//
// nGirlId is the girl's roster index in dlc/AlienDefense/aliendefense_girl.json (1..MaxGirlNum) and
// 0 clears the selection; it is stored raw in (143,7) (GetFightGirl :245-247).
//
// Like SaveFightPal there is no s2c callback, so the new value is delivered as an attribute sync
// rather than a script response (Misc/S2C.lua:29-35 raises on unknown commands).
//
// Request:  { nGirlId }
// Response: none (attribute sync only)
[CallGSApi("AlienDefense_SaveFightGirl")]
public class AlienDefense_SaveFightGirl : CallGSHandler<AlienDefenseSaveFightGirlParam>
{
    private static readonly Logger Log = new(nameof(AlienDefense_SaveFightGirl));

    protected override async Task<CallGSResult> HandleAsync(
        CallGSContext context,
        AlienDefenseSaveFightGirlParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return BadParam();

        var girlId = request.GirlId;
        if (girlId > AttrIds.AlienDefense.MaxGirlNum)
        {
            Log.Warn($"Alien defense girl {girlId} is out of range (uid={player.Uid}).");
            girlId = 0;
        }

        var attr = player.Attributes.GetOrCreate(AttrIds.AlienDefense.Gid, AttrIds.AlienDefense.FightGirlSid);
        attr.Val = girlId;

        var sync = new NtfSyncPlayer();
        player.Attributes.SyncTo(sync, attr);
        DatabaseHelper.SaveDatabaseType(player.Data);
        await player.SendPacket(CmdIds.NtfSyncAttr, sync);

        Log.Info($"Alien defense fight girl saved by uid={player.Uid}: girl={girlId}.");

        return CallGSResult.NoResponse();
    }
}

public sealed class AlienDefenseSaveFightGirlParam
{
    [JsonPropertyName("nGirlId")] public uint GirlId { get; set; }
}
