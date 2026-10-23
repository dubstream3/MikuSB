namespace MikuSB.GameServer.Server.CallGS;

[AttributeUsage(AttributeTargets.Class)]
public class CallGSApiAttribute(string api) : Attribute
{
    public string Api { get; } = api;
}
