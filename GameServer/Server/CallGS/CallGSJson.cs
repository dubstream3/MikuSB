using System.Text.Json;

namespace MikuSB.GameServer.Server.CallGS;

public static class CallGSJson
{
    public static JsonSerializerOptions Options { get; } = new();
}
