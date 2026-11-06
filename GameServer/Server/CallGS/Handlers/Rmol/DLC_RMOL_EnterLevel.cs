using MikuSB.Util;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rmol;

/// <summary>
/// DLC 绘梦沙盘（RMOL，大富翁 + 恋爱养成）的关卡进入。
///
/// The client owns the map, the dice and the whole board: <c>_Mod/RMOL/DLC_RMOL.lua</c>'s
/// <c>DLC_RMOL_Level:Enter</c> registers an event handler for <c>DLC_RMOL_EnterLevel_Return</c> and,
/// once that fires, calls <c>Map.Open(RMOL_Config:GetLevel(ID).MapId, "", ...)</c> locally. The only
/// thing it needs from the server is the echo "yes, session S level L", which is what this handler
/// answers. Returning an error here is what made 立即出发 look dead before: the request had no handler
/// at all, so the login-side CallGS dispatcher logged "No handler for CallGS API" and never replied.
/// </summary>
[CallGSApi("DLC_RMOL_EnterLevel")]
public class DLC_RMOL_EnterLevel : CallGSHandler<DlcRmolEnterLevelParam>
{
    private static readonly Logger Log = new("DLC_RMOL");

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, DlcRmolEnterLevelParam req)
    {
        if (req == null || req.Session <= 0 || req.Level <= 0)
        {
            return Task.FromResult(CallGSResult.Ok(new JsonObject { ["Err"] = "error.BadParam" }.ToJsonString()));
        }

        var response = new JsonObject
        {
            ["Session"] = req.Session,
            ["Level"] = req.Level,
            ["Temp"] = new JsonArray()
        };

        Log.Info($"DLC_RMOL_EnterLevel: uid={context.Connection?.Player?.Uid} session={req.Session} level={req.Level} girl={req.Girl}.");

        return Task.FromResult(CallGSResult.Ok(response.ToJsonString()));
    }
}

public sealed class DlcRmolEnterLevelParam
{
    [JsonPropertyName("Session")]
    public int Session { get; set; }

    [JsonPropertyName("Level")]
    public int Level { get; set; }

    [JsonPropertyName("Girl")]
    public int Girl { get; set; }

    [JsonPropertyName("SkillIDList")]
    public List<int> SkillIdList { get; set; } = [];
}
