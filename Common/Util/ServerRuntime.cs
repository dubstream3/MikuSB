namespace MikuSB.Util;

/// <summary>
///     What the host process is doing right now, so the admin panel can say more than "up".
///
///     The panel is served from <c>LoaderManager.InitSdkServer</c>, i.e. before the database, the resource
///     pack and the command layer exist, so the status cannot come from an <see cref="InGameConsoleBridge" />
///     delegate: nothing is wired yet at the moment the operator opens the page and the resource pack starts
///     downloading. Static state crosses that boundary without SdkServer having to reference the host.
///
///     Three phases are all the panel shows: downloading a new build, downloading the resource pack, and
///     plain serving. "Nothing is running yet" is not a phase - it is <see cref="Initialized"/> being false.
/// </summary>
public static class ServerRuntime
{
    /// <summary>Serving only: the client can log in.</summary>
    public const string PhaseRunning = "running";

    /// <summary>A release package is being downloaded; the updater takes over once it is complete.</summary>
    public const string PhaseUpdate = "update";

    /// <summary>The MikuSB-Resource archive is being downloaded or unpacked (startup, or after a bump).</summary>
    public const string PhaseResources = "resources";

    private static int _initialized;
    private static volatile string _phase = PhaseRunning;
    private static volatile string _detail = "";

    public static string Phase => _phase;

    /// <summary>Free-form context for the phase, usually the file or version being fetched.</summary>
    public static string Detail => _detail;

    /// <summary>True once the database, the resource pack and the command layer are all up.</summary>
    public static bool Initialized => Volatile.Read(ref _initialized) != 0;

    public static void Enter(string phase, string? detail = null)
    {
        _detail = detail ?? "";
        _phase = phase;
    }

    public static void BackToRunning() => Enter(PhaseRunning);

    /// <summary>
    ///     Called at the end of <c>LoaderManager.InitCommand</c>, which is the last step of the boot
    ///     sequence (it is also where every bridge delegate the panel uses gets wired).
    /// </summary>
    public static void MarkInitialized()
    {
        Volatile.Write(ref _initialized, 1);
        BackToRunning();
    }
}
