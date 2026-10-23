namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;

// Selects the Rogue3D game mode (nModeID: 1=infinity, 2=normal, 3=season).
// param: {"nModeID": int}
// Response: {} on success, {"sErr": "key"} on failure
[CallGSApi("Rogue3D_SelectMode")]
public class Rogue3D_SelectMode : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var sync = context.Player.Rogue3DManager.EnsureUnlockState();
        return Task.FromResult(CallGSResult.Ok("{}", sync));
    }
}
