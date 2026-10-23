namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;

// Called when a season room is cleared. The client does not process the response.
// param: {"nNodeId": int, "tbKill": [...], "tbMonster": [...]}
[CallGSApi("Rogue3D_SeasonRoomSettlement")]
public class Rogue3D_SeasonRoomSettlement : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        return Task.FromResult(CallGSResult.Ok("{}"));
    }
}
