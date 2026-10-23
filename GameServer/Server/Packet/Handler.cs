namespace MikuSB.GameServer.Server.Packet;

public abstract class Handler
{
    public abstract Task OnHandle(Connection connection, byte[] data, ushort SeqNo = 0);
}