using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.Packet.Recv.Login;
using MikuSB.Proto;
using MikuSB.Util;
using System.Text.Json.Nodes;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DLC;

[CallGSApi("DLCLogic_CheckOpenAct")]
public class DLCLogic_CheckOpenAct : CallGSHandler
{
    private const uint GroupId = AttrIds.Dlc.Gid;
    private const uint ActIdSid = AttrIds.Dlc.ActIdSid;

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var now = DateTime.Now;
        var pinnedId = DlcRuntimeOverrides.EffectiveDlcId;
        var act = ResolvePinned(pinnedId) ?? ResolveCurrent(GameData.DlcActivityData.Values, now);
        if (act == null)
        {
            return CallGSResult.Ok("{\"bOpen\":false}");
        }

        var player = context.Connection.Player!;
        var sync = new NtfSyncPlayer();
        SetAttr(player, ActIdSid, act.Id, sync);

        var response = new JsonObject
        {
            ["bOpen"] = true,
            ["nId"] = act.Id,
            ["nStartTime"] = DlcRuntimeOverrides.ToUnixSeconds(DlcRuntimeOverrides.ParseConfigTime(act.EnterStartTime)),
            ["nEndTime"] = DlcRuntimeOverrides.ToUnixSeconds(DlcRuntimeOverrides.ParseConfigTime(act.CloseEndTime))
        };

        // The client rebuilds the DLC UI right after this answer and hides/marks "expired" every
        // entry whose local window is not current, so every window override (DLC row, chapter,
        // defend act, shop tabs are pulled separately) is pushed before answering.
        await HandlerReqLogin.PushDlcWindowOverrides(context.Connection);

        return CallGSResult.Ok(response.ToJsonString(), sync);
    }

    /// <summary>
    /// Returns the activity configured through <c>ServerOption.PinDlcActivityId</c>, if any.
    /// </summary>
    private static DlcActivityExcel? ResolvePinned(uint pinnedId)
    {
        if (pinnedId == 0)
            return null;

        if (GameData.DlcActivityData.TryGetValue(pinnedId, out var pinned))
            return pinned;

        Logger.GetByClassName().Warn($"PinDlcActivityId={pinnedId} was not found in dlc/dlc_activities, falling back to time-window resolution.");
        return null;
    }

    private static DlcActivityExcel? ResolveCurrent(IEnumerable<DlcActivityExcel> configs, DateTime now)
    {
        var parsed = configs
            .Select(x => new
            {
                Config = x,
                Start = DlcRuntimeOverrides.ParseConfigTime(x.EnterStartTime),
                End = DlcRuntimeOverrides.ParseConfigTime(x.CloseEndTime)
            })
            .Where(x => x.Start.HasValue && x.End.HasValue)
            .OrderBy(x => x.Start)
            .ToList();

        var current = parsed.FirstOrDefault(x => x.Start <= now && now < x.End);
        if (current != null)
            return current.Config;

        var latestStarted = parsed.LastOrDefault(x => x.Start <= now && x.End > x.Start);
        return latestStarted?.Config;
    }

    private static void SetAttr(PlayerInstance player, uint sid, uint value, NtfSyncPlayer sync)
    {
        var attr = player.Attributes.GetOrCreate(GroupId, sid);
        if (attr.Val != value)
        {
            attr.Val = value;
            player.Attributes.SyncTo(sync, attr);
        }
    }
}
