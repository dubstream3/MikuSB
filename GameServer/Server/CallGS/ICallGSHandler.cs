namespace MikuSB.GameServer.Server.CallGS;

public interface ICallGSHandler
{
    Task Handle(Connection connection, string param, ushort seqNo);
}
