using MikuSB.Util;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

/// <summary>
/// Tolerant readers for the house mini-game tables the server keeps in its resource tree
/// (ConfigManager.Config.Path.ResourcePath).
///
/// The coin pusher ladder is already modelled server side as
/// Common/Data/Excel/CoinPusherRewardExcel.cs, declared as
/// [ResourceEntity("house/coinpusher/coinpusher_rewards.json")] — that proves both the directory
/// layout (the client CSV path with the extension swapped) and the column names (Level/Exp/Reward)
/// for the sibling house tables. The pub (酒馆/GuessPoker) and throwing (冰趣蜜弹/Throw) tables are
/// NOT modelled in Common, and Common is outside this module's write scope, so they are read here
/// with the same tolerant numeric handling the Excel classes use: a single empty string in one row
/// must not swallow the rest of the file (docs/contracts/README.md §6).
///
/// Client origins of every table name below:
///   * house/GuessPoker/GuessPokerRewards.json — House/HousePub.lua:35-45 (Level/Exp/Reward ladder)
///   * house/GuessPoker/GuessPokerMode.json    — House/HousePub.lua:49-65 (ModeType/PlayerNum/RankExp/TrialExp)
///   * house/GuessPoker/GuessPokerAchv.json    — House/HousePub.lua:68-78 (achievement id list)
///   * house/Throw/ThrowAward.json             — HouseMiniGame/HouseThrow/HouseThrowConfig.lua:224-237
///   * house/Throw/ThrowAchiev.json            — HouseMiniGame/HouseThrow/HouseThrowConfig.lua:239-252
///
/// A missing or malformed file only disables the feature that reads it (logged once per file);
/// nothing here throws into the request path.
/// </summary>
internal static class HouseMiniGameTables
{
    internal const string PubRewardTable = "house/GuessPoker/GuessPokerRewards.json";
    internal const string PubModeTable = "house/GuessPoker/GuessPokerMode.json";
    internal const string PubAchvTable = "house/GuessPoker/GuessPokerAchv.json";
    internal const string ThrowRewardTable = "house/Throw/ThrowAward.json";
    internal const string ThrowAchvTable = "house/Throw/ThrowAchiev.json";

    internal const string PubBlindTable = "house/GuessPoker/GuessPokerBlind.json";

    private static readonly ConcurrentDictionary<string, IReadOnlyList<JObject>> RowCache = new();
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<uint, HouseLevelRow>> LadderCache = new();
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<uint, uint>> BlindIndexCache = new();
    private static readonly ConcurrentDictionary<string, byte> Reported = new();

    /// <summary>All rows of a house table, or an empty list when the file is unavailable.</summary>
    internal static IReadOnlyList<JObject> Rows(string relativePath) => RowCache.GetOrAdd(relativePath, LoadRows);

    /// <summary>Level ladder (1-based keys) of a Level/Exp/Reward table.</summary>
    internal static IReadOnlyDictionary<uint, HouseLevelRow> LevelLadder(string relativePath)
        => LadderCache.GetOrAdd(relativePath, BuildLadder);

    /// <summary>The ID column of an achievement list table (GuessPokerAchv / ThrowAchiev).</summary>
    internal static List<uint> Ids(string relativePath)
    {
        var ids = new List<uint>();
        foreach (var row in Rows(relativePath))
        {
            var id = ReadUint(row["ID"]);
            if (id == 0)
                id = ReadUint(row["Id"]);
            if (id != 0)
                ids.Add(id);
        }

        return ids;
    }

    /// <summary>
    /// GuessPokerMode row for a (mode type, player count) pair, i.e. the client's
    /// tbModeConfigByKey (HousePub.lua:47-65, key = string.format('%d-%d', ModeType, PlayerNum)).
    /// </summary>
    internal static JObject? PubModeRow(uint modeType, uint playerNum)
    {
        foreach (var row in Rows(PubModeTable))
        {
            if (ReadUint(row["ModeType"]) == modeType && ReadUint(row["PlayerNum"]) == playerNum)
                return row;
        }

        return null;
    }

    /// <summary>
    /// Blind-box album index map: GuessPokerConfig.LoadBlindConfig (GuessPokerConfig.lua:168-192) sets
    /// tb.nIndex = i, the 1-based ordinal of the row among rows whose BlindID > 0, and that ordinal is
    /// what HousePub:GetBlindRikiUnlock (:124-138) tests as a bit of pub offset 4 (index &lt;= 30) or
    /// offset 5 (index &gt; 30). Rows are enumerated in file order, so the JSON dump keeps the CSV
    /// ordinal (JObject preserves document order in Newtonsoft.Json).
    /// </summary>
    internal static IReadOnlyDictionary<uint, uint> BlindIndexMap(string relativePath)
        => BlindIndexCache.GetOrAdd(relativePath, BuildBlindIndexMap);

    private static IReadOnlyDictionary<uint, uint> BuildBlindIndexMap(string relativePath)
    {
        var map = new Dictionary<uint, uint>();
        var index = 0u;
        foreach (var row in Rows(relativePath))
        {
            var id = ReadUint(row["BlindID"]);
            if (id == 0)
                id = ReadUint(row["ID"]);
            if (id == 0)
                continue;

            map[id] = ++index;
        }

        return map;
    }

    /// <summary>
    /// Flat number list, used for the Eval()'d table columns (e.g. GuessPokerMode RankExp/TrialExp,
    /// loaded by HousePub.lua:57-58 as Eval(tbLine.RankExp)). Nested braces are flattened in order.
    /// </summary>
    internal static List<uint> ReadUintList(JToken? token)
    {
        var values = new List<uint>();
        if (token == null || token.Type == JTokenType.Null)
            return values;

        if (token is JArray array && array.All(entry => entry is JValue))
        {
            foreach (var entry in array)
            {
                var raw = entry.ToString().Trim().Trim('"');
                if (uint.TryParse(raw, out var value))
                    values.Add(value);
            }

            return values;
        }

        var digits = new System.Text.StringBuilder();
        foreach (var ch in token.ToString())
        {
            if (char.IsDigit(ch))
            {
                digits.Append(ch);
                continue;
            }

            if (digits.Length == 0)
                continue;

            if (uint.TryParse(digits.ToString(), out var number))
                values.Add(number);
            digits.Clear();
        }

        if (digits.Length > 0 && uint.TryParse(digits.ToString(), out var last))
            values.Add(last);

        return values;
    }

    private static IReadOnlyList<JObject> LoadRows(string relativePath)
    {
        var path = Path.Combine(ConfigManager.Config.Path.ResourcePath, relativePath);
        if (!File.Exists(path))
        {
            WarnOnce(relativePath, $"House table {relativePath} not found at {path}; the feature reading it stays disabled.");
            return [];
        }

        try
        {
            var rows = new List<JObject>();
            switch (JToken.Parse(File.ReadAllText(path)))
            {
                case JArray array:
                    foreach (var entry in array)
                        if (entry is JObject row)
                            rows.Add(row);
                    break;
                case JObject obj:
                    foreach (var property in obj.Properties())
                        if (property.Value is JObject row)
                            rows.Add(row);
                    break;
            }

            return rows;
        }
        catch (Exception ex)
        {
            WarnOnce(relativePath, $"Could not read house table {relativePath}: {ex.Message}");
            return [];
        }
    }

    private static IReadOnlyDictionary<uint, HouseLevelRow> BuildLadder(string relativePath)
    {
        var ladder = new Dictionary<uint, HouseLevelRow>();
        foreach (var row in Rows(relativePath))
        {
            var level = ReadUint(row["Level"]);
            if (level == 0)
                level = ReadUint(row["ID"]);
            if (level == 0)
                continue;

            ladder[level] = new HouseLevelRow
            {
                Level = level,
                Exp = ReadUint(row["Exp"]),
                Rewards = ReadRewardRows(row["Reward"])
            };
        }

        return ladder;
    }

    private static void WarnOnce(string key, string message)
    {
        if (Reported.TryAdd(key, 0))
            Logger.GetByClassName().Warn(message);
    }

    /// <summary>
    /// Tolerant number read, mirroring Common/Data/Excel/CoinPusherRewardExcel.cs: the shipped
    /// tables mix plain numbers with quoted strings ("1") and empty cells.
    /// </summary>
    internal static uint ReadUint(JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null)
            return 0;

        var raw = token.ToString().Trim().Trim('[', ']', '"');
        return uint.TryParse(raw, out var value) ? value : 0;
    }

    /// <summary>
    /// Reward column as [ [G,D,P,L,Count], ... ]. Three shapes occur in the shipped tables:
    /// a nested array of rows (DLC tables), a FLAT five-number array (coinpusher_rewards.json), and
    /// the raw Lua literal the client evaluates with Eval() ("{{1,1001,1,1}}"). A row is kept only
    /// when it has five numbers and a non-zero count, which is exactly what
    /// RewardManager.GrantConfiguredRewardsAsync (:47-63) can consume.
    /// </summary>
    internal static List<List<uint>> ReadRewardRows(JToken? token)
    {
        var rows = new List<List<uint>>();
        switch (token)
        {
            case null:
            case { Type: JTokenType.Null }:
                return rows;
            case JArray { Count: >= 5 } flat when flat.All(entry => entry is JValue):
                AddRow(flat, rows);
                return rows;
            case JArray array:
                foreach (var entry in array)
                {
                    if (entry is JArray row)
                        AddRow(row, rows);
                    else if (entry.Type == JTokenType.String)
                        AddLiteralRows(entry.ToString(), rows);
                }

                return rows;
            case JValue value:
                AddLiteralRows(value.ToString(), rows);
                return rows;
            default:
                return rows;
        }
    }

    private static void AddRow(JArray row, List<List<uint>> rows)
    {
        var parsed = new List<uint>(5);
        for (var i = 0; i < 5; i++)
        {
            var raw = row[i]?.ToString().Trim().Trim('[', ']', '"') ?? string.Empty;
            if (!uint.TryParse(raw, out var value))
                return;

            parsed.Add(value);
        }

        // Same acceptance rule as the reward tables the server already grants (row[0] != 0 for the
        // reward kind, row[4] != 0 for the count).
        if (parsed[0] != 0 && parsed[4] > 0)
            rows.Add(parsed);
    }

    /// <summary>Parses a Lua table literal / comma list such as "{1,1001,1,1}" or "{{1,1001,1,1}}".</summary>
    private static void AddLiteralRows(string raw, List<List<uint>> rows)
    {
        var row = new List<uint>();
        var digits = new System.Text.StringBuilder();

        void FlushNumber()
        {
            if (digits.Length == 0)
                return;
            if (uint.TryParse(digits.ToString(), out var value))
                row.Add(value);
            digits.Clear();
        }

        void FlushRow()
        {
            FlushNumber();
            if (row.Count >= 5)
                AddRow(new JArray(row.Select(number => (JToken)number).ToArray()), rows);
            row = new List<uint>();
        }

        foreach (var ch in raw)
        {
            if (char.IsDigit(ch))
            {
                digits.Append(ch);
                continue;
            }

            switch (ch)
            {
                case ',':
                    FlushNumber();
                    break;
                case '}':
                case ']':
                    FlushRow();
                    break;
                case '{':
                case '[':
                    FlushNumber();
                    break;
            }
        }

        FlushRow();
    }
}

/// <summary>One row of a house level-reward ladder.</summary>
internal sealed class HouseLevelRow
{
    internal uint Level { get; init; }

    /// <summary>Experience needed to leave this level; 0 on the max level (client reads it the same way).</summary>
    internal uint Exp { get; init; }

    internal List<List<uint>> Rewards { get; init; } = [];
}
