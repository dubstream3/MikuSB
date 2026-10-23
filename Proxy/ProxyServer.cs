using System.Buffers;
using System.Net;
using System.Net.Sockets;
using MikuSB.Configuration;
using MikuSB.Util;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace MikuSB.Proxy;

public sealed class ProxyServer(
    IOptions<ProxyOptions> options,
    Logger logger) : BackgroundService
{
    private const string ListenAddress = "127.0.0.1";
    private const int DefaultSocksPort = 18888;

    private static readonly string[] TargetDomains =
    [
        "amazingseasuncdn.com",
        "amazingseasun.com",
        "seasungames.com",
        "snowbreak-game.com",
        "xgsdk.com",
        "xoyo.games",
        "yo.games",
        "qcloud.com",
        "xqdata.xoyo.games",
        "tencentcs.com"
    ];

    private readonly ProxyOptions _options = options.Value;
    private readonly List<TcpListener> _listeners = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.Info("MikuSB proxy is disabled");
            return;
        }

        foreach (var port in GetListenPorts())
        {
            var listener = new TcpListener(IPAddress.Parse(ListenAddress), port);
            listener.Start();
            _listeners.Add(listener);
            logger.Info($"MikuSB SOCKS5 proxy listening on {ListenAddress}:{port}");
            _ = Task.Run(() => AcceptLoopAsync(listener, port, stoppingToken), stoppingToken);
        }

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        var stopTask = base.StopAsync(cancellationToken);
        foreach (var listener in _listeners)
            listener.Stop();
        await stopTask;
    }

    private IEnumerable<int> GetListenPorts()
    {
        yield return DefaultSocksPort;

        if (_options.Port > 0 && _options.Port != DefaultSocksPort)
            yield return _options.Port;
    }

    private async Task AcceptLoopAsync(TcpListener listener, int port, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken);
                _ = Task.Run(() => HandleClientAsync(client, port, cancellationToken), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (SocketException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task HandleClientAsync(TcpClient client, int listenPort, CancellationToken cancellationToken)
    {
        using (client)
        {
            using var clientStream = client.GetStream();

            try
            {
                await NegotiateAsync(clientStream, cancellationToken);
                var request = await ReadConnectRequestAsync(clientStream, cancellationToken);
                if (request is null)
                    return;

                using var upstream = new TcpClient();
                var destination = ResolveDestination(request, listenPort);

                await upstream.ConnectAsync(destination.Host, destination.Port, cancellationToken);
                await SendConnectReplyAsync(clientStream, success: true, cancellationToken);

                if (ConfigManager.Config.HttpServer.EnableLog)
                    logger.Info($"SOCKS: {request.Host}:{request.Port} -> {destination.Host}:{destination.Port}");

                using var upstreamStream = upstream.GetStream();
                await TunnelAsync(clientStream, upstreamStream, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex) when (ex is IOException or SocketException)
            {
                if (ConfigManager.Config.HttpServer.EnableLog)
                    logger.Warn($"SOCKS client failed: {ex.Message}");
            }
            catch (Exception ex)
            {
                logger.Warn($"SOCKS client failed: {ex}");
            }
        }
    }

    private async Task NegotiateAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var header = new byte[2];
        await ReadExactAsync(stream, header, cancellationToken);

        if (header[0] != 0x05)
            throw new IOException("Unsupported SOCKS version");

        var methods = new byte[header[1]];
        if (methods.Length > 0)
            await ReadExactAsync(stream, methods, cancellationToken);

        await stream.WriteAsync(new byte[] { 0x05, 0x00 }, cancellationToken);
    }

    private async Task<SocksRequest?> ReadConnectRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        await ReadExactAsync(stream, header, cancellationToken);

        if (header[0] != 0x05)
            throw new IOException("Invalid SOCKS request");

        if (header[1] != 0x01)
        {
            await SendConnectReplyAsync(stream, success: false, cancellationToken);
            return null;
        }

        var host = header[3] switch
        {
            0x01 => new IPAddress(await ReadBytesAsync(stream, 4, cancellationToken)).ToString(),
            0x03 => await ReadDomainAsync(stream, cancellationToken),
            0x04 => new IPAddress(await ReadBytesAsync(stream, 16, cancellationToken)).ToString(),
            _ => throw new IOException("Unsupported address type")
        };

        var portBytes = await ReadBytesAsync(stream, 2, cancellationToken);
        var port = (portBytes[0] << 8) | portBytes[1];
        return new SocksRequest(host, port);
    }

    private static async Task<string> ReadDomainAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var length = await ReadBytesAsync(stream, 1, cancellationToken);
        var domain = await ReadBytesAsync(stream, length[0], cancellationToken);
        return System.Text.Encoding.ASCII.GetString(domain);
    }

    private (string Host, int Port) ResolveDestination(SocksRequest request, int listenPort)
    {
        if (IsSelfReference(request.Host, request.Port, listenPort))
            throw new IOException("Proxy self-reference detected");

        if (!ShouldRedirect(request.Host))
            return (request.Host, request.Port);

        return ("127.0.0.1", request.Port switch
        {
            80 => ConfigManager.Config.HttpServer.Port,
            893 => 31443,
            13443 => 13443,
            18443 => 18443,
            31443 => 31443,
            _ => 13443
        });
    }

    private static bool ShouldRedirect(string host)
    {
        foreach (var domain in TargetDomains)
        {
            if (host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private bool IsSelfReference(string host, int port, int listenPort)
    {
        if (port != listenPort && port != _options.Port && port != DefaultSocksPort)
            return false;

        return host is "127.0.0.1" or "localhost" or "::1";
    }

    private static async Task SendConnectReplyAsync(NetworkStream stream, bool success, CancellationToken cancellationToken)
    {
        var reply = success
            ? new byte[] { 0x05, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }
            : new byte[] { 0x05, 0x05, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        await stream.WriteAsync(reply, cancellationToken);
    }

    private static async Task TunnelAsync(NetworkStream clientStream, NetworkStream upstreamStream, CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var upstreamToClient = CopyAsync(upstreamStream, clientStream, linkedCts.Token);
        var clientToUpstream = CopyAsync(clientStream, upstreamStream, linkedCts.Token);

        await Task.WhenAny(upstreamToClient, clientToUpstream);
        linkedCts.Cancel();

        try
        {
            await Task.WhenAll(upstreamToClient, clientToUpstream);
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
    }

    private static async Task CopyAsync(Stream source, Stream destination, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            while (true)
            {
                var bytesRead = await source.ReadAsync(buffer, cancellationToken);
                if (bytesRead <= 0)
                    break;

                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                await destination.FlushAsync(cancellationToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task<byte[]> ReadBytesAsync(Stream stream, int length, CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        await ReadExactAsync(stream, buffer, cancellationToken);
        return buffer;
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (bytesRead <= 0)
                throw new IOException("Unexpected EOF");
            offset += bytesRead;
        }
    }

    private sealed record SocksRequest(string Host, int Port);
}
