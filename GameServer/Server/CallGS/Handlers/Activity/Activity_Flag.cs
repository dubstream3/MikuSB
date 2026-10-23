using System.Globalization;
using System.Text.Json.Nodes;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

// Client side contract: Activity/Activity.lua.
//
// Report (Activity.Quest_Flag, lines 1377-1387): the client tells the server that a quest red dot
// was looked at -> me:CallGS("Activity_Flag", { nId = nId, nType = nType or 0 }).
//
// Reply (s2c.Register('ActivityQuest_Flag', ...) line 1461): the names differ on purpose. The
// callback runs Activity.SetDeviceRed(tbParam.nId), triggers Event.ActivityQuestFlag and refreshes
// the Activity / NewTask UIs.
//
// Echoing "Activity_Flag" back (which is what CallGSRouter.SendUnavailableResponse did while no
// handler existed) makes the client raise "Misc/S2C.lua:32: s2c.Dispatch error. No command named:
// Activity_Flag", because no such s2c command is registered. Answer under ActivityQuest_Flag
// instead, mirroring Activity_GetAward -> ActivityQuest_GetReward.
[CallGSApi("Activity_Flag")]
public class Activity_Flag : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var request = JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject
                      ?? new JsonObject();

        var nId = ParseUInt(request["nId"]) ?? 0;
        var nType = ParseUInt(request["nType"]) ?? 0;

        // The client's callback only needs nId (Activity.SetDeviceRed) and nType, so echo both back.
        var response = new JsonObject
        {
            ["nId"] = nId,
            ["nType"] = nType
        };

        Logger.GetByClassName().Info(
            $"Activity flag nId={nId} nType={nType} for uid={context.Connection.Player?.Uid}.");

        return Task.FromResult(CallGSResult.Ok(response.ToJsonString(), null, "ActivityQuest_Flag"));
    }

    private static uint? ParseUInt(JsonNode? node)
    {
        if (node == null)
            return null;

        var raw = node.ToString();
        if (uint.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return value;

        return null;
    }
}
