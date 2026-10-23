namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;

// Called when a room is cleared. Client s2c handler is empty — just acknowledge.
// param: {"nNodeId": int, "tbKill": [...], "tbMonster": [...]}
[CallGSApi("Rogue3D_RoomSettlement")]
public class Rogue3D_RoomSettlement : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        return Task.FromResult(CallGSResult.Ok("{}"));
    }
}
