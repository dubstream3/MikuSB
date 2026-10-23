using MikuSB.Database.Account;
using MikuSB.Proto;
using MikuSB.TcpSharp;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.Packet.Recv.Login;

// The client resends ReqReconnect on every new TCP connection when it still holds a session
// (its pid/session id from the previous login). Without a handler the connection stays
// player-less: CallGS requests then run against a null player and the client never recovers,
// which forced a full client relaunch after every server rebuild. The reconnect simply replays
// the login sequence on the new connection.
[Opcode(CmdIds.ReqReconnect)]
public class HandlerReqReconnect : Handler
{
    private static readonly Logger Logger = new("ReqReconnect");

    public override async Task OnHandle(Connection connection, byte[] data, ushort seqNo)
    {
        var req = ReqReconnect.Parser.ParseFrom(data);
        var account = ResolveAccount(connection, req);
        if (account == null)
        {
            Logger.Warn($"Rejected reconnect: pid={req.Pid}, sessionId={req.SessionId}");
            await connection.SendPacket(CmdIds.NtfLogout);
            return;
        }

        Logger.Info($"Reconnect accepted: pid={req.Pid}, uid={account.Uid}");
        await HandlerReqLogin.CompleteLogin(connection, account, true);
    }

    private static AccountData? ResolveAccount(Connection connection, ReqReconnect req)
    {
        var options = ConfigManager.Config.ServerOption;
        var loopback = LanAddress.IsLocal(connection.RemoteEndPoint);

        // With per-identity logins every machine is handed its own uid when it logs in, so the pid the
        // client sends back is already the right account and has to win over any pin. This matters
        // because the pin (PinLoginIdentity) is set on this server, and letting it answer every
        // reconnect would drag the guest back onto the host's player.
        //
        // Note: a LAN client that deliberately logged in as the pinned name (e.g. the host's own
        // account) is NOT rejected here — whoever typed the name owns it, and refusing it would make
        // the client re-run the SDK login, get the same uid back, and loop forever.
        if (options.PerIdentityAccount && req.Pid is > 0 and <= int.MaxValue)
        {
            var byUid = AccountData.GetAccountByUid((int)req.Pid);
            if (byUid != null)
                return byUid;
        }

        // A guest keeps its own account even on the reconnect path.
        //
        // ReqReconnect carries only the client's cached pid/session, and the host identity is pinned,
        // so a guest that still holds the host's session (which happens as soon as it ever logged in
        // before the guest mapping existed) used to be re-accepted as the host's uid. Two clients
        // then shared one account: the online room roster saw one player, /lua @uid picked an
        // arbitrary one of the two connections, and both machines fought over the same player data.
        var guestPinned = options.LanGuestLoginIdentity;
        if (!string.IsNullOrWhiteSpace(guestPinned) && !loopback)
        {
            var guest = AccountData.GetAccountByEmail(guestPinned);
            if (guest != null)
            {
                Logger.Info($"LAN guest reconnect from {connection.RemoteEndPoint} maps to account " +
                            $"'{guestPinned}' (uid={guest.Uid}), not the pinned host account.");
                return guest;
            }

            Logger.Warn($"LAN guest reconnect from {connection.RemoteEndPoint} could not resolve account " +
                        $"'{guestPinned}'; falling back to the pinned identity.");
        }

        var pinned = options.PinLoginIdentity;
        if (!string.IsNullOrWhiteSpace(pinned))
        {
            var pinnedAccount = AccountData.GetAccountByEmail(pinned);
            if (pinnedAccount != null)
                return pinnedAccount;
        }

        if (req.Pid is > 0 and <= int.MaxValue)
        {
            var byUid = AccountData.GetAccountByUid((int)req.Pid);
            if (byUid != null)
                return byUid;
        }

        return null;
    }
}
