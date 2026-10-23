using System.Text.Json.Nodes;
using MikuSB.Data;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Daily;

// The client asks for the limited-time windows of every repeatable daily dungeon
// (Daily.Req_GetActivityInfo -> me:CallGS("Daily_GetActivityInfo", {})) and stores the answer in
// Daily.tbCfg[nID].tbActivity (Launch/Daily/Daily.lua:329-348).
//
// Contract, from Daily.IsInActivityOpenTime (Launch/Daily/Daily.lua:69-77):
//   for _, activityInfo in pairs(tbActivity or {}) do
//       if #activityInfo == 3 and activityInfo[3] == 3
//          and ParseTime(activityInfo[1]) < curTime and ParseTime(activityInfo[2]) > curTime then
//           return true
//   end
// so every value is an array of {startTime, endTime, type} triples with type == 3, and the times are
// parsed with ParseTime ("YYYYmmdd[HH[MM[SS]]]", numbers or numeric strings).
//
// The windows shipped in the client's own daily/daily.txt are years in the past ({{20220331, ...}}),
// which is why daily dungeons ask the server. We answer with a rolling window so the dungeons are
// available regardless of the current date.
//
// Response: {nID: [["<yyyyMMdd>", "<yyyyMMdd>", 3]]}
[CallGSApi("Daily_GetActivityInfo")]
public class Daily_GetActivityInfo : CallGSHandler
{
    private const int WindowBeforeDays = 30;
    private const int WindowAfterDays = 30;
    private const int LimitedTimeType = 3;

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var now = DateTime.Now;
        var start = now.AddDays(-WindowBeforeDays).ToString("yyyyMMdd");
        var end = now.AddDays(WindowAfterDays).ToString("yyyyMMdd");

        var payload = new JsonObject();
        foreach (var id in GameData.DailyData.Keys)
        {
            payload[id.ToString()] = new JsonArray
            {
                new JsonArray { start, end, LimitedTimeType }
            };
        }

        return Task.FromResult(CallGSResult.Ok(payload.ToJsonString()));
    }
}
