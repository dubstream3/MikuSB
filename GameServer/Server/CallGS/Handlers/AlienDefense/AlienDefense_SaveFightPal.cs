using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.AlienDefense;

// 异星守护 (alien defense / Mow) party save. Closing the partner team widget
// (UMG/Mow/Widgets/uw_mow_team.lua:80-90 OnClose, :115-118 SetFlag) sends the checked slots through
// AlienDefense.SaveFightPal (DLC/AlienDefense/AlienDefense.lua:1075-1078):
//
//     { tbPalIdx = { pal1, pal2 } }
//
// (143,5) packs both slots -- pal 1 in bits 0-15, pal 2 in bits 16-31 (AlienDefense.lua:19,
// GetFightPal :250-261) -- and an empty list, which is what the widget sends when nothing is
// checked, clears the party.
//
// The client registers no s2c callback for this api, and Misc/S2C.lua:29-35 errors out on an
// unknown command, so no script response may be sent. The packed attribute still has to reach the
// client as NtfSyncAttr, otherwise GetFightPal keeps reporting the previous party until relog.
//
// Request:  { tbPalIdx = { pal1, pal2 } }
// Response: none (attribute sync only)
[CallGSApi("AlienDefense_SaveFightPal")]
public class AlienDefense_SaveFightPal : CallGSHandler<AlienDefenseSaveFightPalParam>
{
    private static readonly Logger Log = new(nameof(AlienDefense_SaveFightPal));

    protected override async Task<CallGSResult> HandleAsync(
        CallGSContext context,
        AlienDefenseSaveFightPalParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return BadParam();

        var pals = request.PalIndices ?? [];
        var packed = 0u;

        for (var slot = 0; slot < 2 && slot < pals.Count; slot++)
        {
            var palIdx = pals[slot];
            if (palIdx == 0)
                continue;

            if (palIdx > AttrIds.AlienDefense.MaxPalNum)
            {
                Log.Warn($"Alien defense party: pal {palIdx} in slot {slot + 1} is out of range (uid={player.Uid}).");
                continue;
            }

            packed |= palIdx << (slot * 16);
        }

        var attr = player.Attributes.GetOrCreate(AttrIds.AlienDefense.Gid, AttrIds.AlienDefense.FightPalSid);
        attr.Val = packed;

        var sync = new NtfSyncPlayer();
        player.Attributes.SyncTo(sync, attr);
        DatabaseHelper.SaveDatabaseType(player.Data);
        await player.SendPacket(CmdIds.NtfSyncAttr, sync);

        Log.Info($"Alien defense party saved by uid={player.Uid}: [{string.Join(", ", pals)}] -> (143,5)={packed}.");

        return CallGSResult.NoResponse();
    }
}

public sealed class AlienDefenseSaveFightPalParam
{
    [JsonPropertyName("tbPalIdx")] public List<uint>? PalIndices { get; set; }
}
