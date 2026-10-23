using MikuSB.Common.Util;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.Packet.Recv.Online;

/// <summary>
/// LAN co-op plumbing shared by the two packets that can start a match (305 ready / 311 accept).
/// In LAN mode the host is sent its level with "?listen" and the guests the host's address; a guest that
/// asks to start before the host is parked here and answered as soon as the host has started.
/// </summary>
internal static class LanRoomStart
{
    private static readonly Logger Logger = new("LanRoomStart");

    /// <summary>
    /// How long to wait after the host's own NtfOnlineStart before the parked guests are answered. The host
    /// client has to load the level and open its net driver first (measured 2026-10-07: LoadMap -> listener
    /// ready in under a second, but the level itself takes ~15 s to become playable).
    /// </summary>
    private const int GuestConnectDelayMs = 15000;

    /// <summary>Sends every parked guest the host address. No-op when nobody is waiting.</summary>
    public static async Task FlushParkedGuests()
    {
        var guests = OnlineRoomService.TakeLanParkedGuests(out var address);
        if (guests.Count == 0) return;

        try
        {
            Logger.Info($"LAN host started: answering {guests.Count} waiting guest(s) with '{address}' in {GuestConnectDelayMs} ms.");
            await Task.Delay(GuestConnectDelayMs);

            foreach (var guest in guests)
            {
                Logger.Info($"Sending NtfOnlineStart RoomId=1 RoomAddr='{address}' to waiting guest uid {guest.Player?.Uid ?? 0}.");
                await guest.SendPacket(CmdIds.NtfOnlineStart, new NtfOnlineStart { RoomId = 1, RoomAddr = address });
            }
        }
        catch (Exception e)
        {
            Logger.Warn($"Could not answer the waiting LAN guests: {e.Message}");
        }
    }

    /// <summary>
    /// Handles the case where somebody else pressed start before the configured LAN host did: the host
    /// still has to load the level with "?listen", so it is pushed that URL and the caller is parked
    /// until it is up. Returns true when the caller has nothing left to do (no travel of its own).
    /// </summary>
    public static async Task<bool> TryPushHostStart(Connection connection)
    {
        if (OnlineRoomService.StartLanHostOnBehalf(connection) is not { } push) return false;

        Logger.Info($"Pushing the listen level to host uid {push.Host.Player?.Uid ?? 0} on behalf of " +
                    $"uid {connection.Player?.Uid ?? 0}.");
        await push.Host.SendPacket(CmdIds.NtfOnlineStart, new NtfOnlineStart { RoomId = 1, RoomAddr = push.Address });

        OnlineRoomService.ParkLanGuest(connection);
        _ = FlushParkedGuests();
        return true;
    }
}

/// <summary>
/// After RspOnlineRoom the client immediately sends ReqOnlineRoomUpdate(313) to pull the current
/// room line-up (observed 2026-10-05 18:16:08, right after PartyFormation opened). Answer with an
/// empty Rsp plus the room roster, which is what fills the party panel and the level's spawn data.
/// </summary>
[Opcode(CmdIds.ReqOnlineRoomUpdate)]
public class HandlerReqOnlineRoomUpdate : Handler
{
    private static readonly Logger Logger = new("ReqOnlineRoomUpdate");

    public override async Task OnHandle(Connection connection, byte[] data, ushort seqNo)
    {
        Logger.Info($"Online room update: {data.Length} byte(s) [{Convert.ToHexString(data)}]");
        await connection.SendPacket(CmdIds.RspOnlineRoomUpdate);
        await OnlineRoomService.PushRoomRoster(connection);
    }
}

/// <summary>
/// Sent by the native binding me:OnlineRoomStart(state) on every presence change the client makes
/// inside a room (states at Launch/Online/Online.lua:74-79: 0 Empty 空闲 / 1 PreReady 预备 /
/// 2 Change 换装 / 3 Ready 准备 / 4 Fight / 5 End).
///
/// Only the two "ready" states start a match:
///  - 3 Ready  - umg_party_formation.lua:820 DoFight() and PartyLogic.PlayerReady() when the room
///               holds at most 3 players (决战哈姆 112 has nMaxPlayer 3);
///  - 1 PreReady - PartyLogic.PlayerReady() (PartyLogic.lua:246-259) when nMaxPlayer > 3, i.e. the
///               15-player 冲关啦 105 asks to start with PreReady instead of Ready.
///
/// 2 Change must NOT travel: umg_party_formation.lua:971 DoChangeMem() (the 更换少女 / change
/// Q-character button) sends exactly that, and answering it with NtfOnlineStart dropped the player
/// straight into PartyGo_9633 while the Q-character picker was still opening (observed 2026-10-06
/// 23:11:34 - payload [08 02], client log "UI.OpenInner InName:PartyFormation" followed 0.9 s later
/// by "OnNotiyOnlineStart").
///
/// For a ready state the real server answers with a dedicated-server address; MikuSB instead resolves
/// a party level the client can load by itself (see <see cref="OnlineRoomService"/>).
/// </summary>
[Opcode(CmdIds.ReqOnlineRoomStart)]
public class HandlerReqOnlineRoomStart : Handler
{
    private static readonly Logger Logger = new("ReqOnlineRoomStart");

    /// <summary>Online.Player_State_PreReady - start signal for rooms with more than 3 players.</summary>
    private const uint PlayerStatePreReady = 1;

    /// <summary>Online.Player_State_Ready - start signal for rooms with at most 3 players.</summary>
    private const uint PlayerStateReady = 3;

    public override async Task OnHandle(Connection connection, byte[] data, ushort seqNo)
    {
        var states = OnlineRoomService.Varints(data);
        var state = states.Count > 0 ? states[0] : PlayerStateReady;

        Logger.Info($"Online room start: {data.Length} byte(s) [{Convert.ToHexString(data)}] " +
                    $"state={state} onlineId={connection.OnlineRoomId}");
        await connection.SendPacket(CmdIds.RspOnlineRoomStart);

        if (state is not (PlayerStatePreReady or PlayerStateReady))
        {
            // 0 Empty (BtnPause) / 2 Change (DoChangeMem, DoChangeSkill) / 4 Fight / 5 End only move the
            // room line-up state - travelling here would yank the player out of the lobby.
            Logger.Info($"State {state} is not a ready state; line-up only, no level travel.");
            return;
        }

        if (await LanRoomStart.TryPushHostStart(connection)) return;

        var decision = OnlineRoomService.DecideRoomAddress(connection, out var roomAddr);
        switch (decision)
        {
            case OnlineRoomService.RoomAddressDecision.Defer:
                // LAN guest: the host has not started yet, so there is nothing to connect to. The parked
                // connection is answered by LanRoomStart.FlushParkedGuests once the host travels.
                OnlineRoomService.ParkLanGuest(connection);
                Logger.Info("LAN guest is waiting for the host to start; no travel yet.");
                return;

            case OnlineRoomService.RoomAddressDecision.Unavailable:
                Logger.Warn("No room address could be resolved; the client stays in the lobby.");
                return;
        }

        // The level's GameMode spawns the players from the roster, so make sure it is up to date before the
        // client browses into the match.
        await OnlineRoomService.PushRoomRoster(connection);

        // ... and it reads the activity/level ids from the player attributes while it builds the level task.
        await OnlineRoomService.ApplyOnlineLevel(connection);

        Logger.Info($"Sending NtfOnlineStart RoomId=1 RoomAddr='{roomAddr}'.");
        await connection.SendPacket(CmdIds.NtfOnlineStart, new NtfOnlineStart { RoomId = 1, RoomAddr = roomAddr });

        _ = LanRoomStart.FlushParkedGuests();
    }
}

/// <summary>
/// Leaving the room (back button / kick). No room state is tracked yet, so just acknowledge.
/// </summary>
[Opcode(CmdIds.ReqOnlineRoomExit)]
public class HandlerReqOnlineRoomExit : Handler
{
    private static readonly Logger Logger = new("ReqOnlineRoomExit");

    public override async Task OnHandle(Connection connection, byte[] data, ushort seqNo)
    {
        Logger.Info($"Online room exit: {data.Length} byte(s) [{Convert.ToHexString(data)}]");
        await connection.SendPacket(CmdIds.RspOnlineRoomExit);
    }
}

/// <summary>
/// Inviting a friend into the current room (Online.InvitePlayer / Online.InviteFriend ->
/// me:OnlineInvite(pid)). Observed 2026-10-06 17:23:46: ReqOnlineRoomInvite(309) arrived, the server
/// logged "No handler found for ReqOnlineRoomInvite(309)" and the client stayed on the
/// "connecting" spinner forever - that is the "邀请队友卡在连接服务器画面" bug.
///
/// The payload has no generated type, so the target is taken as the varint that belongs to a
/// connected player (safe for both [pid] and [pid, onlineId] layouts); NtfOnlineInvite(1034) is then
/// pushed to that player so the invite popup opens on their side.
/// </summary>
[Opcode(CmdIds.ReqOnlineRoomInvite)]
public class HandlerReqOnlineRoomInvite : Handler
{
    private static readonly Logger Logger = new("ReqOnlineRoomInvite");

    public override async Task OnHandle(Connection connection, byte[] data, ushort seqNo)
    {
        Logger.Info($"Online room invite: {data.Length} byte(s) [{Convert.ToHexString(data)}]");
        await connection.SendPacket(CmdIds.RspOnlineRoomInvite);

        var candidates = OnlineRoomService.Varints(data);
        Logger.Info($"Online room invite values: [{string.Join(", ", candidates)}]");

        var selfUid = connection.Player?.Uid ?? 0;
        Connection? target = null;
        foreach (var candidate in candidates)
        {
            if (candidate == (uint)selfUid) continue;

            target = OnlineRoomService.FindConnection((int)candidate);
            if (target is not null) break;
        }

        if (target?.Player is null || connection.Player is null)
        {
            Logger.Warn("Online room invite has no reachable target on this server; ack only.");
            return;
        }

        var inviter = connection.Player;
        await target.SendPacket(CmdIds.NtfOnlineInvite, new NtfOnlineInvite
        {
            RoomId = 1,
            Onlineid = connection.OnlineRoomId,
            Playerid = (uint)inviter.Uid,
            Name = $"Player{inviter.Uid}",
            Level = inviter.Data.Level,
        });

        Logger.Info($"Online room invite forwarded to uid {target.Player.Uid} (room {connection.OnlineRoomId}).");
    }
}

/// <summary>
/// Accepting an invite (Online.AcceptInvite -> me:OnlineAccept(pid, onlineId)). Without a room model
/// the accepter has nothing to join, so acknowledge and advertise the same level the room plays;
/// whether both machines really meet needs a shared dedicated/listen server, which the
/// OnlineRoomDsAddress hook is for.
/// </summary>
[Opcode(CmdIds.ReqOnlineRoomAccept)]
public class HandlerReqOnlineRoomAccept : Handler
{
    private static readonly Logger Logger = new("ReqOnlineRoomAccept");

    public override async Task OnHandle(Connection connection, byte[] data, ushort seqNo)
    {
        Logger.Info($"Online room accept: {data.Length} byte(s) [{Convert.ToHexString(data)}] " +
                    $"onlineId={connection.OnlineRoomId}");

        if (connection.OnlineRoomId == 0)
        {
            foreach (var candidate in OnlineRoomService.Varints(data))
            {
                if (!OnlineRoomService.IsPartyActivity(candidate)) continue;

                connection.OnlineRoomId = candidate;
                break;
            }
        }

        await connection.SendPacket(CmdIds.RspOnlineRoomAccept);

        if (await LanRoomStart.TryPushHostStart(connection)) return;

        var decision = OnlineRoomService.DecideRoomAddress(connection, out var roomAddr);
        switch (decision)
        {
            case OnlineRoomService.RoomAddressDecision.Defer:
                OnlineRoomService.ParkLanGuest(connection);
                Logger.Info("Accepting LAN guest is waiting for the host to start; no travel yet.");
                return;

            case OnlineRoomService.RoomAddressDecision.Unavailable:
                Logger.Warn("No room address could be resolved for the accepting player; staying in the lobby.");
                return;
        }

        Logger.Info($"Sending NtfOnlineStart RoomId=1 RoomAddr='{roomAddr}' to the accepting player.");
        await connection.SendPacket(CmdIds.NtfOnlineStart, new NtfOnlineStart { RoomId = 1, RoomAddr = roomAddr });

        _ = LanRoomStart.FlushParkedGuests();
    }
}

/// <summary>Reconnect to the room the client was in; nothing to restore yet, so just acknowledge.</summary>
[Opcode(CmdIds.ReqOnlineRoomReconnect)]
public class HandlerReqOnlineRoomReconnect : Handler
{
    private static readonly Logger Logger = new("ReqOnlineRoomReconnect");

    public override async Task OnHandle(Connection connection, byte[] data, ushort seqNo)
    {
        Logger.Info($"Online room reconnect: {data.Length} byte(s) [{Convert.ToHexString(data)}]");
        await connection.SendPacket(CmdIds.RspOnlineRoomReconnect);
    }
}

/// <summary>Chat-channel invite accept; acknowledge so the chat tab never hangs on a spinner.</summary>
[Opcode(CmdIds.ReqOnlineRoomChatAccept)]
public class HandlerReqOnlineRoomChatAccept : Handler
{
    private static readonly Logger Logger = new("ReqOnlineRoomChatAccept");

    public override async Task OnHandle(Connection connection, byte[] data, ushort seqNo)
    {
        Logger.Info($"Online room chat accept: {data.Length} byte(s) [{Convert.ToHexString(data)}]");
        await connection.SendPacket(CmdIds.RspOnlineRoomChatAccept);
    }
}

/// <summary>
/// Map changed inside the room (party map picker / random map). The pick itself already reaches the
/// server through PartyLogic_SetRandomMapId, so only log the payload here.
/// </summary>
[Opcode(CmdIds.ReqOnlineRoomUpdateMap)]
public class HandlerReqOnlineRoomUpdateMap : Handler
{
    private static readonly Logger Logger = new("ReqOnlineRoomUpdateMap");

    public override async Task OnHandle(Connection connection, byte[] data, ushort seqNo)
    {
        Logger.Info($"Online room update map: {data.Length} byte(s) [{Convert.ToHexString(data)}]");
        await connection.SendPacket(CmdIds.RspOnlineRoomUpdateMap);
    }
}
