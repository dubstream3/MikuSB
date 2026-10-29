using System.Globalization;
using System.Text.Json;
using MikuSB.Data;
using MikuSB.Util;

namespace MikuSB.SdkServer.Utils;

/// <summary>One outfit a banner handed out: which character, which skin, and the activity row behind it.</summary>
public sealed record ReplayCharacter(
    string Key,
    uint Number,
    string Variant,
    string Name,
    string Skin,
    uint ActivityId);

/// <summary>One DLC era, i.e. one card the operator can click.</summary>
public sealed record ReplayEra(
    uint Id,
    string Name,
    string MainUi,
    uint Icon,
    string IconPath,
    string Preview,
    string StartTime,
    string EndTime,
    string EnterStartTime,
    string CloseEndTime,
    long StartUnix,
    long EndUnix,
    int ActivityCount,
    /// <summary>True when an offline-exported key art exists at <c>ui/replay/dlc-{Id}.png</c>.</summary>
    bool HasImage,
    /// <summary>
    /// True for the eras whose content was dropped from the resource packs: the card stays on the page for
    /// reference but neither the command nor the panel will serve them.
    /// </summary>
    bool Retired,
    IReadOnlyList<ReplayCharacter> Characters);

/// <summary>Everything the replay page needs, plus the era the server is actually serving.</summary>
public sealed record ReplaySnapshot(
    bool Available,
    string Root,
    string Lang,
    IReadOnlyList<string> Languages,
    ServedDlcInfo Current,
    IReadOnlyList<ReplayEra> Eras);

/// <summary>
/// Read-only window over the separately versioned resource repository (<c>Config.Path.ResourcePath</c>).
///
/// The rows deliberately stay in that repository instead of being copied into the server's Excel models:
/// the replay page has to show whatever the resource repo currently ships (it is updated on its own
/// schedule), and duplicating 455 activity rows + 24 eras into C# types would only create a second source
/// of truth that drifts. Parsing is defensive - a malformed field degrades that one card, never the page.
///
/// Names come from <c>all_language/ui</c> (the only multi-locale copy of the UI strings), the era title from
/// <c>TxtDLC{id}.BtnStory</c> (older eras only ship <c>TxtDLC{id}.Title</c>), and the cast of an era from
/// <c>dlc.TryID -> activities.GroupId -> gacha_try.ActId -> try_girl.Girl[1]</c>, where <c>Girl[1]</c> is the
/// character number (the <c>girl&lt;nn&gt;</c> key in card.json) and <c>Girl[2]</c> the outfit variant (2=a, 3=b,
/// 4=c, matching the <c>Girl005c</c> spine folder).
/// </summary>
public static class ReplayCatalog
{
    /// <summary>Locale columns present in every <c>all_language/ui</c> row, in column order.</summary>
    public static readonly string[] Locales =
    [
        "zh_CN", "zh_TW", "ja_JP", "ko_KR", "en_US", "fr_FR", "es_ES", "de_DE", "th_TH", "id_ID"
    ];

    private static readonly object Gate = new();
    private static readonly JsonDocumentOptions Options = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    private static readonly Dictionary<string, ReplaySnapshot> Cache = new(StringComparer.Ordinal);
    private static string _stamp = "";

    private static readonly string[] WatchedFiles =
    [
        "dlc/dlc_activities.json",
        "activity/activities.json",
        "activity/gacha_try/gacha_try.json",
        "activity/gacha_try/try_girl.json",
        "resource/resource.json",
        "all_language/ui/ui.json",
        "all_language/ui/activity.json",
        "all_language/ui/card.json",
        "all_language/ui/plotrolename.json",
        "all_language/ui/supplies.json",
        "all_language/ui/riki.json"
    ];

    public static ReplaySnapshot Load(string? lang = null)
    {
        var root = Path.GetFullPath(ConfigManager.Config.Path.ResourcePath);
        var stamp = string.Join('|', WatchedFiles.Select(file => Stamp(root, file))) + "|" + StampImages(root);
        var locale = Normalize(lang);

        lock (Gate)
        {
            // The served era changes without any watched file changing, so refresh it on every hit.
            if (stamp != _stamp)
            {
                Cache.Clear();
                _stamp = stamp;
            }

            if (Cache.TryGetValue(locale, out var cached))
                return cached with { Current = ReadServedDlc() };

            var built = Build(root, locale);
            Cache[locale] = built;
            return built;
        }
    }

    /// <summary>Unknown or missing locales fall back to Simplified Chinese, which every table is keyed by.</summary>
    private static string Normalize(string? lang)
    {
        if (string.IsNullOrWhiteSpace(lang))
            return Locales[0];

        var wanted = lang.Trim();
        foreach (var locale in Locales)
            if (string.Equals(locale, wanted, StringComparison.OrdinalIgnoreCase))
                return locale;

        // "zh", "zh-cn", "japanese" style inputs.
        foreach (var locale in Locales)
            if (wanted.StartsWith(locale[..2], StringComparison.OrdinalIgnoreCase))
                return locale;

        return Locales[0];
    }

    private static string Stamp(string root, string relative)
    {
        var info = new FileInfo(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        return info.Exists ? $"{info.LastWriteTimeUtc.Ticks}:{info.Length}" : "missing";
    }

    /// <summary>
    /// The exported card art (ui/replay/dlc-*.png) lives outside the watched tables, so fold
    /// its count and newest timestamp into the stamp as well: re-running
    /// scripts/export_era_images.py then refreshes <c>HasImage</c> without a restart.
    /// </summary>
    private static string StampImages(string root)
    {
        try
        {
            var dir = Path.Combine(root, "ui", "replay");
            if (!Directory.Exists(dir))
                return "none";

            var files = new DirectoryInfo(dir).GetFiles("dlc-*.png");
            var newest = files.Length == 0 ? 0 : files.Max(file => file.LastWriteTimeUtc.Ticks);
            return $"{files.Length}:{newest}";
        }
        catch
        {
            return "error";
        }
    }

    private static ReplaySnapshot Build(string root, string lang)
    {
        var uiStrings = LoadLocalized(Path.Combine(root, "all_language", "ui", "ui.json"), ["TxtDLC"]);
        var skinStrings = LoadLocalized(Path.Combine(root, "all_language", "ui", "activity.json"), ["TxtActivity"]);
        var characterStrings = LoadLocalized(Path.Combine(root, "all_language", "ui", "card.json"), ["girl"]);
        var rosterStrings = LoadLocalized(Path.Combine(root, "all_language", "ui", "plotrolename.json"), ["Chara."]);
        // Era 23 ships no TxtDLC23.* key at all, but its reward token is still called
        // "漠北寻风的奖励代币", which is the only place the era title survives.
        var supplyStrings = LoadLocalized(Path.Combine(root, "all_language", "ui", "supplies.json"), ["dlc"]);
        var cgStrings = LoadLocalized(Path.Combine(root, "all_language", "ui", "riki.json"), ["CG_DLC"]);
        var suitsIndex = BuildSuitsIndex(characterStrings, lang);
        var fallbackNames = LoadLanguage(Path.Combine(root, "language", "activity.json"));
        var resources = LoadResourcePaths(Path.Combine(root, "resource", "resource.json"));
        var activities = LoadActivities(Path.Combine(root, "activity", "activities.json"));
        var tryGirls = LoadTryGirls(Path.Combine(root, "activity", "gacha_try", "try_girl.json"));
        var gachaTry = LoadGachaTry(Path.Combine(root, "activity", "gacha_try", "gacha_try.json"));

        var byGroup = new Dictionary<uint, List<uint>>();
        var className = new Dictionary<uint, string>();
        var activityNames = new Dictionary<uint, string>();
        foreach (var (id, group, title, kind) in activities)
        {
            if (!byGroup.TryGetValue(group, out var bucket))
                byGroup[group] = bucket = [];
            bucket.Add(id);
            className[id] = kind;
            activityNames[id] = title;
        }

        // Pass 1: collect the raw era rows, keeping every trial activity together with its
        // TxtActivityTry1_NN number.
        var eraRows = new List<(uint Id, string MainUi, uint Icon, string Preview, string Start, string End,
            string Enter, string Close, List<uint> Members, List<(uint ActivityId, int Number)> Trials)>();
        var dlcPath = Path.Combine(root, "dlc", "dlc_activities.json");
        if (File.Exists(dlcPath))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllBytes(dlcPath), Options);
                foreach (var row in document.RootElement.EnumerateArray())
                {
                    var id = ReadUInt(row, "ID");
                    if (id == 0)
                        continue;

                    var memberIds = new List<uint>();
                    foreach (var group in ReadNumbers(row, "TryID"))
                        if (byGroup.TryGetValue(group, out var bucket))
                            memberIds.AddRange(bucket);

                    var distinct = memberIds.Distinct().ToList();
                    var trials = new List<(uint ActivityId, int Number)>();
                    foreach (var activityId in distinct)
                    {
                        var number = TrialNumber(activityNames.TryGetValue(activityId, out var title) ? title : "");
                        if (number > 0)
                            trials.Add((activityId, number));
                    }

                    eraRows.Add((id, ReadString(row, "MainUI"), ReadUInt(row, "Icon"), ReadString(row, "Preview"),
                        ReadString(row, "StartTime"), ReadString(row, "EndTime"), ReadString(row, "EnterStartTime"),
                        ReadString(row, "CloseEndTime"), distinct, trials));
                }
            }
            catch (JsonException)
            {
                // Reached only when the resource repo is mid-write; keep the last good snapshot instead.
                return Cache.TryGetValue(lang, out var previous)
                    ? previous
                    : new ReplaySnapshot(false, root, lang, Locales, ReadServedDlc(), []);
            }
        }

        // Pass 2: the trial titles number up once per release (`activity.TxtActivityTry1_33`), so an era's own
        // cast is the trials numbered above everything released before it. Rerun banners (`TxtActivityTry2_*`)
        // and the standing rotation (low numbers, re-listed every version) drop out by construction.
        var eras = new List<ReplayEra>();
        var released = 0;
        foreach (var row in eraRows.OrderBy(item => item.Id))
        {
            var premiere = row.Trials.Where(trial => trial.Number > released)
                .OrderBy(trial => trial.Number)
                .ToList();

            // Gallery titles are the authoritative "who ships with this era" list: they name the outfit
            // (「胧嫣·瑞狐」) and therefore also cover the character that is handed out instead of rolled, which
            // the trial chain cannot see. Eras from before the gallery picked up the outfit convention fall back
            // to the trial numbering, which is all those versions ever shipped.
            var characters = CgCast(row.Id, cgStrings, suitsIndex, characterStrings, rosterStrings, lang);
            if (characters.Count == 0)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var (activityId, _) in premiere)
                {
                    if (!gachaTry.TryGetValue(activityId, out var girlIds))
                        continue;

                    foreach (var girlId in girlIds)
                    {
                        if (!tryGirls.TryGetValue(girlId, out var girl))
                            continue;

                        if (!seen.Add($"{girl.Number}:{girl.Variant}"))
                            continue;

                        characters.Add(new ReplayCharacter(
                            $"girl{girl.Number:000}",
                            girl.Number,
                            girl.Variant,
                            CharacterName(characterStrings, rosterStrings, girl.Number, lang),
                            ResolveSkin(activityId, activityNames, skinStrings, fallbackNames, lang),
                            activityId));
                    }
                }
            }

            // Some base outfits are literally named after the character (era 6: 伊切尔·伊切尔), which reads like a
            // bug on the card, so blank the duplicate and let the page label it as the base outfit.
            for (var index = 0; index < characters.Count; index++)
            {
                var character = characters[index];
                if (character.Skin.Length > 0 && string.Equals(character.Skin, character.Name, StringComparison.Ordinal))
                    characters[index] = character with { Skin = "" };
            }

            characters.Sort((left, right) =>
            {
                var byNumber = left.Number.CompareTo(right.Number);
                return byNumber != 0 ? byNumber : string.CompareOrdinal(left.Variant, right.Variant);
            });

            eras.Add(new ReplayEra(
                row.Id,
                EraName(row.Id, uiStrings, supplyStrings, lang),
                row.MainUi,
                row.Icon,
                resources.TryGetValue(row.Icon, out var path) ? path : "",
                row.Preview,
                row.Start,
                row.End,
                row.Enter,
                row.Close,
                ToUnix(row.Start),
                ToUnix(row.End),
                row.Members.Count,
                ImageFile(root, row.Id) != null,
                DlcEraPolicy.IsRetired(row.Id),
                characters));

            if (row.Trials.Count > 0)
                released = Math.Max(released, row.Trials.Max(trial => trial.Number));
        }

        eras.Sort((left, right) => left.Id.CompareTo(right.Id));
        return new ReplaySnapshot(eras.Count > 0, root, lang, Locales, ReadServedDlc(), eras);
    }

    /// <summary>
    /// Cast of an era taken from its gallery entries (<c>CG_DLC{client}_name{n}</c>).
    /// </summary>
    /// <remarks>
    /// Every era from 9 on has one gallery tile per shipped outfit, titled "<c>角色·皮肤</c>", so the tiles are
    /// the only table that lists a version's characters *and* their outfits in one place — including the one
    /// that is given away rather than drawn from a banner (era 36: 胧嫣·瑞狐 handed out, 芙提雅·炬芯 in the pool).
    /// The tile numbering is the client's own DLC number, which only differs from the era id for era 23
    /// (client DLC12, 漠北寻风).
    /// </remarks>
    private static List<ReplayCharacter> CgCast(uint eraId, Dictionary<string, string[]> cgStrings,
        Dictionary<string, (uint Number, int Variant)> suits, Dictionary<string, string[]> cards,
        Dictionary<string, string[]> roster, string lang)
    {
        var result = new List<ReplayCharacter>();
        var client = eraId == 23 ? 12u : eraId;
        var prefix = $"CG_DLC{client}_name";
        var keys = cgStrings.Keys
            .Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(key => int.TryParse(key.AsSpan(prefix.Length), out var index) ? index : int.MaxValue)
            .ToList();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            var title = Text(cgStrings, key, lang);
            if (title.Length == 0)
                continue;

            var separator = title.IndexOfAny(['·', '-']);
            if (separator <= 0)
                continue;

            var character = title[..separator].Trim();
            var skin = title[(separator + 1)..].Trim();
            if (skin.Length == 0 || character.Length == 0)
                continue;

            if (!suits.TryGetValue($"{character}-{skin}", out var match))
                continue;

            if (!seen.Add($"{match.Number}:{match.Variant}"))
                continue;

            result.Add(new ReplayCharacter(
                $"girl{match.Number:000}",
                match.Number,
                VariantLetter(match.Variant),
                CharacterName(cards, roster, match.Number, lang),
                skin,
                0));
        }

        return result;
    }

    /// <summary>card.json outfit index: "<c>角色-皮肤</c>" -&gt; character number and card variant.</summary>
    private static Dictionary<string, (uint Number, int Variant)> BuildSuitsIndex(Dictionary<string, string[]> cards,
        string lang)
    {
        var index = new Dictionary<string, (uint, int)>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in cards)
        {
            var key = pair.Key;
            if (!key.StartsWith("girl", StringComparison.OrdinalIgnoreCase) ||
                !key.EndsWith("_suits", StringComparison.OrdinalIgnoreCase))
                continue;

            var digits = key[4..^"_suits".Length];
            if (digits.Length != 3 ||
                !uint.TryParse(digits[..2], out var number) ||
                !int.TryParse(digits[2..], out var variant))
                continue;

            var text = Text(cards, key, lang);
            if (!text.Contains('-') && !text.Contains('·'))
                continue;

            index.TryAdd(NormalizeSuit(text), (number, variant));
        }

        return index;
    }

    /// <summary>"角色·皮肤" / "角色 - 皮肤" both collapse to "<c>角色-皮肤</c>" so the two tables can be joined.</summary>
    private static string NormalizeSuit(string text)
    {
        var parts = text.Replace('·', '-')
            .Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join('-', parts);
    }

    /// <summary>Card variants start at the base outfit (1) and then letter like the trial rows: 2 -&gt; "a".</summary>
    private static string VariantLetter(int variant) =>
        variant <= 2 ? "a" : ((char)('a' + variant - 2)).ToString();

    /// <summary>
    /// Trial banners are titled <c>activity.TxtActivityTry1_33</c>; the number counts up once per release, which
    /// is the only field in the shipped tables that separates a version's own cast from its reruns.
    /// </summary>
    private static int TrialNumber(string titleKey)
    {
        const string Prefix = "activity.TxtActivityTry1_";
        if (!titleKey.StartsWith(Prefix, StringComparison.Ordinal))
            return 0;

        return int.TryParse(titleKey.AsSpan(Prefix.Length), out var number) ? number : 0;
    }

    /// <summary>
    /// Era titles: 7..36 ship <c>TxtDLC{id}.BtnStory</c>; 2/4/5/6 only ship <c>TxtDLC{id}.Title</c>; 1 and 3 use
    /// the older <c>TxtDLC{id}Name</c> spelling (era 3 even ships <c>TxtDlc3Name</c>, hence the case-insensitive
    /// table); era 23 has no title key at all and stays empty so the page can print the bare era number.
    /// </summary>
    private static string EraName(uint id, Dictionary<string, string[]> table, Dictionary<string, string[]> supplies, string lang)
    {
        var story = Text(table, $"TxtDLC{id}.BtnStory", lang);
        if (story.Length > 0)
            return story;

        var title = Text(table, $"TxtDLC{id}.Title", lang);
        if (title.Length > 0)
            return title;

        var legacy = Text(table, $"TxtDLC{id}Name", lang);
        if (legacy.Length > 0)
            return legacy;

        // Era 23 has no title key anywhere; its shop token is "<name>的奖励代币".
        return StripTokenSuffix(Text(supplies, $"dlc{id}_01_use", lang));
    }

    private static string StripTokenSuffix(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        var trimmed = text.Trim();
        foreach (var suffix in new[] { "的奖励代币", "奖励代币", " Reward Token", " reward token" })
        {
            if (trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return trimmed[..^suffix.Length].Trim();
        }

        return trimmed;
    }

    /// <summary>Absolute path of the offline-exported key art for an era, or null when it was never exported.</summary>
    public static string? ImageFile(uint id)
    {
        var root = Path.GetFullPath(ConfigManager.Config.Path.ResourcePath);
        return ImageFile(root, id);
    }

    private static string? ImageFile(string root, uint id)
    {
        var file = Path.Combine(root, "ui", "replay", $"dlc-{id}.png");
        return File.Exists(file) ? file : null;
    }

    private static string ResolveSkin(
        uint activityId,
        Dictionary<uint, string> activityNames,
        Dictionary<string, string[]> skinStrings,
        Dictionary<string, string> fallbackNames,
        string lang)
    {
        if (!activityNames.TryGetValue(activityId, out var titleKey) || titleKey.Length == 0)
            return "";

        var key = titleKey.StartsWith("activity.", StringComparison.OrdinalIgnoreCase)
            ? titleKey["activity.".Length..]
            : titleKey;

        var localized = Text(skinStrings, key, lang);
        if (localized.Length > 0)
            return localized;

        return fallbackNames.TryGetValue(key, out var name) ? name : "";
    }

    private static ServedDlcInfo ReadServedDlc()
    {
        var read = InGameConsoleBridge.GetServedDlc;
        if (read == null)
            return new ServedDlcInfo(0, false, false);
        try
        {
            return read();
        }
        catch (Exception)
        {
            // The bridge runs inside the game-server assembly; a throw there must not take the panel down.
            return new ServedDlcInfo(0, false, false);
        }
    }

    /// <summary>
    /// Multi-locale table keyed by row <c>Key</c>, holding one column per known locale. Only keys whose prefix
    /// is requested are kept: ui.json is 3.8 MB and the page needs a few hundred rows of it.
    /// </summary>
    private static Dictionary<string, string[]> LoadLocalized(string path, string[] prefixes)
    {
        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
            return result;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path), Options);
            foreach (var row in document.RootElement.EnumerateArray())
            {
                var key = ReadString(row, "Key");
                if (key.Length == 0)
                    continue;

                var wanted = false;
                foreach (var prefix in prefixes)
                    if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        wanted = true;
                        break;
                    }

                if (!wanted)
                    continue;

                var values = new string[Locales.Length];
                for (var index = 0; index < Locales.Length; index++)
                    values[index] = ReadString(row, Locales[index]);

                result[key] = values;
            }
        }
        catch (JsonException)
        {
            // A broken language table only costs us the display names.
        }

        return result;
    }

    /// <summary>Picks one locale column, falling back to zh_CN and then en_US before giving up.</summary>
    private static string Text(Dictionary<string, string[]> table, string key, string lang)
    {
        if (!table.TryGetValue(key, out var values))
            return "";

        var index = Array.IndexOf(Locales, lang);
        if (index >= 0 && values[index].Length > 0)
            return values[index];

        var simplified = Array.IndexOf(Locales, "zh_CN");
        if (simplified >= 0 && values[simplified].Length > 0)
            return values[simplified];

        var english = Array.IndexOf(Locales, "en_US");
        return english >= 0 ? values[english] : "";
    }

    /// <summary>
    /// Playable character name for a character number.
    /// </summary>
    /// <remarks>
    /// <c>card.json</c> is keyed <c>girl&lt;nn&gt;&lt;v&gt;</c>: <c>nn</c> is the character number and <c>v</c> the card
    /// variant, so every variant repeats the same playable name (girl011/012/013 are all 安卡希雅).
    /// <c>plotrolename.json</c> (<c>Chara.NN</c>) looks like the same numbering but is the *story roster*: from
    /// Chara.15 onwards it drifts from the character numbers (Chara.15 = 卡罗琳 while girl015 = 凯茜娅),
    /// so it is only a fallback for numbers that never got a card.
    /// </remarks>
    private static string CharacterName(Dictionary<string, string[]> cards, Dictionary<string, string[]> roster,
        uint number, string lang)
    {
        for (var variant = 1; variant <= 9; variant++)
        {
            var name = Text(cards, $"girl{number:00}{variant}", lang);
            if (name.Length > 0 && !name.StartsWith("girl", StringComparison.OrdinalIgnoreCase))
                return name;
        }

        var rosterName = Text(roster, $"Chara.{number:00}", lang);
        return rosterName.Length > 0 && !rosterName.StartsWith("Chara.", StringComparison.OrdinalIgnoreCase)
            ? rosterName
            : $"#{number}";
    }

    private static Dictionary<string, string> LoadLanguage(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path))
            return result;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path), Options);
            foreach (var row in document.RootElement.EnumerateArray())
            {
                var key = ReadString(row, "Key");
                var value = ReadString(row, "zh_CN");
                if (key.Length > 0 && value.Length > 0)
                    result[key] = value;
            }
        }
        catch (JsonException)
        {
            // A broken language table only costs us the display names.
        }

        return result;
    }

    private static Dictionary<uint, string> LoadResourcePaths(string path)
    {
        var result = new Dictionary<uint, string>();
        if (!File.Exists(path))
            return result;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path), Options);
            foreach (var row in document.RootElement.EnumerateArray())
            {
                var id = ReadUInt(row, "ID");
                if (id != 0)
                    result.TryAdd(id, ReadString(row, "path"));
            }
        }
        catch (JsonException)
        {
            // Icons are cosmetic: without the map the cards simply have no icon path.
        }

        return result;
    }

    /// <summary>Activity rows reduced to what the era join needs: id, group, class and the title key.</summary>
    private static List<(uint Id, uint GroupId, string TitleKey, string Class)> LoadActivities(string path)
    {
        var result = new List<(uint, uint, string, string)>();
        if (!File.Exists(path))
            return result;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path), Options);
            foreach (var row in document.RootElement.EnumerateArray())
            {
                var id = ReadUInt(row, "Id");
                if (id != 0)
                    result.Add((id, ReadUInt(row, "GroupId"), ReadString(row, "TitleDes"), ReadString(row, "Class")));
            }
        }
        catch (JsonException)
        {
            // Same contract as above: no names is worse than no page, but it still renders.
        }

        return result;
    }

    /// <summary>gacha_try rows: which try-girl rows a banner row (activity id) shows.</summary>
    private static Dictionary<uint, List<uint>> LoadGachaTry(string path)
    {
        var result = new Dictionary<uint, List<uint>>();
        if (!File.Exists(path))
            return result;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path), Options);
            foreach (var row in document.RootElement.EnumerateArray())
            {
                var actId = ReadUInt(row, "ActId");
                if (actId == 0)
                    continue;

                var ids = ReadNumbers(row, "TryGirl").Distinct().ToList();
                if (ids.Count > 0)
                    result[actId] = ids;
            }
        }
        catch (JsonException)
        {
            // Without this join the cards still switch era, they just list no cast.
        }

        return result;
    }

    /// <summary>try_girl rows: character number plus outfit variant, e.g. <c>[1,5,4,1]</c> -&gt; char 5, variant "c".</summary>
    private static Dictionary<uint, (uint Number, string Variant)> LoadTryGirls(string path)
    {
        var result = new Dictionary<uint, (uint, string)>();
        if (!File.Exists(path))
            return result;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path), Options);
            foreach (var row in document.RootElement.EnumerateArray())
            {
                var id = ReadUInt(row, "ID");
                if (id == 0 || !row.TryGetProperty("Girl", out var girl) || girl.ValueKind != JsonValueKind.Array)
                    continue;

                var values = girl.EnumerateArray().Select(ToUint).ToArray();
                if (values.Length < 2 || values[1] == 0)
                    continue;

                var variantIndex = values.Length > 2 && values[2] >= 2 ? values[2] : 2;
                var variant = ((char)('a' + variantIndex - 2)).ToString();
                result.TryAdd(id, (values[1], variant));
            }
        }
        catch (JsonException)
        {
            // Degrades to "no cast", same as above.
        }

        return result;
    }

    private static string ReadString(JsonElement row, string name)
    {
        if (!row.TryGetProperty(name, out var value))
            return "";
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim() ?? "",
            JsonValueKind.Number => value.GetRawText(),
            _ => ""
        };
    }

    private static uint ReadUInt(JsonElement row, string name)
    {
        if (!row.TryGetProperty(name, out var value))
            return 0;

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetUInt32(out var number) ? number : 0,
            JsonValueKind.String => ToUint(value),
            _ => 0
        };
    }

    private static IEnumerable<uint> ReadNumbers(JsonElement row, string name)
    {
        if (!row.TryGetProperty(name, out var value))
            yield break;

        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                var number = ToUint(item);
                if (number != 0)
                    yield return number;
            }

            yield break;
        }

        // Some rows write a single group as "561" instead of [561].
        var single = ReadUInt(row, name);
        if (single != 0)
            yield return single;
    }

    private static uint ToUint(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number)
            return value.TryGetUInt32(out var numeric) ? numeric : 0;
        if (value.ValueKind == JsonValueKind.String)
            return uint.TryParse(value.GetString()?.Trim().Trim('[', ']', '"'), out var parsed) ? parsed : 0;
        return 0;
    }

    /// <summary>
    /// The tables store "yyyyMMddHHmm" wrapped in brackets (e.g. "[202607150400]"), in the server's local
    /// zone (Asia/Shanghai). Unparseable values collapse to 0 so the UI can print the raw string instead.
    /// </summary>
    private static long ToUnix(string raw)
    {
        var text = raw.Trim().Trim('[', ']');
        if (text.Length < 12 || !DateTime.TryParseExact(text[..12], "yyyyMMddHHmm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var local))
            return 0;

        return new DateTimeOffset(local, TimeSpan.FromHours(8)).ToUnixTimeMilliseconds() / 1000;
    }
}
