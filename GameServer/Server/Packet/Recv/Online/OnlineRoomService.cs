using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server;
using MikuSB.GameServer.Server.Packet.Recv.Login;
using MikuSB.Proto;
using MikuSB.TcpSharp;
using MikuSB.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.GameServer.Server.Packet.Recv.Online;

/// <summary>
/// Room bootstrap for the co-op ("联机") activities that run through the native room binding:
///
///   ReqOnlineRoom(303)      { onlineId, teamId } -> RspOnlineRoom(304)
///   ReqOnlineRoomStart(305) { stateFlag }        -> RspOnlineRoomStart(306)
///                                                   + NtfOnlineStart{ RoomId, RoomAddr }
///
/// NtfOnlineStart is what makes the client travel: the native layer browses RoomAddr as a plain UE
/// travel URL, so that one string decides which level the room plays. The live server points it at a
/// dedicated server; MikuSB has none, so for party activities it points at the party level itself,
/// which the client can load standalone - probed 2026-10-06: PartyGo_9633 (决战哈姆) and
/// PartyGo_9636 (冲关啦练习关) both load from a bare local browse and run a complete solo match
/// (match state reached Progress, HUD opened, combat ticked, no dedicated server involved).
///
/// Everything below is derived from client data:
///
///   * online/online.json  ResetLevel holds the candidate map ids of an activity (map id == level id
///     for every normal party level; only the practice levels 9635/9636 are levels whose MapID differs).
///   * Resources/map/map.json - the client's own map table (1635 rows) - is authoritative for the asset
///     path AND the GameMode of a map id: 9633 -> .../Party_05/Level/PartyGo_9633 +
///     BP_WaterParkGameMode, 9635/9636 -> BP_WaterParkGuideGameMode, 60200 ->
///     MultiLevel/AssaultOps_1/AssaultOps_60200 + BP_GameOnline (进击行动, online Type 13).
///   * The party folder table below is only a fallback for ids map/map.json does not list. The unpacked
///     client tree dataoutput_20260715_221020 confirms both families:
///     .../LevelTask/Level/MultiLevel/Party_0X/Level/PartyGo_&lt;id&gt; and
///     .../LevelTask/Level/MultiLevel/AssaultOps_1/AssaultOps_&lt;id&gt;.
/// </summary>
public static class OnlineRoomService
{
    /// <summary>online/online.json Type of the party rows (T7, "海姆优化作战" / 派对).</summary>
    private const uint PartyType = 7;

    /// <summary>Party level id ranges and the asset folder each one lives in.</summary>
    private static readonly (uint First, uint Last, string Folder)[] PartyLevelFolders =
    [
        (9600, 9611, "Party_01"),
        (9612, 9622, "Party_02"),
        (9623, 9625, "Party_03"),
        (9626, 9632, "Party_04"),
        (9633, 9636, "Party_05"),
        (9637, 9639, "Party_06"),
    ];

    private const string PartyLevelRoot = "/Game/Blueprints/LevelTask/Level/MultiLevel";
    private const string WaterParkGameMode = "/Game/Blueprints/WaterPark/BP_WaterParkGameMode.BP_WaterParkGameMode_C";
    private const string WaterParkGuideGameMode = "/Game/Blueprints/GameMode/BP_WaterParkGuideGameMode.BP_WaterParkGuideGameMode_C";

    /// <summary>(103, 1) PartyLogic.MapGID - the JSON blob PartyLogic_SetRandomMapId stores.</summary>
    private const uint PartyMapGid = 103;
    private const uint PartyMapSid = 1;

    private static readonly Logger Logger = new("OnlineRoomService");

    /// <summary>Base the co-op shorthand of map/map.json expands into (see ExpandMapRow).</summary>
    private const string ResourceLevelRoot = "/Game/Blueprints/LevelTask/Level/";
    private const string ResourceGameModeRoot = "/Game/Blueprints/GameMode/";

    /// <summary>Base the online level table's TaskPath column expands into (see LevelOptions).</summary>
    private const string ResourceTaskRoot = "/Game/Blueprints/LevelTask/Tasks/";

    private static Dictionary<uint, MapRow>? _mapRows;

    /// <summary>
    /// Resources/map/map.json - the client's own map table - keyed by map id. Loaded once on first use;
    /// a missing or unreadable file simply degrades to the built-in party table.
    /// </summary>
    private static Dictionary<uint, MapRow> MapRows => _mapRows ??= LoadMapRows();

    private static Dictionary<uint, MapRow> LoadMapRows()
    {
        var rows = new Dictionary<uint, MapRow>();
        var path = $"{ConfigManager.Config.Path.ResourcePath}/map/map.json";
        if (!File.Exists(path))
        {
            Logger.Warn($"map/map.json not found at '{path}'; falling back to the built-in party level table.");
            return rows;
        }

        try
        {
            var list = JsonConvert.DeserializeObject<List<MapRowJson>>(File.ReadAllText(path)) ?? [];
            foreach (var item in list)
            {
                if (item.ID != 0)
                    rows[item.ID] = new MapRow(item.MapPath ?? string.Empty, item.GameMode ?? string.Empty);
            }

            Logger.Info($"Loaded {rows.Count} map row(s) from map/map.json.");
        }
        catch (Exception e)
        {
            Logger.Warn($"Unreadable map/map.json ('{path}'): {e.Message}");
        }

        return rows;
    }

    /// <summary>One map/map.json row, as far as the room bootstrap cares.</summary>
    private sealed record MapRow(string MapPath, string GameMode);

    private sealed class MapRowJson
    {
        [JsonProperty("ID")] public uint ID { get; set; }
        [JsonProperty("MapPath")] public string? MapPath { get; set; }
        [JsonProperty("GameMode")] public string? GameMode { get; set; }
    }

    private static Dictionary<uint, LevelRow>? _levelRows;

    /// <summary>
    /// Resources/online/level.json - the online level table the client knows as OnlineLevel - keyed by level
    /// id. The room bootstrap feeds three columns into the travel URL: CurrentLevelId (the level id itself),
    /// FightId and TaskPath. The generic online GameMode (BP_GameOnline + MultiMap, which every AssaultOps
    /// level uses) resolves its fight config and its level task from those options; without them
    /// AGameBaseMode::InitGameState reports "FightId=-1 is not exist!!!" and the task subsystem reports
    /// "StartLevelTask: 无法加载关卡配置 LevelId=0", i.e. a level with no monsters and no objective.
    /// </summary>
    private static Dictionary<uint, LevelRow> LevelRows => _levelRows ??= LoadLevelRows();

    private static Dictionary<uint, LevelRow> LoadLevelRows()
    {
        var rows = new Dictionary<uint, LevelRow>();
        var path = $"{ConfigManager.Config.Path.ResourcePath}/online/level.json";
        if (!File.Exists(path))
        {
            Logger.Warn($"online/level.json not found at '{path}'; online levels get no task option.");
            return rows;
        }

        try
        {
            var list = JsonConvert.DeserializeObject<List<LevelRowJson>>(File.ReadAllText(path)) ?? [];
            foreach (var item in list)
            {
                if (item.Id != 0) rows[item.Id] = new LevelRow(item.FightId, item.TaskPath ?? string.Empty);
            }

            Logger.Info($"Loaded {rows.Count} online level row(s) from online/level.json.");
        }
        catch (Exception e)
        {
            Logger.Warn($"Unreadable online/level.json ('{path}'): {e.Message}");
        }

        return rows;
    }

    /// <summary>One online/level.json row, as far as the room bootstrap cares.</summary>
    private sealed record LevelRow(uint FightId, string TaskPath);

    private sealed class LevelRowJson
    {
        [JsonProperty("Id")] public uint Id { get; set; }
        [JsonProperty("FightID")] public uint FightId { get; set; }
        [JsonProperty("TaskPath")] public string? TaskPath { get; set; }
    }

    /// <summary>
    /// URL options of one level, in the shape the client's own level configs produce
    /// ("TaskPath=/Game/Blueprints/LevelTask/Tasks/&lt;row&gt;"). The separator is '?' rather than '&amp;'
    /// because that is what every GetOption in the client script builds; the native side splits on it.
    /// CurrentLevelId and FightId come first because the native GameMode reads them while it builds its game
    /// state, before it ever loads the task asset.
    /// </summary>
    private static string LevelOptions(uint levelId)
    {
        if (!LevelRows.TryGetValue(levelId, out var row)) return $"?CurrentLevelId={levelId}";

        var options = $"?CurrentLevelId={levelId}";
        if (row.FightId != 0) options += $"?FightId={row.FightId}";
        if (string.IsNullOrWhiteSpace(row.TaskPath)) return options;

        var taskPath = row.TaskPath.StartsWith("/Game", StringComparison.Ordinal)
            ? row.TaskPath
            : ResourceTaskRoot + row.TaskPath;

        return options + $"?TaskPath={taskPath}";
    }

    /// <summary>
    /// The address to advertise in NtfOnlineStart for this connection's current room.
    /// Precedence: LAN/dedicated-server override -> level of the activity -> legacy config value.
    /// </summary>
    public static string ResolveRoomAddress(Connection connection)
    {
        var options = ConfigManager.Config.ServerOption;

        var dedicated = options.OnlineRoomDsAddress;
        if (!string.IsNullOrWhiteSpace(dedicated)) return dedicated;

        var levelId = ResolveLevelId(connection);
        if (levelId != 0 && BuildLevelUrl(levelId) is { Length: > 0 } url) return url;

        return options.OnlineRoomAddress ?? string.Empty;
    }

    /// <summary>What the room-start caller should do with the address that was resolved for it.</summary>
    public enum RoomAddressDecision
    {
        /// <summary>Send NtfOnlineStart with the returned address right away.</summary>
        Send,

        /// <summary>LAN guest whose host has not started yet: park the connection, it is answered later.</summary>
        Defer,

        /// <summary>Nothing usable to advertise; leave the client where it is.</summary>
        Unavailable,
    }

    /// <summary>How long a started host keeps answering guests with its address (a party match runs 300 s).</summary>
    private const int LanHostWindowSeconds = 600;

    private static readonly object LanGate = new();
    private static readonly List<Connection> LanParkedGuests = [];
    private static int _lanHostUid;
    private static DateTime _lanHostStartedAt = DateTime.MinValue;
    private static string _lanGuestAddress = string.Empty;

    /// <summary>
    /// Decides the NtfOnlineStart payload of one room start. Without
    /// <see cref="MikuSB.Common.Configuration.ServerOption.OnlineRoomLanMode"/> this is exactly
    /// <see cref="ResolveRoomAddress"/>; in LAN mode the host is sent its party level with "?listen"
    /// (its own client becomes the server) and every other player the host's address, so two machines
    /// meet without any dedicated server.
    /// </summary>
    public static RoomAddressDecision DecideRoomAddress(Connection connection, out string address)
    {
        address = string.Empty;
        var options = ConfigManager.Config.ServerOption;

        if (!string.IsNullOrWhiteSpace(options.OnlineRoomDsAddress))
        {
            address = options.OnlineRoomDsAddress;
            return RoomAddressDecision.Send;
        }

        if (!options.OnlineRoomLanMode)
        {
            address = ResolveRoomAddress(connection);
            return address.Length == 0 ? RoomAddressDecision.Unavailable : RoomAddressDecision.Send;
        }

        var uid = connection.Player?.Uid ?? 0;
        lock (LanGate)
        {
            if (IsLanHost(uid))
            {
                // A room of one is the party modes' own single-player form: the client loads the level
                // alone. The party levels are written for "dedicated server + remote clients" and only
                // preload / initialise on the client branch, so a listen host falls between the two and
                // ends up with an uninitialised character (see docs/dlc-online-modes.md 10). Appending
                // "?listen" here is what turned a solo "立即出击" into a one-player listen server on
                // 2026-10-08 02:21 and broke it. So only advertise a listen host once somebody can
                // actually join it; with nobody else in the room we hand out the plain level url and the
                // match runs standalone, exactly like the non-LAN path and the working 2026-10-07 16:59
                // solo run.
                //
                // Do NOT use RoomMembers() for this test. It filters on
                // SessionStateEnum.ACTIVE, and nothing in this server ever assigns that value
                // (only INACTIVE / WAITING_FOR_TOKEN / WAITING_FOR_LOGIN are ever set), so
                // RoomMembers() returns exactly one entry - the caller - for every room, always.
                // That is also why every roster push in the log reads "to 1 member(s)" even while
                // two clients sit in the same room; it was never evidence of an empty room.
                // RoomPeerCount() answers the same question without the dead filter.
                if (RoomPeerCount(connection) == 0)
                {
                    address = ResolveRoomAddress(connection);
                    Logger.Info($"LAN host uid {uid}: alone in room {connection.OnlineRoomId}, advertising " +
                                $"the plain level url '{address}' (no ?listen) so the match runs standalone.");
                    return address.Length == 0 ? RoomAddressDecision.Unavailable : RoomAddressDecision.Send;
                }

                var levelId = ResolveLevelId(connection);
                var levelUrl = levelId != 0 ? BuildLevelUrl(levelId) : string.Empty;
                if (levelUrl.Length == 0)
                {
                    Logger.Warn($"LAN host uid {uid}: no party level could be resolved (level id {levelId}).");
                    if (string.IsNullOrWhiteSpace(options.OnlineRoomAddress)) return RoomAddressDecision.Unavailable;
                    address = options.OnlineRoomAddress;
                }
                else
                {
                    address = $"{levelUrl}?listen";
                }

                _lanHostUid = uid;
                _lanHostStartedAt = DateTime.UtcNow;
                _lanGuestAddress = $"{LanHostAddress()}:{options.OnlineRoomListenPort}";
                Logger.Info($"LAN host uid {uid}: advertising '{address}'; guests will be sent '{_lanGuestAddress}'.");
                return RoomAddressDecision.Send;
            }

            if (!LanHostWindowOpen)
            {
                // A pinned host uid that never logs in again must not turn this into a black hole.
                // Report 2026-10-14 (决战哈姆, "开始决战" does nothing): OnlineRoomHostUid still pointed
                // at the previous test account (uid 4) while the current login is uid 6, so every start
                // request was acked with RspOnlineRoomStart and then parked here forever - no
                // NtfOnlineStart, no nState=7, the "connecting" overlay never closed. Waiting for a host
                // that cannot connect is never right, so when the request comes from a client running on
                // the host machine itself (the pinned identity) and the pinned uid has no live
                // connection, the pin is stale: hand out the standalone url like the "alone in room"
                // branch above. Remote guests keep waiting as before.
                var pinnedHostUid = options.OnlineRoomHostUid;
                if (pinnedHostUid != 0 && FindConnection(pinnedHostUid) is null &&
                    LanAddress.IsLocal(connection.RemoteEndPoint))
                {
                    address = ResolveRoomAddress(connection);
                    Logger.Warn($"LAN guest uid {uid}: pinned host uid {pinnedHostUid} has no connection and the " +
                                $"request came from the host machine itself, advertising the standalone level url " +
                                $"'{address}'. OnlineRoomHostUid is pinned to an account that no longer logs in; " +
                                "set it to 0 (or the current uid) to stop pinning it.");
                    return address.Length == 0 ? RoomAddressDecision.Unavailable : RoomAddressDecision.Send;
                }

                Logger.Info($"LAN guest uid {uid}: the host has not started yet; parking until it does.");
                return RoomAddressDecision.Defer;
            }

            address = _lanGuestAddress;
            Logger.Info($"LAN guest uid {uid}: advertising host address '{address}'.");
            return RoomAddressDecision.Send;
        }
    }

    /// <summary>
    /// A player pressed start before the configured LAN host did. Somebody still has to load the party
    /// level with "?listen" before anyone can join, so the server pushes that URL to the host itself.
    /// Returns the connection to notify plus the URL, or null when nothing has to be pushed (the caller
    /// is the host, the host is already running, or no host is reachable).
    /// </summary>
    public static (Connection Host, string Address)? StartLanHostOnBehalf(Connection requester)
    {
        var options = ConfigManager.Config.ServerOption;

        if (!options.OnlineRoomLanMode) return null;
        if (!string.IsNullOrWhiteSpace(options.OnlineRoomDsAddress)) return null;
        if (options.OnlineRoomHostUid == 0) return null;

        var requesterUid = requester.Player?.Uid ?? 0;
        if (requesterUid == options.OnlineRoomHostUid) return null;

        lock (LanGate)
        {
            if (LanHostWindowOpen) return null;

            var host = FindConnection(options.OnlineRoomHostUid);
            if (host is null)
            {
                Logger.Warn($"LAN guest uid {requesterUid} started the room, but host uid " +
                            $"{options.OnlineRoomHostUid} is not connected; nobody can host the match yet.");
                return null;
            }

            var levelId = ResolveLevelId(requester);
            var levelUrl = levelId != 0 ? BuildLevelUrl(levelId) : string.Empty;
            if (levelUrl.Length == 0)
            {
                Logger.Warn($"LAN guest uid {requesterUid} started the room, but no party level could be " +
                            $"resolved (level id {levelId}); the host stays in the lobby.");
                return null;
            }

            _lanHostUid = options.OnlineRoomHostUid;
            _lanHostStartedAt = DateTime.UtcNow;
            _lanGuestAddress = $"{LanHostAddress()}:{options.OnlineRoomListenPort}";

            var address = $"{levelUrl}?listen";
            Logger.Info($"LAN guest uid {requesterUid} started the room before the host; pushing '{address}' " +
                        $"to host uid {_lanHostUid}; guests will be sent '{_lanGuestAddress}'.");
            return (host, address);
        }
    }

    /// <summary>True while the host that started the current match is still expected to be listening.</summary>
    private static bool LanHostWindowOpen =>
        _lanHostStartedAt != DateTime.MinValue
        && DateTime.UtcNow - _lanHostStartedAt < TimeSpan.FromSeconds(LanHostWindowSeconds);

    /// <summary>
    /// True when this uid plays the host role: the configured <c>OnlineRoomHostUid</c> when set, else the
    /// first connection that reached a ready state in this match.
    /// </summary>
    private static bool IsLanHost(int uid)
    {
        var pinned = ConfigManager.Config.ServerOption.OnlineRoomHostUid;
        if (pinned != 0) return uid == pinned;

        return _lanHostUid == 0 || _lanHostUid == uid;
    }

    /// <summary>Remembers a guest that readied before the host, so the handler can answer it later.</summary>
    public static void ParkLanGuest(Connection connection)
    {
        lock (LanGate)
        {
            if (!LanParkedGuests.Contains(connection)) LanParkedGuests.Add(connection);
        }
    }

    /// <summary>
    /// Takes every parked guest plus the address to send them. The address is empty while the host has not
    /// started (or its window has expired), in which case the guests stay parked for the next host start.
    /// </summary>
    public static List<Connection> TakeLanParkedGuests(out string address)
    {
        lock (LanGate)
        {
            address = LanHostWindowOpen ? _lanGuestAddress : string.Empty;
            if (LanParkedGuests.Count == 0 || address.Length == 0) return [];

            var guests = new List<Connection>(LanParkedGuests);
            LanParkedGuests.Clear();
            return guests;
        }
    }

    /// <summary>Address the LAN guests are told to connect to (the host machine's LAN IP).</summary>
    private static string LanHostAddress()
    {
        var configured = ConfigManager.Config.ServerOption.OnlineRoomLanHost;
        if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim();

        var publicAddress = ConfigManager.Config.GameServer.PublicAddress;
        if (IsLoopback(publicAddress))
        {
            Logger.Warn("OnlineRoomLanHost is empty and GameServer.PublicAddress is a loopback address; " +
                        "LAN guests will not reach the host. Set OnlineRoomLanHost to the host's LAN IP.");
        }

        return publicAddress;
    }

    private static bool IsLoopback(string address)
    {
        return string.IsNullOrWhiteSpace(address)
               || address is "127.0.0.1" or "localhost" or "::1" or "0.0.0.0";
    }

    #region Room roster

    /// <summary>
    /// Lineup the online activities play with. The client always passes Online.TeamId (10) to
    /// me:OnlineCreateRoom and keeps exactly Online.MaxTeammate (1) character in it.
    /// </summary>
    private const uint OnlineLineupIndex = 10;

    /// <summary>
    /// Active connections that share this connection's room (the online id ReqOnlineRoom(303) recorded),
    /// or just the caller when the room is still unknown. This is the party the native room layer has to
    /// be told about.
    /// </summary>
    public static List<Connection> RoomMembers(Connection connection)
    {
        var members = new List<Connection>();
        var roomId = connection.OnlineRoomId;

        if (roomId != 0)
        {
            foreach (var item in SocketListener.Connections.Values)
            {
                if (item is not Connection candidate) continue;
                if (candidate.OnlineRoomId != roomId) continue;
                if (candidate.State != SessionStateEnum.ACTIVE) continue;
                if (candidate.Player is null) continue;
                members.Add(candidate);
            }
        }

        if (!members.Contains(connection)) members.Add(connection);
        return members;
    }

    /// <summary>
    /// How many *other* logged-in players share this connection's online room - i.e. how many people
    /// could actually join a listen host on this machine. Same intent as <see cref="RoomMembers"/>,
    /// but deliberately does NOT filter on <c>SessionStateEnum.ACTIVE</c>: nothing in this server ever
    /// assigns that value (see the note in <see cref="DecideRoomAddress"/>), so filtering on it makes
    /// every room look empty. Used to decide whether advertising "?listen" is worth anything.
    /// </summary>
    public static int RoomPeerCount(Connection connection)
    {
        var roomId = connection.OnlineRoomId;
        if (roomId == 0) return 0;

        var peers = 0;
        foreach (var item in SocketListener.Connections.Values)
        {
            if (item is not Connection candidate) continue;
            if (ReferenceEquals(candidate, connection)) continue;
            if (candidate.OnlineRoomId != roomId) continue;
            if (candidate.Player is null) continue;
            peers++;
        }

        return peers;
    }

    /// <summary>
    /// NtfOnlineRoom(7892) for one member of a room: the roster entry the online GameMode spawns that
    /// player's character from. Without a roster the generic online levels (BP_GameOnline + MultiMap, e.g.
    /// 进击行动 60200) abort the travel with "AGameBaseMode::InitNewPlayer :Team is Empty" - the crash the
    /// client hit on 2026-10-07 11:07:44, which is why the server now describes the room.
    /// </summary>
    public static NtfOnlineRoom BuildRoomRoster(Connection member, List<Connection> members, bool match = false)
    {
        var roster = new NtfOnlineRoom
        {
            RoomId = 1,
            Onlineid = member.OnlineRoomId,
            Bmatch = match
        };

        foreach (var other in members)
        {
            var player = other.Player;
            if (player is null) continue;

            var entry = new OnlinePlayer
            {
                Pid = (ulong)player.Uid,
                Name = PlayerGameData.NormalizeDisplayName(player.Data.Name),
                Level = player.Data.Level,
                Captain = other == member,
                Stateflag = 3
            };

            if (RoomLineup(other) is { } lineup) entry.Lineup = lineup;
            roster.Players.Add(entry);
        }

        return roster;
    }

    /// <summary>
    /// The lineup a room member brings into the match: the one the room asked for (303 team id, normally
    /// 10), else the first lineup the account owns.
    /// </summary>
    private static Proto.Lineup? RoomLineup(Connection connection)
    {
        var player = connection.Player;
        if (player is null) return null;

        var lineups = player.LineupManager.LineupData.LineupInfo;
        var teamId = connection.OnlineRoomTeamId != 0 ? connection.OnlineRoomTeamId : OnlineLineupIndex;

        if (!lineups.TryGetValue((int)teamId, out var lineup))
            lineup = lineups.Values.FirstOrDefault(x => x.Member1 != 0) ?? lineups.Values.FirstOrDefault();

        return lineup?.ToProto();
    }

    /// <summary>
    /// Tells every member of the room who is in it. Sent right after RspOnlineRoom(304), on every
    /// ReqOnlineRoomUpdate(313) and once more just before NtfOnlineStart, because the level's GameMode reads
    /// the roster when it spawns the players.
    /// </summary>
    public static async Task PushRoomRoster(Connection connection)
    {
        var members = RoomMembers(connection);
        if (members.Count == 0) return;

        foreach (var member in members)
            await member.SendPacket(RosterOpcode, BuildRoomRoster(member, members));

        Logger.Info($"Online room {connection.OnlineRoomId}: pushed the roster (opcode {RosterOpcode}) to " +
                    $"{members.Count} member(s) [uid {string.Join(",", members.Select(m => m.Player?.Uid ?? 0))}].");
    }

    /// <summary>Opcode the roster is pushed with (NTF_ONLINE_ROOMINFO = 1031 by default).</summary>
    private static int RosterOpcode => ConfigManager.Config.ServerOption.OnlineRoomRosterOpcode;

    /// <summary>
    /// Tell the client which activity and which level the room is playing, before the travel starts.
    ///
    /// The native online GameMode reads these two attributes while it builds its task and fight state
    /// (AttrIds.Online: PreviousIdTask 3 = the activity, LevelTask 6 = the level id). Leaving them at 0 is
    /// what produces, in the 进击行动 match of 2026-10-07 11:55, "AGameBaseMode::InitTaskValue, Try to
    /// GetOnlineId:0", "LogTaskRandomSubsystem: StartLevelTask: 无法加载关卡配置 LevelId=0" and
    /// "AGameBaseMode::InitGameState FightId=-1 is not exist!!!" - a level that loads with no monsters and
    /// no objective.
    /// </summary>
    public static async Task ApplyOnlineLevel(Connection connection)
    {
        if (connection.Player is not { } player) return;

        var onlineId = connection.OnlineRoomId != 0 ? connection.OnlineRoomId : PickedOnlineId(player);
        var levelId = ResolveLevelId(connection);
        if (onlineId == 0 || levelId == 0) return;

        var sync = new NtfSyncPlayer();
        player.Attributes.SyncTo(sync,
            player.Attributes.Set(AttrIds.Online.GroupId, AttrIds.Online.PreviousIdTaskSid, onlineId));
        player.Attributes.SyncTo(sync,
            player.Attributes.Set(AttrIds.Online.GroupId, AttrIds.Online.LevelTaskSid, levelId));
        await connection.SendPacket(CmdIds.NtfSyncAttr, sync);
        await PushLaunchState(connection, onlineId, levelId);

        Logger.Info($"Online room {onlineId}: told uid={player.Uid} that the match level is {levelId}.");
    }

    /// <summary>
    /// Writes the activity/level ids into the client's own launch state just before the packet that
    /// makes it travel.
    ///
    /// The level GameMode never reads a level id out of the travel URL: it asks the client back,
    /// through Launch.GetLevelID(), and for LaunchType.ONLINE that is Online.GetOnlineLevelId() -
    /// a plain Lua variable which the UI only ever refreshes from GameState.CurrentLevelId
    /// (Online.lua:2350-2352, i.e. whatever chapter level the lobby last selected). A T13 sortie
    /// alone therefore started with "AGameBaseMode::InitGame Set CurrentLevelId: 50151",
    /// "StartLevelTask ... 无法加载关卡配置 LevelId=50151", "任务组数量=0" and never reached
    /// AGameTaskActor::StartGame - no HUD, no monsters, no tasks (docs/progress/032 section 5).
    ///
    /// Verified from the console on 2026-10-08: the same three writes make the same level start
    /// correctly ("Set CurrentLevelId: 60200", 3 areas / 3 task groups, StartGame OpenUI:1).
    /// The launch type is only forced when it is not ONLINE already, because Launch.SetType clears
    /// the trial characters of every lineup (Launch.lua:165-184) and the party maps resolve their
    /// level through LaunchType.WaterPark instead.
    /// </summary>
    private static async Task PushLaunchState(Connection connection, uint onlineId, uint levelId)
    {
        var lua = "if Launch.GetType() ~= LaunchType.ONLINE then Launch.SetType(LaunchType.ONLINE) end\n";
        if (IsPartyActivity(onlineId))
            lua = string.Empty;

        lua += $"Online.SetOnlineId({onlineId})\nOnline.SetOnlineLevelId({levelId})\n";

        // The same options the native travel URL carries, in the shape Map.Open expects (no leading
        // '?'). OnlineMapFix re-opens the level through Map.Open when the native travel left
        // UMapManager's map id stale, and the re-opened URL has to keep the TaskPath - that option is
        // what reaches AGameTaskActor::SetOptionTaskPath and makes the task actor load the mode's task
        // asset. Characters the patch channel cannot carry are dropped rather than sent, because one
        // bad character silently kills the whole payload (HandlerReqLogin.ForbiddenPatchChars).
        var levelOptions = LevelOptions(levelId).TrimStart('?');
        if (levelOptions.IndexOfAny(['+', '<', '>', '&', '\'', '"']) < 0)
            lua += $"MikuOnlineLevelOptions = [[{levelOptions}]]\n";
        else
            Logger.Warn($"Online level {levelId}: URL options contain characters the patch channel " +
                        $"cannot carry, so OnlineMapFix will re-open the level without them ({levelOptions}).");

        await HandlerReqLogin.SendClientPatch(connection, "OnlineLaunchState", lua);
    }

    #endregion

    /// <summary>
    /// Map id this connection's activity should play, or 0 when nothing usable could be resolved.
    /// The activity id comes from the room request itself (303) or from the map attribute the party
    /// screens write; the level id then comes from the Config pin, the client's own map pick, or the
    /// first entry of the activity's online/online.json ResetLevel cell.
    /// </summary>
    public static uint ResolveLevelId(Connection connection)
    {
        var onlineId = connection.OnlineRoomId;
        if (onlineId == 0)
            onlineId = PickedOnlineId(connection.Player);

        if (onlineId == 0) return 0;
        if (!GameData.OnlineData.TryGetValue(onlineId, out var row)) return 0;

        // A pin in Config.json always wins, so a specific map can be forced while testing.
        if (ConfigManager.Config.ServerOption.OnlineRoomLevels.TryGetValue(onlineId.ToString(), out var pinned)
            && IsKnownLevel(pinned))
            return pinned;

        // Otherwise prefer the map the client itself picked in the party map list / random picker.
        var picked = PickedLevelId(connection.Player);
        if (IsKnownLevel(picked)) return picked;

        var fromRow = ResetLevelId(row.ResetLevel);
        if (IsKnownLevel(fromRow)) return fromRow;

        Logger.Warn($"Online activity {onlineId} has no usable level id (ResetLevel='{row.ResetLevel}').");
        return 0;
    }

    /// <summary>
    /// Full travel URL of a level, in the exact shape the client's own Map.Open produces, e.g.
    /// "/Game/Blueprints/LevelTask/Level/MultiLevel/Party_05/Level/PartyGo_9633?Game=/Game/Blueprints/WaterPark/BP_WaterParkGameMode.BP_WaterParkGameMode_C?MapId=9633"
    /// (that exact string is the one the live client browsed successfully on 2026-10-06).
    /// map/map.json is authoritative; the party folder table only covers ids it does not list.
    /// </summary>
    public static string BuildLevelUrl(uint levelId)
    {
        if (MapRows.TryGetValue(levelId, out var row) && ExpandMapRow(levelId, row) is { } url) return url;
        return IsPartyLevel(levelId) ? BuildPartyLevelUrl(levelId) : string.Empty;
    }

    /// <summary>
    /// Expands one map/map.json row into a travel URL. Two shapes occur in the live table: fully
    /// qualified (party maps: "/Game/Blueprints/.../PartyGo_9633" + ".../BP_WaterParkGameMode_C") and the
    /// co-op shorthand used by every MultiLevel map ("MultiLevel/AssaultOps_1/AssaultOps_60200" +
    /// "BP_GameOnline", 1562 of the 1635 rows). The shorthand expands to
    /// /Game/Blueprints/LevelTask/Level/&lt;MapPath&gt; and /Game/Blueprints/GameMode/&lt;Name&gt;.&lt;Name&gt;_C -
    /// which is byte for byte the URL shape the live server used for Multi_110
    /// (".../MultiLevel/Multi_110?Game=/Game/Blueprints/GameMode/BP_GameOnline.BP_GameOnline_C?MapId=110").
    /// Rows outside the MultiLevel family are left alone: their shorthand base is unproven.
    /// </summary>
    private static string? ExpandMapRow(uint levelId, MapRow row)
    {
        var package = row.MapPath switch
        {
            null or "" => null,
            var p when p.StartsWith("/Game", StringComparison.Ordinal) => p,
            var p when p.StartsWith("MultiLevel/", StringComparison.Ordinal) => ResourceLevelRoot + p,
            _ => null,
        };
        if (package is null || string.IsNullOrWhiteSpace(row.GameMode)) return null;

        var gameMode = row.GameMode.StartsWith("/Game", StringComparison.Ordinal)
            ? row.GameMode
            : $"{ResourceGameModeRoot}{row.GameMode}.{row.GameMode}_C";

        return $"{package}?Game={gameMode}?MapId={levelId}{LevelOptions(levelId)}";
    }

    /// <summary>Level id of a party asset folder, for ids map/map.json does not list.</summary>
    private static string BuildPartyLevelUrl(uint levelId)
    {
        var folder = "Party_05";
        foreach (var (first, last, name) in PartyLevelFolders)
        {
            if (levelId >= first && levelId <= last)
            {
                folder = name;
                break;
            }
        }

        var gameMode = levelId is 9635 or 9636 ? WaterParkGuideGameMode : WaterParkGameMode;
        return $"{PartyLevelRoot}/{folder}/Level/PartyGo_{levelId}?Game={gameMode}?MapId={levelId}";
    }

    /// <summary>Activity id the client asked for, as stored by PartyLogic_SetRandomMapId ({ nId = onlineId }).</summary>
    private static uint PickedOnlineId(PlayerInstance? player)
    {
        if (ParsePartyMapJson(player) is not { } json) return 0;
        return ToUint(json["nId"]);
    }

    /// <summary>Map id the client picked in the party map list ({ tbMapId = { ... } }).</summary>
    private static uint PickedLevelId(PlayerInstance? player)
    {
        if (ParsePartyMapJson(player) is not { } json) return 0;
        if (json["tbMapId"] is JsonArray { Count: > 0 } maps) return ToUint(maps[0]);
        return 0;
    }

    private static JsonObject? ParsePartyMapJson(PlayerInstance? player)
    {
        var raw = player?.Attributes.GetStringValue(PartyMapGid, PartyMapSid);
        if (string.IsNullOrWhiteSpace(raw)) return null;

        try
        {
            return JsonNode.Parse(raw) as JsonObject;
        }
        catch (Exception e)
        {
            Logger.Warn($"Unreadable party map attribute '{raw}': {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Level id out of an online/online.json ResetLevel cell. Three shapes exist in the live data:
    /// [[9633]], [[9602, 9605, ...]] and {"9634": 9626} - all of them name the level first.
    /// </summary>
    private static uint ResetLevelId(JToken? resetLevel)
    {
        switch (resetLevel)
        {
            case JArray { Count: > 0 } array:
                return array[0] is JArray { Count: > 0 } nested ? ToUint(nested[0]) : ToUint(array[0]);

            case JObject obj:
                foreach (var property in obj.Properties())
                {
                    var key = ToUint(property.Name);
                    if (key != 0) return key;
                }
                break;
        }

        return 0;
    }

    /// <summary>True when this online/online.json id is a party activity (T7).</summary>
    public static bool IsPartyActivity(uint onlineId)
    {
        return onlineId != 0 && GameData.OnlineData.TryGetValue(onlineId, out var row) && IsPartyType(row);
    }

    private static bool IsPartyType(OnlineExcel row) => ToUint(row.Type) == PartyType;

    private static bool IsPartyLevel(uint levelId)
    {
        foreach (var (first, last, _) in PartyLevelFolders)
        {
            if (levelId >= first && levelId <= last) return true;
        }

        return false;
    }

    /// <summary>True when the level id can be turned into a travel URL at all.</summary>
    private static bool IsKnownLevel(uint levelId)
    {
        return levelId != 0 && (MapRows.ContainsKey(levelId) || IsPartyLevel(levelId));
    }

    private static uint ToUint(object? value)
    {
        if (value is null) return 0;
        return uint.TryParse(value.ToString(), out var parsed) ? parsed : 0;
    }

    /// <summary>Connected player with this uid, if any (used by room invites).</summary>
    public static Connection? FindConnection(int uid)
    {
        foreach (var socketConnection in SocketListener.Connections.Values)
        {
            if (socketConnection is Connection connection && connection.Player?.Uid == uid) return connection;
        }

        return null;
    }

    /// <summary>Every varint in a protobuf body, in order (the room packets carry no generated types).</summary>
    public static List<uint> Varints(byte[] data)
    {
        var values = new List<uint>();

        var index = 0;
        while (index < data.Length)
        {
            var key = data[index++];
            var wireType = key & 0x07;

            switch (wireType)
            {
                case 0:
                    if (!TryReadVarint(data, ref index, out var value)) return values;
                    values.Add(value);
                    break;

                case 2:
                    if (!TryReadVarint(data, ref index, out var length)) return values;
                    index += (int)length;
                    break;

                case 5:
                    index += 4;
                    break;

                case 1:
                    index += 8;
                    break;

                default:
                    return values;
            }
        }

        return values;
    }

    private static bool TryReadVarint(byte[] data, ref int index, out uint value)
    {
        value = 0;
        var shift = 0;

        while (index < data.Length)
        {
            var current = data[index++];
            value |= (uint)(current & 0x7F) << shift;
            if ((current & 0x80) == 0) return true;

            shift += 7;
            if (shift > 28) return false;
        }

        return false;
    }
}
