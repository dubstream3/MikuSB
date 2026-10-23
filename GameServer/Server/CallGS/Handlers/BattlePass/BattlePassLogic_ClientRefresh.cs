using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using System.Globalization;
using System.Text.Json.Nodes;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BattlePass;

[CallGSApi("BattlePassLogic_ClientRefresh")]
public class BattlePassLogic_ClientRefresh : CallGSHandler
{
    private const uint GroupId = AttrIds.BattlePass.Gid;
    private const uint CurIdSid = AttrIds.BattlePass.CurrentIdSid;

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var now = DateTime.Now;
        var battlePass = ResolveCurrent(GameData.BattlePassTimeData.Values, now);
        var player = context.Connection.Player!;
        var sync = new NtfSyncPlayer();

        if (battlePass == null)
        {
            SetAttr(player, CurIdSid, 0, sync);
            return Task.FromResult(CallGSResult.Ok("{}", sync));
        }

        SetAttr(player, CurIdSid, battlePass.Id, sync);

        var response = new JsonObject
        {
            ["nId"] = battlePass.Id,
            ["nStartTime"] = ToUnixSeconds(ParseConfigTime(battlePass.StartTime)),
            ["nEndTime"] = ToUnixSeconds(ParseConfigTime(battlePass.EndTime))
        };

        return Task.FromResult(CallGSResult.Ok(response.ToJsonString(), sync));
    }

    private static BattlePassTimeExcel? ResolveCurrent(IEnumerable<BattlePassTimeExcel> configs, DateTime now)
    {
        var parsed = configs
            .Select(x => new
            {
                Config = x,
                Start = ParseConfigTime(x.StartTime),
                End = ParseConfigTime(x.EndTime)
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

    private static DateTime? ParseConfigTime(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var normalized = raw.Trim().Trim('[', ']');
        if (normalized.Length != 12)
            return null;

        return DateTime.TryParseExact(
            normalized,
            "yyyyMMddHHmm",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var value)
            ? value
            : null;
    }

    private static long ToUnixSeconds(DateTime? value)
    {
        return value.HasValue ? new DateTimeOffset(value.Value).ToUnixTimeSeconds() : 0L;
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
