using MikuSB.Configuration;
using MikuSB.Data;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Game.Activity;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.GameServer.Server;
using MikuSB.GameServer.Server.Packet.Recv.Login;
using MikuSB.Proto;
using MikuSB.TcpSharp;
using MikuSB.Util;

namespace MikuSB.GameServer.Command.Commands;

/// <summary>
/// Serves any DLC era at runtime, without editing Config.json and restarting the server.
///
/// Every "which period am I" id the client keeps (DLC activity, Defend act, 心愿寄语 act, 多诺 season,
/// 弑神斗场 act/term, 异星守护 act) is a server-seeded attribute, and every window override the DLC page
/// needs is derived from the served dlc/dlc_activities row (DlcRuntimeOverrides). Switching the era
/// therefore only takes three steps: move the served id, re-seed those attributes for every connected
/// player (including clearing the modules the new era does not have) and re-push the era-bound windows
/// and client patches. The client then rebuilds the DLC page against the new era.
///
///   /dlc                  show the served era and the eras the data has
///   /dlc 36               serve DLC 36 until the server restarts
///   /dlc 36 -save         serve DLC 36 and persist it to Config.json
///   /dlc off              serve no era (client falls back to its own time windows), until restart
///   /dlc off -save        ... and persist that too
/// </summary>
[CommandInfo("dlc", "Serve any DLC era at runtime.", "Usage: /dlc [<id>|off|status] [-save]", ["era"], [PermEnum.Admin, PermEnum.Support])]
public class CommandDlc : ICommands
{
    private static readonly Logger Log = new(nameof(CommandDlc));

    [CommandDefault]
    public async ValueTask Execute(CommandArg arg)
    {
        // "-save" is stripped into Attributes by CommandManager, so it never shows up in Args.
        var save = arg.Attributes.Any(x => x.Equals("save", StringComparison.OrdinalIgnoreCase));
        var token = arg.Args.FirstOrDefault()?.ToLowerInvariant();

        if (token is null or "status")
        {
            await arg.SendMsg(Describe());
            return;
        }

        var id = 0u;
        if (token is not "off")
        {
            if (!uint.TryParse(token, out id))
            {
                await arg.SendMsg($"'{token}' is not a DLC id. {Describe()}");
                return;
            }

            if (DlcEraPolicy.IsRetired(id))
            {
                await arg.SendMsg(
                    $"DLC {id} has been withdrawn: its data is no longer in the resource packs. " +
                    $"Retired: {DlcEraPolicy.RetiredList}. Available: {AvailableIds()}");
                return;
            }

            if (!GameData.DlcActivityData.ContainsKey(id))
            {
                await arg.SendMsg($"DLC {id} is not in dlc_activities. Available: {AvailableIds()}");
                return;
            }
        }

        if (save)
        {
            ConfigManager.Config.ServerOption.PinDlcActivityId = id;
            ConfigManager.SaveConfig();
            // The runtime pin would otherwise keep shadowing the value just written (and a later /dlc
            // without -save would silently ignore Config.json again).
            DlcRuntimeOverrides.ClearRuntimeDlcId();
        }
        else
        {
            // A runtime pin of 0 is the deliberate "serve no era", distinct from "no pin installed"
            // (ClearRuntimeDlcId), which follows Config.json.
            DlcRuntimeOverrides.SetRuntimeDlcId(id);
        }

        Log.Info($"DLC era switched to {(id == 0 ? "none" : id.ToString())} (save={save}).");

        var applied = await ApplyToOnlinePlayers();
        await arg.SendMsg($"{Describe()} Re-seeded {applied} online player(s).");
    }

    /// <summary>
    /// Re-seeds the era attributes of every connected player and re-pushes the windows/patches that
    /// depend on the era. Players who are not connected pick the new era up at their next login, since
    /// CompleteLogin runs the same seeding.
    /// </summary>
    private static async Task<int> ApplyToOnlinePlayers()
    {
        var applied = 0;
        foreach (var entry in SocketListener.Connections.Values.ToList())
        {
            if (entry is not Connection connection)
                continue;

            var player = connection.Player;
            if (player is null)
                continue;

            var sync = new NtfSyncPlayer();
            HandlerReqLogin.ApplyPinnedEra(player, sync);
            // The era's try girls are owned cards on this server, so a switch has to hand them out.
            await GachaTryService.GrantEraCharactersAsync(player);
            DlcMissionActiveService.RollDailyState(player, sync);
            if (sync.Custom.Count > 0)
                await player.SendPacket(CmdIds.NtfSyncAttr, sync);

            await HandlerReqLogin.PushDlcEraSwitch(connection);
            applied++;
        }

        return applied;
    }

    private static string Describe()
    {
        var served = DlcRuntimeOverrides.EffectiveDlcId;
        var configId = ConfigManager.Config.ServerOption.PinDlcActivityId;
        var source = DlcRuntimeOverrides.HasRuntimePin
            ? (served == 0 ? "runtime pin: off" : "runtime pin")
            : "Config.json";

        if (served == 0)
            return $"DLC era: none ({source}; Config.json PinDlcActivityId={configId}{RetiredNote(configId)}). " +
                   $"Available: {AvailableIds()}";

        var name = GameData.DlcActivityData.TryGetValue(served, out var row) ? row.MainUI : "missing row";
        var floor = DlcRuntimeOverrides.ActivationFloor();
        var floorText = floor > 0
            ? DateTimeOffset.FromUnixTimeSeconds(floor).ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : "n/a";
        return $"DLC era: {served} ({name}, {source}; Config.json PinDlcActivityId={configId}), " +
               $"era floor {floorText}. Available: {AvailableIds()}";
    }

    /// <summary>
    /// Explains a Config.json pin that no longer takes effect. Without this, a pin of a withdrawn era
    /// reads as "none (... PinDlcActivityId=3)", which looks like the server ignored the config.
    /// </summary>
    private static string RetiredNote(uint configId) =>
        DlcEraPolicy.IsRetired(configId) ? $", retired: DLC {configId} data removed" : "";

    private static string AvailableIds() =>
        string.Join(", ", GameData.DlcActivityData.Keys
            .Where(id => !DlcEraPolicy.IsRetired(id))
            .OrderBy(id => id));
}
