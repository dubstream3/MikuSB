using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http;
using MikuSB.Util;

namespace MikuSB.SdkServer.Handlers;

public static class InGameConsoleWebSocket
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task HandleAsync(HttpContext context)
    {
        if (!IsLoopback(context.Connection.RemoteIpAddress))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        using var sendLock = new SemaphoreSlim(1, 1);
        var packetLogs = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        using var packetLogSubscription = InGameConsoleBridge.SubscribePacketLogs(
            message => packetLogs.Writer.TryWrite(message));

        var receiveTask = ReceiveLoopAsync(socket, sendLock, cancellation.Token);
        var packetLogTask = SendPacketLogsAsync(socket, sendLock, packetLogs.Reader, cancellation.Token);

        try
        {
            await Task.WhenAny(receiveTask, packetLogTask);
        }
        finally
        {
            cancellation.Cancel();
            packetLogs.Writer.TryComplete();
            await IgnoreTaskFailureAsync(receiveTask);
            await IgnoreTaskFailureAsync(packetLogTask);

            if (socket.State == WebSocketState.Open)
            {
                await socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Server closed the connection.",
                    CancellationToken.None);
            }
        }
    }

    private static async Task ReceiveLoopAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        CancellationToken cancellationToken)
    {
        try
        {
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var message = await ReceiveTextAsync(socket, cancellationToken);
                if (message is null)
                    return;

                await HandleMessageAsync(socket, sendLock, message, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await SendErrorAsync(socket, sendLock, null, "internal_error", exception.Message, CancellationToken.None);
        }
    }

    private static async Task HandleMessageAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var typeElement)
                || !string.Equals(typeElement.GetString(), "execute_command", StringComparison.Ordinal))
            {
                await SendErrorAsync(socket, sendLock, null, "invalid_message", "Unsupported message type.", cancellationToken);
                return;
            }

            var requestId = root.TryGetProperty("requestId", out var requestIdElement)
                ? requestIdElement.GetString()
                : null;
            var command = root.TryGetProperty("command", out var commandElement)
                ? commandElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(command))
            {
                await SendErrorAsync(socket, sendLock, requestId, "invalid_command", "Command is empty.", cancellationToken);
                return;
            }

            var execute = InGameConsoleBridge.ExecuteCommandAsync;
            if (execute is null)
            {
                await SendErrorAsync(socket, sendLock, requestId, "console_unavailable", "Command service is not ready.", cancellationToken);
                return;
            }

            var startedAt = DateTimeOffset.UtcNow;
            var response = await execute(command, cancellationToken);
            var endedAt = DateTimeOffset.UtcNow;
            var messages = response.Messages.Count == 0
                ? ["Command completed."]
                : response.Messages;

            foreach (var resultMessage in messages)
            {
                await SendAsync(socket, sendLock, new
                {
                    type = "command_result",
                    requestId,
                    success = response.Success,
                    message = resultMessage,
                    startedAt,
                    endedAt
                }, cancellationToken);
            }
        }
        catch (JsonException exception)
        {
            await SendErrorAsync(socket, sendLock, null, "invalid_json", exception.Message, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await SendErrorAsync(socket, sendLock, null, "internal_error", exception.Message, cancellationToken);
        }
    }

    private static async Task SendPacketLogsAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        ChannelReader<string> packetLogs,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var message in packetLogs.ReadAllAsync(cancellationToken))
            {
                await SendAsync(socket, sendLock, new
                {
                    type = "packet_log",
                    message,
                    timestamp = DateTimeOffset.UtcNow
                }, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static async Task<string?> ReceiveTextAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        var text = new StringBuilder();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;
            if (result.MessageType != WebSocketMessageType.Text)
                continue;

            text.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (text.Length > 1_048_576)
                throw new InvalidDataException("WebSocket message is too large.");
        } while (!result.EndOfMessage);

        return text.ToString();
    }

    private static Task SendErrorAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string? requestId,
        string code,
        string message,
        CancellationToken cancellationToken)
    {
        return SendAsync(socket, sendLock, new
        {
            type = "error",
            requestId,
            code,
            message
        }, cancellationToken);
    }

    private static async Task SendAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        object payload,
        CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        await sendLock.WaitAsync(cancellationToken);
        try
        {
            if (socket.State == WebSocketState.Open)
                await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
        }
        finally
        {
            sendLock.Release();
        }
    }

    private static async Task IgnoreTaskFailureAsync(Task task)
    {
        try
        {
            await task;
        }
        catch
        {
        }
    }

    private static bool IsLoopback(IPAddress? address)
    {
        if (address is null)
            return false;
        if (IPAddress.IsLoopback(address))
            return true;
        return address.IsIPv4MappedToIPv6 && IPAddress.IsLoopback(address.MapToIPv4());
    }
}
