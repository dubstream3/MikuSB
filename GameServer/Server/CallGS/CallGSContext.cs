using MikuSB.GameServer.Game.Player;

namespace MikuSB.GameServer.Server.CallGS;

public sealed class CallGSContext
{
    public required Connection Connection { get; init; }
    public required PlayerInstance Player { get; init; }
    public required ushort SequenceNumber { get; init; }
    public required string RawParam { get; init; }
}
