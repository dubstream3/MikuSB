using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MikuSB.SdkServer.Handlers;
using MikuSB.SdkServer.Utils;
using MikuSB.Util;
using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace MikuSB.SdkServer;

/// <summary>
/// The admin panel runs as its own tiny web host rather than as an extra listener of <see cref="SdkServer"/>.
/// The game host binds six HTTPS ports and touches the certificate helper eagerly; any certificate problem
/// (sandbox ACLs, a missing key container, an unreadable pfx) makes its <c>Start()</c> throw and then the
/// whole host is dead -- verified 2026-10-28: "Failed to start the SDK/HTTP (dispatch) server / 拒绝访问"
/// left every port, including the panel, closed. The panel is plain HTTP on loopback and has no business
/// depending on those certificates, so it gets a separate host and a separate failure path.
/// </summary>
public static class AdminPanelHost
{
    private static readonly Logger Logger = new("AdminPanel");

    public static WebApplication? Instance { get; private set; }

    public static void Start()
    {
        var port = AdminApi.Port;
        if (port <= 0)
        {
            Logger.Info("Admin panel disabled (HttpServer.AdminPort <= 0).");
            return;
        }

        if (!Directory.Exists(AdminApi.WebRoot))
            Logger.Warn($"Admin panel assets not found at {AdminApi.WebRoot}; only the JSON API will answer.");

        // Materialise the token now. IsValid() short-circuits on an empty candidate, so a purely lazy getter
        // would never generate one for the operator who has no token to present yet.
        _ = AdminToken.Current;

        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ContentRootPath = AppContext.BaseDirectory
            });
            builder.Logging.ClearProviders();

            // Explicit URL: keeps Kestrel off the ASPNETCORE_URLS default and off every HTTPS endpoint.
            builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

            builder.Services.AddControllers()
                .AddApplicationPart(typeof(AdminApiController).Assembly)
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
                });

            var app = builder.Build();

            if (Directory.Exists(AdminApi.WebRoot))
            {
                var files = new PhysicalFileProvider(AdminApi.WebRoot);
                app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
                // Revalidate instead of trusting the cache: the panel ships with the server, and a stale
                // app.js after an upgrade is far more confusing than one extra 304 round trip.
                app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = files,
                    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache",
                });
            }

            app.UseRouting();
            app.UseMiddleware<Utils.AdminAuthMiddleware>();

            app.MapControllers();
            app.MapFallback(async context =>
            {
                var path = context.Request.Path.Value ?? "";

                // A JSON 404 beats the SPA shell for typos in API paths: the front-end needs a parseable
                // answer, not an HTML page it would try to JSON.parse.
                if (path.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    context.Response.ContentType = "application/json; charset=utf-8";
                    await context.Response.WriteAsync("{\"code\":\"not_found\",\"message\":\"unknown api route\"}");
                    return;
                }

                if (File.Exists(AdminApi.IndexFile))
                {
                    context.Response.ContentType = "text/html; charset=utf-8";
                    await context.Response.SendFileAsync(AdminApi.IndexFile);
                    return;
                }

                context.Response.StatusCode = StatusCodes.Status404NotFound;
            });

            app.Start();
            Instance = app;

            var token = AdminToken.Current;
            var url = $"http://127.0.0.1:{port}/?token={token}";
            Logger.Info($"Admin panel listening on http://127.0.0.1:{port} (loopback only, bearer token required).");
            AnnounceUrl(url, token);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to start the admin panel on 127.0.0.1:{port}.", ex);
        }
    }

    /// <summary>
    /// A double-clicked build has no other way of telling the operator the token: the console scrolls past the
    /// log lines and without the token every request is a 401. Print a block that is easy to copy, then open
    /// the browser with <c>?token=</c> already applied so the first login is a single click.
    /// </summary>
    private static void AnnounceUrl(string url, string token)
    {
        try
        {
            var line = new string('-', 72);
            Console.WriteLine();
            Console.WriteLine(line);
            Console.WriteLine(" MikuSB admin panel / 管理面板");
            Console.WriteLine($"   URL   : http://127.0.0.1:{new Uri(url).Port}/");
            Console.WriteLine($"   Token : {token}");
            Console.WriteLine($"   Login : {url}");
            Console.WriteLine(line);
            Console.WriteLine();
        }
        catch (IOException)
        {
            // No console attached (service host): the log line above carries the same information.
        }

        if (!ConfigManager.Config.HttpServer.OpenAdminPanel)
            return;

        if (Environment.GetEnvironmentVariable("MIKUSB_NO_BROWSER") is { Length: > 0 })
            return;

        if (!Environment.UserInteractive)
            return;

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            Logger.Info("Opened the admin panel in the default browser.");
        }
        catch (Exception ex)
        {
            Logger.Warn($"Could not open the browser automatically: {ex.Message}. Open the URL above by hand.");
        }
    }
}

