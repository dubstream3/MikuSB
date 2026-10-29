using Microsoft.AspNetCore.Http;
using MikuSB.Util;

namespace MikuSB.SdkServer.Utils;

/// <summary>
/// Shared plumbing for the admin panel: a second, plain-HTTP Kestrel endpoint bound to loopback only and
/// kept off the HTTPS ports the game client pins (a self-signed certificate prompt would make the panel
/// useless in any browser). Everything keys off the local port, so one middleware pipeline can carry both
/// the game traffic and the panel without the SDK routes ever seeing panel requests.
/// </summary>
public static class AdminApi
{
    public static int Port => ConfigManager.Config.HttpServer.AdminPort;

    public static bool IsEnabled => Port > 0;

    public static bool IsAdminRequest(HttpContext context)
        => IsEnabled && context.Connection.LocalPort == Port;

    /// <summary>Folder holding index.html / app.js / style.css, resolved next to the executable.</summary>
    public static string WebRoot =>
        Path.Combine(AppContext.BaseDirectory, ConfigManager.Config.HttpServer.AdminWebRoot);

    public static string IndexFile => Path.Combine(WebRoot, "index.html");
}
