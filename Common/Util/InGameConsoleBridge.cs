namespace MikuSB.Util;

public sealed record InGameConsoleCommandResponse(bool Success, IReadOnlyList<string> Messages);

/// <summary>A player currently holding an active game connection, for the admin panel's online view.</summary>
public sealed record OnlinePlayerInfo(int Uid, string Name, uint Level, string RemoteAddress);

/// <summary>Metadata of one console command, so the panel can list them without calling /help.</summary>
public sealed record CommandDescriptor(string Name, string Usage, string Description, string[] Aliases, string[] Permissions);

/// <summary>Currency id plus display name, mirrored from CurrencyService.</summary>
public sealed record CurrencyDescriptor(uint Id, string Name);

/// <summary>
/// Which DLC era the server is serving right now, and whether the pin came from /dlc at runtime or from
/// Config.json. The replay page needs all three: the id to highlight a card, the flag to explain why the
/// highlight will not survive a restart, and the persisted id to say what a restart would fall back to.
/// </summary>
public sealed record ServedDlcInfo(uint Id, bool RuntimePin, bool Persisted);

/// <summary>
/// Outcome of starting the client through the loader. <paramref name="Pid"/> is the game process id on
/// success; on failure <paramref name="Message"/> carries the reason (missing exe, patch download failed,
/// injection failed). It exists because the console path reports success unconditionally and pushes the real
/// verdict into localised text, which a page cannot distinguish from "it worked".
/// </summary>
public sealed record GameLaunchResult(bool Ok, int Pid, string Message);

public static class InGameConsoleBridge
{
    public static Func<string, CancellationToken, Task<InGameConsoleCommandResponse>>? ExecuteCommandAsync { get; set; }

    /// <summary>
    /// Online roster. Lives here for the same reason <see cref="ExecuteCommandAsync"/> does: SdkServer only
    /// references Common, so the socket/player types must not leak into it.
    /// </summary>
    public static Func<IReadOnlyList<OnlinePlayerInfo>>? GetOnlinePlayers { get; set; }

    public static Func<IReadOnlyList<CommandDescriptor>>? GetCommands { get; set; }

    public static Func<IReadOnlyList<CurrencyDescriptor>>? GetCurrencyCatalog { get; set; }

    public static Func<ServedDlcInfo>? GetServedDlc { get; set; }

    /// <summary>
    /// Starts the client through the upstream loader (same call <c>/game</c> makes, including the patch
    /// download). Wired by the host; the panel uses it so it can report a real pid or a real reason instead of
    /// scraping console text.
    /// </summary>
    public static Func<string[], GameLaunchResult>? LaunchGame { get; set; }

    /// <summary>
    /// Update check the panel renders; <c>SdkServer</c> cannot reference the host assembly, so the
    /// result travels as a plain record.
    /// </summary>
    public sealed record UpdateStatus(
        bool Ok,
        string Current,
        string? Latest,
        bool Available,
        string? Asset,
        long Size,
        string? Notes,
        string? Error);

    /// <summary>Asks the updater for the latest release (cached server side for five minutes).</summary>
    public static Func<bool, Task<UpdateStatus?>>? CheckUpdate { get; set; }

    /// <summary>
    /// Runs the update the panel asked for: download, stage the updater and hand over. Returns true when the
    /// updater took control, in which case the host is about to exit.
    /// </summary>
    public static Func<Task<bool>>? ApplyUpdate { get; set; }

    private static readonly object PacketLogSync = new();
    private static readonly Dictionary<Guid, Action<string>> PacketLogSubscribers = [];

    public static IDisposable SubscribePacketLogs(Action<string> handler)
    {
        var id = Guid.NewGuid();
        lock (PacketLogSync)
            PacketLogSubscribers.Add(id, handler);

        return new PacketLogSubscription(id);
    }

    public static void PublishPacketLog(string message)
    {
        Action<string>[] subscribers;
        lock (PacketLogSync)
            subscribers = PacketLogSubscribers.Values.ToArray();

        foreach (var subscriber in subscribers)
            subscriber(message);
    }

    private sealed class PacketLogSubscription(Guid id) : IDisposable
    {
        public void Dispose()
        {
            lock (PacketLogSync)
                PacketLogSubscribers.Remove(id);
        }
    }
}
