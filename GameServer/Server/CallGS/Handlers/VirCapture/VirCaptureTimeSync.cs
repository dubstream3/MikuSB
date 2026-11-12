using MikuSB.Data;
using MikuSB.GameServer.Server.CallGS;
using MikuSB.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text.Json.Nodes;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// Client-side time synchronisation for 新星开拓, pushed on login.
///
/// The client decides whether a trial period is open from its own `trial_timelist` row, while the
/// server decides from the served DLC era (<see cref="VirCaptureEraTime"/>). Without a push the two
/// disagree: the last shipped period ended 2026-05-14, so an era-pinned server would accept a trial
/// entry the player cannot even see. `VirCapture_GMChangeTime` is the channel the client already
/// implements (VirCapture.lua:1847-1905) and it rewrites `nStartTime / nEndTime / nAwardTime /
/// tbCondition` on the local rows.
///
/// Known side effect (client-side, documented in 072): the same callback unconditionally clears the
/// `tbCondition` of every tower chapter and level, so pushing trial windows also removes the client
/// side tower gating. The server still enforces those conditions on entry
/// (<see cref="VirCaptureTower_EnterLevel"/>), so the visible difference is a chapter that can be
/// opened but reports 解锁条件不足.
/// </summary>
internal static class VirCaptureTimeSync
{
    private static readonly Logger Logger = new("VirCapture.TimeSync");

    /// <summary>Only an era-pinned server needs to overwrite the shipped client windows.</summary>
    public static bool ShouldPush => VirCaptureEraTime.IsEraPinned;

    public static JsonObject BuildPayload()
    {
        var (start, end) = VirCaptureEraTime.ClientWindow();
        var trials = new JsonArray();

        foreach (var row in GameData.VirCaptureTrialTimeData.Values.OrderBy(x => x.Id))
        {
            trials.Add(new JsonObject
            {
                ["nId"] = row.Id,
                ["nStartTime"] = start,
                ["nEndTime"] = end,
                ["nAwardTime"] = end,
                ["tbCondition"] = ToNode(row.ConditionRaw)
            });
        }

        return new JsonObject
        {
            ["tbTrial"] = trials
        };
    }

    private static JsonNode ToNode(JToken? token) =>
        token == null ? new JsonObject() : JsonNode.Parse(token.ToString(Formatting.None))!;

    public static async Task PushAsync(Connection connection)
    {
        if (!ShouldPush)
            return;

        if (GameData.VirCaptureTrialTimeData.Count == 0)
        {
            Logger.Warn("no trial period rows loaded; skipping VirCapture_GMChangeTime");
            return;
        }

        var payload = BuildPayload();
        Logger.Info($"pushing VirCapture_GMChangeTime with {GameData.VirCaptureTrialTimeData.Count} trial period(s)");
        await CallGSRouter.SendScript(connection, "VirCapture_GMChangeTime", payload.ToJsonString());
    }
}
