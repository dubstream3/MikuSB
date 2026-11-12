using MikuSB.Data;
using MikuSB.GameServer.Game.Player;
using MikuSB.Util;
using Newtonsoft.Json.Linq;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// Server-side evaluator for VirCapture condition tables (captureregion.Condition / LightSwitchCondition,
/// tower_chapter.Condition, timelist rows). Mirrors Condition.lua but is authoritative:
/// an unknown condition type is a hard failure unless MIKU_VIRCAPTURE_ALLOW_UNKNOWN_CONDITIONS=1.
/// See docs/progress/073-新星开拓可执行验收契约.md §8.
/// </summary>
internal static class VirCaptureConditionHelper
{
    private const string AllowUnknownEnv = "MIKU_VIRCAPTURE_ALLOW_UNKNOWN_CONDITIONS";
    private static readonly Logger Logger = new("VirCapture.Condition");
    private static readonly Lazy<bool> AllowUnknown = new(() =>
    {
        var raw = Environment.GetEnvironmentVariable(AllowUnknownEnv);
        var enabled = raw is "1" or "true" or "TRUE";
        if (enabled)
            Logger.Warn($"{AllowUnknownEnv}=1: unknown VirCapture conditions are skipped (test only).");
        return enabled;
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    internal readonly record struct Entry(int Type, long[] Args);

    /// <summary>
    /// Accepts both config shapes found in the resource tables:
    /// {"20":15} (object) and [[20,15],[23,5810,1102,1104]] (array of entries).
    /// Returns null when the token cannot be parsed at all.
    /// </summary>
    public static List<Entry>? Parse(JToken? token)
    {
        var result = new List<Entry>();
        switch (token)
        {
            case null:
            case JValue { Type: JTokenType.Null }:
                return result;
            // captureregion.json uses "" for "no condition" (5810 Condition, 326 LightSwitchCondition).
            case JValue value when value.Type == JTokenType.String && string.IsNullOrWhiteSpace(value.Value<string>()):
                return result;
            case JObject obj:
                foreach (var property in obj.Properties())
                {
                    if (!int.TryParse(property.Name, out var type))
                        return null;
                    var args = ToArgs(property.Value);
                    if (args == null)
                        return null;
                    result.Add(new Entry(type, args));
                }

                return result;
            case JArray array:
                foreach (var item in array)
                {
                    if (item is JArray entry)
                    {
                        if (entry.Count == 0)
                            return null;
                        var args = ToArgs(entry);
                        if (args == null)
                            return null;
                        result.Add(new Entry((int)args[0], args[1..]));
                        continue;
                    }

                    if (item is JObject nested)
                    {
                        foreach (var property in nested.Properties())
                        {
                            if (!int.TryParse(property.Name, out var type))
                                return null;
                            var args = ToArgs(property.Value);
                            if (args == null)
                                return null;
                            result.Add(new Entry(type, args));
                        }

                        continue;
                    }

                    return null;
                }

                return result;
            default:
                return null;
        }
    }

    /// <summary>Evaluate all conditions; any unknown/unparsable entry denies access.</summary>
    public static bool Check(PlayerInstance player, JToken? token, string scope)
    {
        var entries = Parse(token);
        if (entries == null)
        {
            Logger.Error($"{scope}: unparsable condition payload '{token?.ToString(Newtonsoft.Json.Formatting.None)}'.");
            return false;
        }

        if (entries.Count == 0)
            return true;

        foreach (var entry in entries)
        {
            if (!CheckEntry(player, entry, scope))
                return false;
        }

        return true;
    }

    private static bool CheckEntry(PlayerInstance player, Entry entry, string scope)
    {
        switch (entry.Type)
        {
            case 1: // player level
            {
                if (entry.Args.Length < 1)
                    return Deny(scope, entry, "missing level argument");
                var required = ToUInt(entry.Args[0]);
                return player.Data.Level >= required || Deny(scope, entry, $"player level {player.Data.Level} < {required}");
            }
            case 2: // launch-pass progress: the argument is the tower level id
            {
                if (entry.Args.Length < 1)
                    return Deny(scope, entry, "missing pass level id");
                var passLevel = ToUInt(entry.Args[0]);
                var passed = player.Attributes.GetValue(AttrIds.Tower.PassGid, passLevel) > 0;
                return passed || Deny(scope, entry, $"tower level {passLevel} not passed");
            }
            case 20: // VirCapture persuade level (128,3)
            {
                if (entry.Args.Length < 1)
                    return Deny(scope, entry, "missing vircapture level");
                var required = ToUInt(entry.Args[0]);
                var current = player.Attributes.GetValue(AttrIds.VirCapture.Gid, AttrIds.VirCapture.CurrentLevelSid);
                return current >= required || Deny(scope, entry, $"vircapture level {current} < {required}");
            }
            case 23: // region capture: [[23,mapId,pointId,...]] -> point must be captured
            {
                if (entry.Args.Length < 2)
                    return Deny(scope, entry, "missing map/point id");
                var mapId = ToUInt(entry.Args[0]);
                var pointId = ToUInt(entry.Args[1]);
                var captured = VirCaptureStateHelper.GetPointState(player, mapId, pointId) > 0;
                if (entry.Args.Length > 2)
                    Logger.Warn($"{scope}: condition 23 ignores trailing args {string.Join(',', entry.Args[2..])} (client.lua does the same).");
                return captured || Deny(scope, entry, $"map {mapId} point {pointId} not captured");
            }
            default:
                if (AllowUnknown.Value)
                {
                    Logger.Warn($"{scope}: unknown condition type {entry.Type} skipped by test switch.");
                    return true;
                }

                Logger.Error($"{scope}: unknown condition type {entry.Type} (args {string.Join(',', entry.Args)}); denying.");
                return false;
        }
    }

    private static bool Deny(string scope, Entry entry, string reason)
    {
        Logger.Info($"{scope}: condition {entry.Type}({string.Join(',', entry.Args)}) failed: {reason}.");
        return false;
    }

    private static long[]? ToArgs(JToken? token)
    {
        if (token == null)
            return [];

        return token switch
        {
            JValue value => ToLong(value) is { } single ? [single] : null,
            JArray array => ToArgsArray(array),
            _ => null
        };
    }

    private static long[]? ToArgsArray(JArray array)
    {
        var args = new long[array.Count];
        for (var i = 0; i < array.Count; i++)
        {
            if (array[i] is not JValue value || ToLong(value) is not { } number)
                return null;
            args[i] = number;
        }

        return args;
    }

    private static long? ToLong(JValue value) => value.Type switch
    {
        JTokenType.Integer => value.Value<long>(),
        JTokenType.Float => (long)value.Value<double>(),
        JTokenType.String when long.TryParse(value.Value<string>(), out var parsed) => parsed,
        _ => null
    };

    private static uint ToUInt(long value) => value <= 0 ? 0u : (uint)Math.Min(value, uint.MaxValue);
}
