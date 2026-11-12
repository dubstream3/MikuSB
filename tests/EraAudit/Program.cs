using System.Reflection;
using MikuSB.GameServer.Game.Dlc;

// L1 (static/deterministic) checks for the era-replay layer. Runs without a game client and without
// Lua: the payloads are generated strings, so everything that can be proven here is proven here.
// The behavioural half (Lua execution, database state, real client) is listed in
// docs/progress/061-activity-era-audit.md and is NOT covered by this program.

var failures = new List<string>();
var passed = 0;

void Check(string name, bool ok, string? detail = null)
{
    if (ok)
    {
        passed++;
        return;
    }

    failures.Add(detail is null ? name : $"{name}: {detail}");
}

const long floor = 1769630400; // DLC 35 EnterStartTime
const long stop = 1770000000;  // arbitrary era end

// DlcEraWindow: half-open [start, end), 0 = unset, -1/nil handled by callers before conversion.
Check("historical end==floor is historical", DlcEraWindow.IsHistorical(floor, floor));
Check("historical end<floor is historical", DlcEraWindow.IsHistorical(floor, floor - 1));
Check("historical end>floor is not", !DlcEraWindow.IsHistorical(floor, floor + 1));
Check("historical floor==0 is not", !DlcEraWindow.IsHistorical(0, 123));
Check("historical end==0 is not", !DlcEraWindow.IsHistorical(floor, 0));

Check("inside start==floor belongs", DlcEraWindow.IsInside(floor, stop, floor, stop + 1));
Check("inside end==floor does not belong", !DlcEraWindow.IsInside(floor, stop, floor - 10, floor));
Check("inside start==stop does not belong", !DlcEraWindow.IsInside(floor, stop, stop, stop + 10));
Check("inside after stop does not belong", !DlcEraWindow.IsInside(floor, stop, stop + 1, stop + 10));
Check("inside before floor does not belong", !DlcEraWindow.IsInside(floor, stop, floor - 100, floor - 50));
Check("inside without pin keeps shipped semantics", DlcEraWindow.IsInside(0, 0, 1, 2));
Check("inside unknown start is not membership", !DlcEraWindow.IsInside(floor, stop, 0, stop - 1));
Check("inside without stop accepts open end", DlcEraWindow.IsInside(floor, 0, floor, stop + 10));

Check("intersects overlap", DlcEraWindow.Intersects(floor, stop, floor - 10, floor + 10));
Check("intersects ending exactly at floor does not", !DlcEraWindow.Intersects(floor, stop, floor - 10, floor));
Check("intersects starting exactly at stop does not", !DlcEraWindow.Intersects(floor, stop, stop, stop + 10));
Check("intersects future does not", !DlcEraWindow.Intersects(floor, stop, stop + 10, stop + 20));
Check("intersects without pin is false", !DlcEraWindow.Intersects(0, 0, 1, 2));

// Every large Lua constant must be decodable by the client: HandlerReqLogin.SendClientPatch drops
// payloads holding '+' '<' '>' '&' '\'' '"' or any non-ASCII character (Json.lua/Lua 5.1), and any
// unsubstituted MIKU_ token.
var forbidden = new[] { '+', '<', '>', '&', '\'', '"' };
var fields = typeof(DlcClientPatches)
    .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
    .Where(f => f.IsLiteral && f.FieldType == typeof(string))
    .OrderBy(f => f.Name);

foreach (var field in fields)
{
    if (field.GetRawConstantValue() is not string body || body.Length < 200)
        continue;

    foreach (var bad in forbidden)
        Check($"const {field.Name} has no '{bad}'", !body.Contains(bad), $"offset {body.IndexOf(bad)}");

    Check($"const {field.Name} is ASCII", body.All(ch => ch <= 0x7E));
}

var eraPayloads = new (string Name, string Body)[]
{
    ("GlobalTimeWindows", DlcClientPatches.GlobalTimeWindowsFor(floor, stop, 35)),
    ("ActivityFaceQuiet", DlcClientPatches.ActivityFaceQuietFor(floor, stop, 35)),
    ("GachaTryWindows", DlcClientPatches.GachaTryWindowsFor(floor, stop, 35, new uint[] { 561, 576 })),
    ("GachaPoolWindows", DlcClientPatches.GachaPoolWindowsFor(floor, stop, 35)),
    ("IbshopWindows", DlcClientPatches.IbshopWindowsFor(floor, 35)),
    ("FarmWarWindows", DlcClientPatches.FarmWarWindowsFor(floor, 35)),
};

foreach (var (name, body) in eraPayloads)
{
    Check($"{name} rendered without token", !body.Contains("MIKU_", StringComparison.Ordinal));
    Check($"{name} rendered ASCII", body.All(ch => ch <= 0x7E));
    foreach (var bad in forbidden)
        Check($"{name} rendered without '{bad}'", !body.Contains(bad));
}

var try35 = DlcClientPatches.GachaTryWindowsFor(floor, stop, 35, new uint[] { 561, 576 });
Check("try payload injects id list", try35.Contains("local ids = {561,576}", StringComparison.Ordinal));
Check("try payload injects id count", try35.Contains("local idcount = 2", StringComparison.Ordinal));
Check("try payload gates the loop on the id set", try35.Contains("if idset[row.nId] then", StringComparison.Ordinal));
Check("try payload builds the id set from the sequence", try35.Contains("for _, id in ipairs(ids) do idset[id] = true end", StringComparison.Ordinal));
Check("try payload rejects end==floor", try35.Contains("e ~= floor", StringComparison.Ordinal));
Check("try payload rejects start==stop", try35.Contains("math.max(s, stop) == s then inside = false", StringComparison.Ordinal));

var tryEmpty = DlcClientPatches.GachaTryWindowsFor(floor, stop, 35, Array.Empty<uint>());
Check("try payload with no ids is an empty table", tryEmpty.Contains("local ids = {}", StringComparison.Ordinal));
Check("try payload with no ids has count 0", tryEmpty.Contains("local idcount = 0", StringComparison.Ordinal));

foreach (var name in new[] { "IbshopWindows", "FarmWarWindows" })
{
    var off = name == "IbshopWindows"
        ? DlcClientPatches.IbshopWindowsFor(0, 0)
        : DlcClientPatches.FarmWarWindowsFor(0, 0);
    Check($"{name} Off restores instead of stamping", off.Contains("if not on then return end", StringComparison.Ordinal));
}

// --emit <dir>: dump the same rendered payloads the server sends, so the Lua harness executes the
// real bytes instead of a hand-copied fixture (contract 6.1: export from the actual C# path).
var emitAt = Array.IndexOf(args, "--emit");
if (emitAt >= 0 && emitAt + 1 < args.Length)
{
    var dir = args[emitAt + 1];
    Directory.CreateDirectory(dir);
    foreach (var (name, body) in eraPayloads)
        File.WriteAllText(Path.Combine(dir, name + ".lua"), body);
    File.WriteAllText(Path.Combine(dir, "GachaTryWindows.empty.lua"),
        DlcClientPatches.GachaTryWindowsFor(floor, stop, 35, Array.Empty<uint>()));
    File.WriteAllText(Path.Combine(dir, "IbshopWindows.off.lua"), DlcClientPatches.IbshopWindowsFor(0, 0));
    File.WriteAllText(Path.Combine(dir, "FarmWarWindows.off.lua"), DlcClientPatches.FarmWarWindowsFor(0, 0));
    File.WriteAllText(Path.Combine(dir, "GachaTryWindows.off.lua"),
        DlcClientPatches.GachaTryWindowsFor(0, 0, 0, new uint[] { 561, 576 }));
    File.WriteAllText(Path.Combine(dir, "GlobalTimeWindows.off.lua"), DlcClientPatches.GlobalTimeWindowsFor(0, 0, 0));
    File.WriteAllText(Path.Combine(dir, "ActivityFaceQuiet.off.lua"), DlcClientPatches.ActivityFaceQuietFor(0, 0, 0));
    File.WriteAllText(Path.Combine(dir, "GachaPoolWindows.off.lua"), DlcClientPatches.GachaPoolWindowsFor(0, 0, 0));
    Console.WriteLine($"EraAudit: emitted {eraPayloads.Length + 7} payload(s) to {dir}");
}

var audit = DlcClientPatches.AuditEraPayloads();
Check("AuditEraPayloads reports nothing", audit.Count == 0, string.Join("; ", audit));

Console.WriteLine($"EraAudit: {passed} check(s) passed, {failures.Count} failed.");
foreach (var failure in failures)
    Console.WriteLine($"  FAIL {failure}");

return failures.Count == 0 ? 0 : 1;
