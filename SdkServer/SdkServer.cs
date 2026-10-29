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

                        var bindAddr = System.Net.IPAddress.Parse(ConfigManager.Config.HttpServer.BindAddress);
                        // 11443 = js2sdk.xoyo.com (CN jinshan account API), 19443 = sh-qrcode.xgsdk.com,
                        // 443 = xgdata.xgsdk.com / gpm-conf.testplus.cn; the rest are the international ports.
                        foreach (var port in new[] { ConfigManager.Config.HttpServer.Port, 443, 11443, 13443, 18443, 19443, 31443 })
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
        }

        // Separate host on purpose: the panel must survive a certificate/bind failure of the game host above.
        AdminPanelHost.Start();
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
