using Microsoft.AspNetCore.Http;
using MikuSB.Util;
using System.Text;

namespace MikuSB.SdkServer.Utils;

public class RequestLoggingMiddleware(RequestDelegate next)
{
    private const long MaxLoggedBodyBytes = 1024 * 1024;

    private static bool ShouldSkip(string path)
        => path.StartsWith("/report")
            || path.StartsWith("/data/report")
            || path.StartsWith("/bisdk/batchpush")
            || path.Contains("/log/")
            || path == "/alive";

    private static bool ShouldLogBody(HttpRequest request)
    {
        if (request.ContentLength is null or 0)
            return false;

        if (request.ContentLength > MaxLoggedBodyBytes)
            return false;

        if (string.IsNullOrWhiteSpace(request.ContentType))
            return false;

        return request.ContentType.Contains("json", StringComparison.OrdinalIgnoreCase)
            || request.ContentType.Contains("text", StringComparison.OrdinalIgnoreCase)
            || request.ContentType.Contains("xml", StringComparison.OrdinalIgnoreCase)
            || request.ContentType.Contains("x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeForLog(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch == '\r')
            {
                builder.Append(@"\r");
                continue;
            }

            if (ch == '\n')
            {
                builder.Append(@"\n");
                continue;
            }

            if (char.IsControl(ch))
            {
                builder.Append(@"\u");
                builder.Append(((int)ch).ToString("x4"));
                continue;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    private static async Task<string> ReadBodyAsString(HttpRequest request)
    {
        request.EnableBuffering();
        request.Body.Position = 0;

        using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();

        request.Body.Position = 0;
        return body;
    }

    public async Task InvokeAsync(HttpContext context, Logger logger)
    {
        var request = context.Request;
        var method = request.Method;
        var path = request.Path.ToString();
        var pathWithQuery = path + request.QueryString;
        var host = request.Host.Value;

        // The CN client reaches several different hosts (sh-jxsj.xgsdk.com, js2sdk.xoyo.com, ...)
        // on the same listener, so the Host header has to be part of every log line.
        var enabled = ConfigManager.Config.HttpServer.EnableLog && !ShouldSkip(path);

        if (enabled)
        {
            var body = ShouldLogBody(request)
                ? SanitizeForLog(await ReadBodyAsString(request))
                : "<omitted>";
            logger.Info($"REQ [{host}] {method} {pathWithQuery} body={body}");
        }

        var originalBody = context.Response.Body;
        MemoryStream? buffer = null;
        if (enabled)
        {
            buffer = new MemoryStream();
            context.Response.Body = buffer;
        }

        try
        {
            await next(context);
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        var statusCode = context.Response.StatusCode;

        if (buffer != null)
        {
            buffer.Position = 0;
            string responseText;
            using (var reader = new StreamReader(buffer, Encoding.UTF8, leaveOpen: true))
            {
                responseText = await reader.ReadToEndAsync();
            }

            if (responseText.Length > 4096)
                responseText = responseText[..4096] + "...<truncated>";

            var line = $"RSP [{host}] {method} {pathWithQuery} => {statusCode} body={SanitizeForLog(responseText)}";
            if (statusCode == 200)
            {
                logger.Info(line);
            }
            else if (statusCode == 404)
            {
                logger.Warn(line);
            }
            else
            {
                logger.Error(line);
            }

            buffer.Position = 0;
            await buffer.CopyToAsync(originalBody);
        }
        else if (!ShouldSkip(path) && ConfigManager.Config.HttpServer.EnableLog)
        {
            if (statusCode == 200)
            {
                logger.Info($"{method} {pathWithQuery} => {statusCode}");
            }
            else if (statusCode == 404)
            {
                logger.Warn($"{method} {pathWithQuery} => {statusCode}");
            }
            else
            {
                logger.Error($"{method} {pathWithQuery} => {statusCode}");
            }
        }

        buffer?.Dispose();
    }
}
