using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using Google.Protobuf;
using MikuSB.Proto;

// L2 probe for the activity-era audit (docs/progress/069). Run it against a live server:
//   dotnet build tests/EraSmoke/EraSmoke.csproj -c Debug
//   dotnet tests/EraSmoke/bin/Debug/net10.0/era-smoke.dll <comboToken> 14
// Speaks the same 4-byte LE framing as .temp/smoke, logs in with a real ComboToken, then watches
// what the server pushes without being asked: the era client patches all arrive as NtfScript(1010)
// with Api == "GM" carrying a JSON body whose "code" member is the Lua payload; the pinned
// attributes arrive as NtfSyncAttr(1004).
//
// The assertions are about the delivered frame, not the source: the payload that actually went on
// the wire must already be token-free and ASCII, and the try payload must carry a set, not a sequence.
internal static class Program
{
    private const ushort ReqLoginId = 1;
    private const ushort RspLoginId = 2;
    private const int NtfScriptId = 1010;
    private const int NtfSyncAttrId = 1004;

    private const string Forbidden = "+<>&'\"";
    private const string Envelope = "{\"code\":";
    private const string CodeKey = "code";

    private static int _failed;

    private static readonly (string Marker, string What)[] EraPatches =
    [
        ("MikuGlobalTimePatched", "GlobalTimeWindows"),
        ("MikuEraFaceShown", "ActivityFaceQuiet"),
        ("MikuTryWin2", "GachaTryWindows"),
        ("MikuPoolFloor", "GachaPoolWindows"),
        ("bMikuWindowEra", "IbshopWindows"),
        ("bMikuFarmEra", "FarmWarWindows"),
    ];

    private static async Task<int> Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: era-smoke <comboToken> [seconds=12]");
            return 2;
        }

        var token = args[0];
        var seconds = args.Length > 1 ? int.Parse(args[1]) : 12;

        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync("127.0.0.1", 21000);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[era] connect failed: " + ex.Message);
            return 3;
        }

        await using var stream = client.GetStream();
        Console.WriteLine("[era] connected to 127.0.0.1:21000");

        await WriteFrameAsync(stream, ReqLoginId, new ReqLogin { Provider = "snowbreak", Token = token }.ToByteArray());
        Console.WriteLine("[era] -> ReqLogin(1)");

        var patches = new Dictionary<string, string>(StringComparer.Ordinal);
        var argsByMarker = new Dictionary<string, string>(StringComparer.Ordinal);
        var otherScripts = new List<string>();
        var syncAttr = 0;
        var cmdCounts = new Dictionary<int, int>();
        var gotLogin = false;

        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            var frame = await ReadFrameAsync(stream, TimeSpan.FromSeconds(seconds));
            if (frame is null)
                break;

            var (cmd, payload) = frame.Value;
            cmdCounts[cmd] = cmdCounts.GetValueOrDefault(cmd) + 1;

            if (cmd == RspLoginId)
            {
                gotLogin = true;
                var json = JsonFormatter.Default.Format(RspLogin.Parser.ParseFrom(payload));
                Console.WriteLine("[era] <- RspLogin(2) " + json.Length + " chars");
                continue;
            }

            if (cmd == NtfSyncAttrId)
            {
                syncAttr++;
                continue;
            }

            if (cmd != NtfScriptId)
                continue;

            NtfCallScript script;
            try
            {
                script = NtfCallScript.Parser.ParseFrom(payload);
            }
            catch (Exception ex)
            {
                Verify(false, "NtfScript(1010) did not parse: " + ex.Message);
                continue;
            }

            if (script.Api != "GM")
            {
                otherScripts.Add(script.Api);
                continue;
            }

            var code = ExtractCode(script.Arg);
            if (code is null)
            {
                Verify(false, "GM script without a code member");
                continue;
            }

            // The dev-only diagnostics payload mentions every marker it reports on, so it would win the
            // lookup below and then fail the "no MIKU_ placeholder" rule on its own MIKU_CAP prints.
            if (code.Contains("MIKU_CAP ", StringComparison.Ordinal))
            {
                otherScripts.Add("GM:diagnostics(" + code.Length + "ch)");
                continue;
            }

            var hit = EraPatches.FirstOrDefault(p => code.Contains(p.Marker, StringComparison.Ordinal));
            if (hit.Marker is null)
            {
                var head = code.Length > 40 ? code[..40] : code;
                otherScripts.Add("GM:" + code.Length + "ch:" + head);
                continue;
            }

            patches[hit.Marker] = code;
            argsByMarker[hit.Marker] = script.Arg;
            Console.WriteLine("[era] <- " + hit.What + ": " + code.Length + " chars");
        }

        Console.WriteLine();
        Console.WriteLine("=== received ===");
        foreach (var kv in cmdCounts.OrderByDescending(k => k.Value))
            Console.WriteLine("  cmd " + kv.Key + ": " + kv.Value + " frame(s)");
        if (otherScripts.Count > 0)
            Console.WriteLine("  other scripts: " + string.Join(", ", otherScripts.Distinct()));

        Console.WriteLine();
        Console.WriteLine("=== assertions ===");
        Verify(gotLogin, "RspLogin received");
        Verify(syncAttr > 0, "NtfSyncAttr(1004) frames = " + syncAttr + " (pinned attributes re-seeded)");

        foreach (var (marker, what) in EraPatches)
        {
            if (!patches.TryGetValue(marker, out var lua))
            {
                Verify(false, what + " pushed (marker " + marker + ")");
                continue;
            }

            Verify(true, what + " pushed (" + lua.Length + " chars)");
            Verify(!lua.Contains("MIKU_", StringComparison.Ordinal), what + " has no MIKU_ placeholder");
            Verify(!lua.Any(ch => ch > 0x7E), what + " is pure ASCII");

            var bad = Forbidden.Where(lua.Contains).ToArray();
            var badText = bad.Length == 0 ? "" : ": " + string.Join(" ", bad);
            Verify(bad.Length == 0, what + " has no forbidden character" + (bad.Length == 1 ? "" : "s") + badText);

            Verify(argsByMarker.TryGetValue(marker, out var arg) && arg.Contains(Envelope, StringComparison.Ordinal),
                what + " arrived inside a code envelope");
        }

        if (patches.TryGetValue("MikuTryWin2", out var tryLua))
        {
            Verify(tryLua.Contains("local idset = {}", StringComparison.Ordinal),
                "GachaTryWindows builds an idset (a bare sequence indexes by position and matches nothing)");
            Verify(tryLua.Contains("idset[row.nId]", StringComparison.Ordinal),
                "GachaTryWindows tests membership through the idset");

            var idcount = ParseInt(tryLua, "local idcount = ");
            var ids = ParseIds(tryLua);
            Verify(idcount is not null && ids.Count == idcount,
                "GachaTryWindows idcount matches the emitted list (" + ids.Count + " vs " + (idcount?.ToString() ?? "?") + ")");
            Verify(ids.Count > 0, "GachaTryWindows carries at least one activity id (" + string.Join(",", ids) + ")");

            var guardAt = tryLua.IndexOf("MikuTryWin2 and", StringComparison.Ordinal);
            var restoreAt = tryLua.IndexOf("if MikuTryRaw then", StringComparison.Ordinal);
            Verify(guardAt >= 0 && restoreAt >= 0 && guardAt < restoreAt,
                "GachaTryWindows checks idempotency before restoring the snapshot");

            Console.WriteLine("   try ids: " + string.Join(",", ids));
        }

        Console.WriteLine();
        if (_failed == 0)
            Console.WriteLine("EraSmoke: all checks passed.");
        else
            Console.WriteLine("EraSmoke: " + _failed + " check(s) FAILED.");

        return _failed == 0 ? 0 : 1;
    }

    private static void Verify(bool ok, string what)
    {
        if (!ok)
            _failed++;
        Console.WriteLine("  " + (ok ? "PASS" : "FAIL") + "  " + what);
    }

    private static string ExtractCode(string arg)
    {
        if (string.IsNullOrWhiteSpace(arg))
            return null;

        try
        {
            return JsonNode.Parse(arg)?[CodeKey]?.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }

    private static int? ParseInt(string lua, string prefix)
    {
        var i = lua.IndexOf(prefix, StringComparison.Ordinal);
        if (i < 0)
            return null;
        var start = i + prefix.Length;
        var end = start;
        while (end < lua.Length && (char.IsDigit(lua[end]) || lua[end] == '-'))
            end++;
        return end > start && int.TryParse(lua[start..end], out var v) ? v : null;
    }

    private static List<uint> ParseIds(string lua)
    {
        var list = new List<uint>();
        var i = lua.IndexOf("local ids = {", StringComparison.Ordinal);
        if (i < 0)
            return list;

        var start = i + "local ids = {".Length;
        var end = lua.IndexOf('}', start);
        if (end < 0)
            return list;

        foreach (var part in lua[start..end].Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (uint.TryParse(part.Trim(), out var id) && id != 0)
                list.Add(id);

        return list;
    }

    private static async Task WriteFrameAsync(Stream stream, ushort cmd, byte[] payload)
    {
        var buffer = new byte[6 + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0, 4), (uint)(2 + payload.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4, 2), cmd);
        payload.CopyTo(buffer.AsSpan(6));
        await stream.WriteAsync(buffer);
        await stream.FlushAsync();
    }

    private static async Task<(int Cmd, byte[] Payload)?> ReadFrameAsync(Stream stream, TimeSpan timeout)
    {
        var header = new byte[4];
        if (!await ReadExactAsync(stream, header, timeout))
            return null;

        var len = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (len < 2 || len > 64 * 1024 * 1024)
            return null;

        var rest = new byte[len];
        if (!await ReadExactAsync(stream, rest, timeout))
            return null;

        return (BinaryPrimitives.ReadUInt16LittleEndian(rest.AsSpan(0, 2)), rest[2..]);
    }

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var offset = 0;
        try
        {
            while (offset < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(offset), cts.Token);
                if (read <= 0)
                    return false;
                offset += read;
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
