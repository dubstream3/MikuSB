namespace MikuSB.Util;

public sealed record InGameConsoleCommandResponse(bool Success, IReadOnlyList<string> Messages);

public static class InGameConsoleBridge
{
    public static Func<string, CancellationToken, Task<InGameConsoleCommandResponse>>? ExecuteCommandAsync { get; set; }

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
