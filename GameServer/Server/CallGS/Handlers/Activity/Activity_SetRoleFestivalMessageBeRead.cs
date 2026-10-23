using System.Text.Json.Nodes;
using MikuSB.Database;
using MikuSB.GameServer.Game.Activity;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

// 心愿寄语 阅读一封信 (Dorm / RoleFestival).
//
// Client origin: RoleFestivalMessage.SetMessageReadState (Activity/RoleFestivalMessage.lua:222-227)
// sends { nGirlId } as soon as the letter widget opens (UMG/RoleFestivalMessage/Widget/uw_role_message.lua:29).
// The answer must be dispatched as "SetRoleFestivalMessageBeReadSuccess" (:231) - there is no s2c
// command under the request name, and S2C.Dispatch errors out on an unknown one
// (Misc/S2C.lua:31-32). The callback only refreshes the reward red point, so an empty body is fine.
//
// The read flag is (72, 3000 + NpcID) - the same offset GetBeReadCount (:200-219) counts, which is
// what gates every reward tier. The client never writes it itself.
//
// Request:  { "nGirlId": <npcId> }
// Response: { "nGirlId": <npcId> }  dispatched as SetRoleFestivalMessageBeReadSuccess
[CallGSApi("Activity_SetRoleFestivalMessageBeRead")]
public class Activity_SetRoleFestivalMessageBeRead : CallGSHandler
{
    private static readonly Logger Log = new(nameof(Activity_SetRoleFestivalMessageBeRead));

    private const string SuccessApi = "SetRoleFestivalMessageBeReadSuccess";

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player!;
        var request = ParseObject(param);
        var npcId = ReadUint(request?["nGirlId"]);

        var actId = RoleFestivalService.GetActId(player);
        if (npcId == 0 || actId == 0 || !RoleFestivalService.IsMessageOfAct(actId, npcId))
        {
            Log.Warn(
                $"Role festival read request for npc {npcId} of act {actId} from uid={player.Uid} " +
                "(unknown letter); answering without a state change.");
            return CallGSResult.Ok("{}", null, SuccessApi);
        }

        var response = new JsonObject { ["nGirlId"] = npcId };

        if (player.Attributes.GetValue(RoleFestivalService.GroupId,
                RoleFestivalService.ReadSid(npcId)) == 1)
        {
            // Re-opening a letter re-sends the request; keep the answer shape but do not rewrite it.
            return CallGSResult.Ok(response.ToJsonString(), null, SuccessApi);
        }

        var sync = new NtfSyncPlayer();
        var attr = player.Attributes.Set(RoleFestivalService.GroupId,
            RoleFestivalService.ReadSid(npcId), 1);
        player.Attributes.SyncTo(sync, attr);
        DatabaseHelper.SaveDatabaseType(player.Data);

        Log.Info(
            $"Role festival letter {npcId} of act {actId} read by uid={player.Uid} " +
            $"(read {RoleFestivalService.CountRead(player, actId)}).");

        return CallGSResult.Ok(response.ToJsonString(), sync, SuccessApi);
    }

    private static JsonObject? ParseObject(string param)
    {
        try
        {
            return string.IsNullOrWhiteSpace(param) ? null : JsonNode.Parse(param) as JsonObject;
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not parse the role festival read request: {ex.Message}");
            return null;
        }
    }

    private static uint ReadUint(JsonNode? node)
    {
        if (node is not JsonValue value)
            return 0;

        try
        {
            if (value.TryGetValue<uint>(out var number))
                return number;

            if (value.TryGetValue<string>(out var text))
                return uint.TryParse(text.Trim(), out var parsed) ? parsed : 0;
        }
        catch
        {
            // unreadable -> 0
        }

        return 0;
    }
}
