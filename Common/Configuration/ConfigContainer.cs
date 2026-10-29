using MikuSB.Proto;

namespace MikuSB.Configuration;

public class ConfigContainer
{
    public HttpServerConfig HttpServer { get; set; } = new();
    public GameServerConfig GameServer { get; set; } = new();
    public PathConfig Path { get; set; } = new();
    public ServerOption ServerOption { get; set; } = new();
    public ProxyOptions Proxy { get; set; } = new();
    public LoaderOptions Loader { get; set; } = new();
}

public class HttpServerConfig
{
    public string BindAddress { get; set; } = "0.0.0.0";
    public string PublicAddress { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 21500;

    /// <summary>Log every dispatch request. Off by default: the game client is very chatty.</summary>
    public bool EnableLog { get; set; } = false;

    /// <summary>
    /// Admin panel listener: plain HTTP, loopback interface only, kept off the HTTPS ports the game client
    /// talks to so the panel never runs into a self-signed certificate prompt. 0 disables the panel.
    /// </summary>
    public int AdminPort { get; set; } = 21400;

    /// <summary>
    /// Bearer token every <c>/api/*</c> call must carry. Empty on first launch means "generate one, log it
    /// once and persist it into Config.json"; from then on the file is the only copy.
    /// </summary>
    public string AdminToken { get; set; } = "";

    /// <summary>Folder (relative to the executable) holding the panel's index.html / app.js / style.css.</summary>
    public string AdminWebRoot { get; set; } = "Web";

    public string GetDisplayAddress()
    {
        return "http" + "://" + PublicAddress + ":" + Port;
    }

    public string GetBindDisplayAddress()
    {
        return "http" + "://" + BindAddress + ":" + Port;
    }
}

public class GameServerConfig
{
    public string BindAddress { get; set; } = "0.0.0.0";
    public string PublicAddress { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 21000;
    public int KcpAliveMs { get; set; } = 45000;
    public string DatabaseName { get; set; } = "Miku.db";
    public string GameServerId { get; set; } = "MikuSB";
    public string GameServerName { get; set; } = "MikuSB";
    public string GetDisplayAddress()
    {
        return PublicAddress + ":" + Port;
    }
}

public class PathConfig
{
    public string ResourcePath { get; set; } = "Resources";
    public string ConfigPath { get; set; } = "Config";
    public string DatabasePath { get; set; } = "Config/Database";
    public string HandbookPath { get; set; } = "Config/Handbook";
    public string LogPath { get; set; } = "Config/Logs";
    public string DataPath { get; set; } = "Config/Data";
}

public class ServerOption
{
    public string Language { get; set; } = "EN";
    public string FallbackLanguage { get; set; } = "EN";
    public string[] DefaultPermissions { get; set; } = ["Admin"];
    public ServerProfile ServerProfile { get; set; } = new();
    public bool EnableGmMenu { get; set; } = false;
    public bool AutoCreateUser { get; set; } = true;
    public bool SavePersonalDebugFile { get; set; } = false;
    public bool AutoSendResponseWhenNoHandler { get; set; } = true;
#if DEBUG
    public bool EnableDebug { get; set; } = true;
#else
    public bool EnableDebug { get; set; } = false;
#endif
    public bool DebugMessage { get; set; } = true;
    public bool DebugDetailMessage { get; set; } = true;
    public bool DebugNoHandlerPacket { get; set; } = true;

    /// <summary>
    /// Forces <c>DLCLogic_CheckOpenAct</c> to always resolve this DLC activity id
    /// instead of picking the activity whose time window contains the current date.
    /// Needed for private-server use, because the live client dates are always in the
    /// past once an activity period has ended. 0 disables the override.
    /// </summary>
    public uint PinDlcActivityId { get; set; } = 0;

    /// <summary>
    /// Debug convenience: maps every CN (jinshan) SDK login to this single account key,
    /// whatever identity the tester typed in the login box. Keeps one stable player
    /// (progress, inventory, unlocked content) between client restarts. Empty disables it.
    /// </summary>
    public string PinLoginIdentity { get; set; } = "";

    /// <summary>
    /// LAN convenience: logins arriving from a non-loopback address (i.e. the second machine) are mapped
    /// to this account key instead of <see cref="PinLoginIdentity"/>, so the two machines become two
    /// different players (host uid vs an auto-created guest uid) without anyone touching the login box.
    /// The key is created on first use by AutoCreateUser, exactly like a typed identity. Empty keeps
    /// <see cref="PinLoginIdentity"/> for every login, which makes both machines share one player.
    /// </summary>
    public string LanGuestLoginIdentity { get; set; } = "";

    /// <summary>
    /// When true (the default) the identity typed in the login box selects the account, so every
    /// distinct phone / email / passport resolves to its own uid with its own progress. That is what
    /// lets several testers, or several machines, hold separate players at the same time.
    /// <see cref="PinLoginIdentity"/> and <see cref="LanGuestLoginIdentity"/> then only apply to logins
    /// that carry no identity at all. Set to false for the old behaviour, where the pin wins for every
    /// login no matter what was typed.
    /// </summary>
    public bool PerIdentityAccount { get; set; } = true;

    /// <summary>
    /// When true a CN (jinshan) login must carry a password matching the account's stored hash.
    /// Off by default, because the SMS flow the CN client actually uses (/v6/loginBySms) sends no
    /// password at all, and turning it on would reject every login from the stock client.
    /// </summary>
    public bool LoginPasswordRequired { get; set; } = false;

    /// <summary>Password stored on accounts created on first login.</summary>
    public string LoginDefaultPassword { get; set; } = "123456";

    /// <summary>
    /// When true (the default) <c>ReqLogin</c> still admits a client whose <c>token</c> no longer
    /// matches the stored <c>ComboToken</c>, provided the token is one of the SDK blobs the client
    /// quotes back (base64 JSON carrying <c>uid</c>). That covers a token that was rotated after the
    /// client cached it: the SDK login of a private server is not a security boundary, and a stale
    /// token used to strand the client on "识别中" with a bare <c>NtfLogout</c>. Every fallback is
    /// logged as a warning, so the desync stays visible instead of silent. Set to false to require
    /// an exact token match again.
    /// </summary>
    public bool AllowStaleTokenLogin { get; set; } = true;

    /// <summary>
    /// Address advertised in <c>NtfOnlineStart</c> when an online room starts, used as the fallback
    /// for activities MikuSB cannot resolve a level for. The live game points this at a dedicated
    /// server; leave it empty, because a stray value here sends every room to that map (a Multi_110
    /// value once crashed the client with "Couldn't spawn player: Invalid Character ID!").
    /// </summary>
    public string OnlineRoomAddress { get; set; } = "";

    /// <summary>
    /// Per-activity override of the level advertised in <c>NtfOnlineStart</c>, keyed by
    /// <c>online/online.json</c> Id as a string (e.g. "112": 9633). Party activities otherwise resolve
    /// their level from the activity's ResetLevel / the map the client picked, which is what a normal
    /// session needs; a pin here is only for forcing one specific map while testing.
    /// </summary>
    public Dictionary<string, uint> OnlineRoomLevels { get; set; } = [];

    /// <summary>
    /// LAN / dedicated-server hook: when set (host:port or a full travel URL), every room start
    /// advertises this address instead of a locally loaded party level, so several machines can meet
    /// on one process. Empty keeps the single-machine behaviour.
    /// </summary>
    public string OnlineRoomDsAddress { get; set; } = "";

    /// <summary>
    /// LAN co-op without a dedicated server. When true the room start is split into two roles:
    /// <list type="bullet">
    ///   <item>the host (<see cref="OnlineRoomHostUid"/>) is sent "&lt;party level URL&gt;?listen", so its own
    ///     client loads the level as a listen server and opens its net driver on
    ///     <see cref="OnlineRoomListenPort"/> (verified 2026-10-07: PartyGo_9633 with ?listen opens
    ///     "SeasunNetDriver ... listening on port 7777" and runs a complete match);</item>
    ///   <item>every other player is sent "&lt;OnlineRoomLanHost&gt;:&lt;OnlineRoomListenPort&gt;" once the host has
    ///     started - the plain UE client connect URL the live server also uses for its DS.</item>
    /// </list>
    /// A guest that readies before the host is parked and answered when the host starts.
    /// False keeps the single-machine behaviour, where every player loads the level alone.
    /// </summary>
    public bool OnlineRoomLanMode { get; set; } = false;

    /// <summary>
    /// Address the LAN guests are told to connect to: the host machine's LAN IP, e.g. "192.0.2.10".
    /// Empty falls back to <c>GameServer.PublicAddress</c>, which must not stay 127.0.0.1 for LAN play.
    /// </summary>
    public string OnlineRoomLanHost { get; set; } = "";

    /// <summary>UDP port the host client's listen server opens (UE default 7777).</summary>
    public int OnlineRoomListenPort { get; set; } = 7777;

    /// <summary>
    /// Uid of the machine that hosts the LAN match - the account whose client becomes the listen server.
    /// 0 makes the first connection that reaches a ready state the host, which is enough for a quick
    /// test but leaves the roles to whoever presses 准备 first.
    /// </summary>
    public int OnlineRoomHostUid { get; set; } = 0;

    /// <summary>
    /// Opcode the room roster (the <c>NtfOnlineRoom</c> message: RoomId, Onlineid, Players, Bmatch,
    /// Buffinfo) is pushed with. 1031 is the protocol's NTF_ONLINE_ROOMINFO and the only room-info
    /// notification the proto enum defines without a competing message class; the generic online levels
    /// (BP_GameOnline + MultiMap, e.g. 进击行动 60200) spawn every player's character from that roster and
    /// abort the travel with "AGameBaseMode::InitNewPlayer :Team is Empty" when it never arrives.
    /// Overridable so an alternative opcode can be probed without a rebuild.
    /// </summary>
    public int OnlineRoomRosterOpcode { get; set; } = 1031;
}

public class ServerProfile
{
    public string Name { get; set; } = "Miku-chan";
    public string Signature { get; set; } = "SnowBreak Private Server";
    public int Uid { get; set; } = 80;
    public int Level { get; set; } = 100;
    public Sex Gender { get; set; } = Sex.Female;
}

public class ProxyOptions
{
    public bool Enabled { get; set; } = true;
    public int Port { get; set; } = 18888;
}

public class LoaderOptions
{
    public string GamePath { get; set; } = "";
    public string[] PatchPaths { get; set; } = [@"Patch\MikuSB-Patch.dll"];
    public bool EnableInGameConsole { get; set; } = true;
    public bool AutoUpdateInGameConsole { get; set; } = true;
    public string InGameConsoleLoaderPath { get; set; } = @"Patch\MikuSB-InGame-GUI-Console.Loader.dll";
    public string[] Arguments { get; set; } = ["-FeatureLevelES31", "-channelid=seasun", "-NoSplash"];
    public bool SetAllProxy { get; set; } = true;

    /// <summary>
    ///     While the server runs, keep the specific-channel SDK host names pointed at this machine
    ///     through a fenced block in the hosts file. The block is removed again on a clean shutdown.
    /// </summary>
    public bool ManageCnHosts { get; set; } = true;

    /// <summary>
    /// Which client build this server is set up for: "cn" (jinshan channel, redirected with hosts entries)
    /// or "global" (Seasun/global build, redirected by injecting MikuSB-Patch.dll through the loader).
    /// Only the setup wizard reads it; launching still works from <see cref="GamePath"/>/<see cref="Arguments"/>.
    /// </summary>
    public string Region { get; set; } = "";

    /// <summary>Game root the client is installed in, i.e. the parent of the <c>data\game</c> folder.</summary>
    public string InstallPath { get; set; } = "";

    /// <summary>Value passed as <c>-userdir</c>; empty means "derive it from the install path".</summary>
    public string UserDir { get; set; } = "";
}
