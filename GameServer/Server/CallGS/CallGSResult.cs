using System.Text.Json;
using System.Text.Json.Nodes;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS;

public sealed class CallGSResult
{
    private CallGSResult(bool sendResponse, string argument, NtfSyncPlayer? sync, string? api)
    {
        SendResponse = sendResponse;
        Argument = argument;
        Sync = sync;
        Api = api;
    }

    public bool SendResponse { get; }
    public string Argument { get; }
    public NtfSyncPlayer? Sync { get; }
    public string? Api { get; }

    public static CallGSResult Ok(string argument = "{}", NtfSyncPlayer? sync = null, string? api = null)
    {
        return new CallGSResult(true, argument, sync, api);
    }

    public static CallGSResult Ok(string argument, string api)
    {
        return Ok(argument, null, api);
    }

    public static CallGSResult Ok(JsonNode? argument, NtfSyncPlayer? sync = null, string? api = null)
    {
        return Ok(argument?.ToJsonString(CallGSJson.Options) ?? "null", sync, api);
    }

    public static CallGSResult Error(string key, NtfSyncPlayer? sync = null, string? api = null)
    {
        return Ok(JsonSerializer.Serialize(new { sErr = key }, CallGSJson.Options), sync, api);
    }

    public static CallGSResult Error(string key, string api)
    {
        return Error(key, null, api);
    }

    public static CallGSResult NoResponse()
    {
        return new CallGSResult(false, string.Empty, null, null);
    }
}
