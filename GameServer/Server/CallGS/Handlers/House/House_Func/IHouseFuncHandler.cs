using MikuSB.GameServer.Server.CallGS;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class HouseFuncAttribute(string funcName) : Attribute
{
    public string FuncName { get; } = funcName;
}

public interface IHouseFuncHandler
{
    Task<CallGSResult> Handle(CallGSContext context, string param);
}
