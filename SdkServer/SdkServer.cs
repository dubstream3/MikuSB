using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MikuSB.Proxy;
using MikuSB.SdkServer.Handlers;
using MikuSB.SdkServer.Utils;
using MikuSB.Util;
using System.Text.Json;

namespace MikuSB.SdkServer;

public static class SdkServer
{
    private static readonly Logger Logger = new("SdkServer");

    public static void Start(string[] args)
    {
        // The SDK ports are hard-coded in the client, so a port somebody else owns is the operator's problem to
        // solve - but they have to be told which one, and the remaining ports are still worth serving. Binding them
        // blind ended in "the SDK server failed to start" with no hint about what was holding the port.
        var bindAddr = System.Net.IPAddress.Parse(ConfigManager.Config.HttpServer.BindAddress);
        var wantedPorts = new[]
        {
            ConfigManager.Config.HttpServer.Port, 443, 11443, 13443, 18443, 19443, 31443
        };
        var busyPorts = PortProbe.FindBusy(bindAddr, wantedPorts);
        var listenPorts = wantedPorts.Where(port => !busyPorts.Contains(port)).ToArray();

        if (busyPorts.Count > 0)
        {
            ReportBusyPorts(busyPorts, listenPorts);
        }

        if (listenPorts.Length == 0)
        {
            // Nothing left to listen on: skip Kestrel entirely, a host without endpoints would fall back to the
            // default http://localhost:5000 and pretend to be healthy.
            AdminPanelHost.Start();
            return;
        }

        var builder = Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder
                    .UseStartup<Startup>()
                    .ConfigureLogging((_, logging) => { logging.ClearProviders(); })
                    .ConfigureKestrel(serverOptions =>
                    {
                        // Pre-warm cert before first TLS handshake
                        _ = Utils.CertHelper.GetOrCreate(null);

                        // 11443 = js2sdk.xoyo.com (CN jinshan account API), 19443 = sh-qrcode.xgsdk.com,
                        // 443 = xgdata.xgsdk.com / gpm-conf.testplus.cn; the rest are the international ports.
                        foreach (var port in listenPorts)
                        {
                            serverOptions.Listen(bindAddr, port, o =>
                            {
                                o.UseHttps(https =>
                                {
                                    https.ServerCertificateSelector = (_, sni) =>
                                        Utils.CertHelper.GetOrCreate(sni);
                                });
                            });
                        }
                    });
            });

        var host = builder.Build();
        try
        {
            // Start() instead of RunAsync() on purpose. RunAsync() hands back a Task nobody observes,
            // so any Kestrel bind failure (port already taken, certificate problem, ...) killed the
            // dispatch/HTTPS listener silently: the process stayed alive, the "listening on ..." line
            // was still logged, and the only symptom was a port that was not actually open -- which
            // makes the game client fail to log in with no visible cause anywhere. Surface it instead.
            host.Start();
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to start the SDK/HTTP (dispatch) server.", ex);
            Task.Run(() => DesktopNotice.ShowError(
                "MikuSB - SDK 端口启动失败 / SDK ports failed",
                "SDK/HTTP 服务没能启动，游戏客户端将无法登录本服务器。\n" +
                "The SDK/HTTP server did not start, so the client cannot log in.\n\n" +
                ex.Message));
        }

        // Separate host on purpose: the panel must survive a certificate/bind failure of the game host above.
        AdminPanelHost.Start();
    }

    /// <summary>
    ///     Names the ports another program owns. This is a "you have to fix it" message, not a warning the server can
    ///     work around: the client dials those numbers and nothing else.
    /// </summary>
    private static void ReportBusyPorts(IReadOnlyList<int> busyPorts, IReadOnlyList<int> listenPorts)
    {
        var ports = string.Join(", ", busyPorts);
        var remaining = listenPorts.Count == 0
            ? "现在没有任何 SDK 端口可用，游戏无法连接。/ No SDK port is available, the client cannot connect."
            : $"其余端口照常监听：{string.Join(", ", listenPorts)} / Still listening on: {string.Join(", ", listenPorts)}";

        var message =
            $"端口已被占用：{ports}\n" +
            "这些端口是游戏客户端硬编码的，必须由 MikuSB 独占。请关闭占用它们的程序（常见：另一个 MikuSB 实例、" +
            "其他代理或抓包工具、IIS），然后重新启动。\n" +
            remaining + "\n\n" +
            $"Port(s) already in use: {ports}\n" +
            "The game client hard-codes these ports, so MikuSB has to own them. Close whatever is holding them " +
            "(another MikuSB instance, a proxy/sniffer, IIS) and start again.";

        Logger.Error($"[ports] in use: {ports}; listening: {(listenPorts.Count == 0 ? "none" : string.Join(", ", listenPorts))}");
        Task.Run(() => DesktopNotice.ShowError("MikuSB - 端口被占用 / port already in use", message));
    }
}

public class Startup
{
    private static bool LooksLikeServerListRequest(string path, string? query)
    {
        var value = $"{path}?{query}".ToLowerInvariant();
        return value.Contains("server")
            || value.Contains("version")
            || value.Contains("query_version")
            || value.Contains("serverlist");
    }

    public static void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment()) app.UseDeveloperExceptionPage();

        app.UseWebSockets();
        app.Use(async (context, next) =>
        {
            if (string.Equals(context.Request.Path.Value, "/ws/gui-console", StringComparison.OrdinalIgnoreCase))
            {
                await InGameConsoleWebSocket.HandleAsync(context);
                return;
            }

            await next();
        });
        app.UseRouting();
        app.UseCors("AllowAll");
        app.UseAuthorization();
        app.UseMiddleware<RequestLoggingMiddleware>();
        app.UseEndpoints(endpoints => { endpoints.MapControllers(); });
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
            endpoints.MapFallback(async context =>
            {
                var path = context.Request.Path.Value ?? "";
                if (LooksLikeServerListRequest(path, context.Request.QueryString.Value))
                {
                    var response = RouteController.BuildServerList("");
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(response));
                    return;
                }
                var fallbackResponse = new
                {
                    code = 0,
                    message = "ok",
                    service = ConfigManager.Config.GameServer.GameServerName,
                    path = path,
                    query = context.Request.QueryString.Value ?? ""
                };

                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(fallbackResponse));
            });
        });
    }

    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddCors(options =>
        {
            options.AddPolicy("AllowAll",
                builder => { builder.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader(); });
        });
        services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
            });
        services.AddSingleton<Logger>(_ => new Logger("Proxy"));
        services.AddMikuSbProxy(ConfigManager.Config.Proxy);
    }
}
