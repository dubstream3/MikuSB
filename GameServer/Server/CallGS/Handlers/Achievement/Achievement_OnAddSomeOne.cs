using System.Text.Json.Serialization;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Achievement;

// The client tells the server that an achievement trigger fired (Achievement_Function.lua:560,
// Achievement.lua:1186, Chat.lua:221, InteractAnim.lua:174, ...). The real server keeps the counters;
// here the only consumer is the DLC mission board (水城观光), whose rows carry the same trigger Type.
// No CallGS response is expected, so the progress delta is pushed as a plain NtfSyncAttr packet.
// param: { nType, tbParam? }
[CallGSApi("Achievement_OnAddSomeOne")]
public class Achievement_OnAddSomeOne : CallGSHandler<AchievementOnAddSomeOneParam>
{
    protected override async Task<CallGSResult> HandleAsync(
        CallGSContext context,
        AchievementOnAddSomeOneParam request)
    {
        var player = context.Connection.Player;
        if (player == null || request.Type == 0)
            return CallGSResult.NoResponse();

        var sync = new NtfSyncPlayer();
        DlcMissionService.AdvanceByType(player, request.Type, sync);

        if (sync.Custom.Count > 0)
            await player.SendPacket(CmdIds.NtfSyncAttr, sync);

        return CallGSResult.NoResponse();
    }
}

public sealed class AchievementOnAddSomeOneParam
{
    [JsonPropertyName("nType")] public uint Type { get; set; }
}
