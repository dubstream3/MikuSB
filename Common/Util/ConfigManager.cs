using MikuSB.Configuration;
using MikuSB.Internationalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MikuSB.Util.Extensions;

namespace MikuSB.Util;

public static class ConfigManager
{
    public static readonly Logger Logger = new("ConfigManager");
    public static ConfigContainer Config { get; private set; } = new();
    private static readonly string ConfigFilePath = Config.Path.ConfigPath + "/Config.json";
    private static readonly string LocalConfigFilePath = Config.Path.ConfigPath + "/Config.Local.json";
    public static HotfixContainer Hotfix { get; private set; } = new();
    private static readonly string HotfixFilePath = Config.Path.ConfigPath + "/Hotfix.json";

    public static void LoadConfig()
    {
        LoadConfigData();
        //LoadHotfixData();
    }

    public static void SaveConfig()
    {
        SaveData(Config, ConfigFilePath);
    }

    private static void LoadConfigData()
    {
        var file = new FileInfo(ConfigFilePath);
        if (!file.Exists)
        {
            Config = new()
            {
                ServerOption =
                {
                    Language = Extensions.Extensions.GetCurrentLanguage()
                }
            };

            Logger.Info("Current Language is " + Config.ServerOption.Language);
            SaveData(Config, ConfigFilePath);
        }

        string json;
        using (var stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(stream))
            json = reader.ReadToEnd();

        var hasLocalOverrides = File.Exists(LocalConfigFilePath);
        var root = JObject.Parse(json);
        if (hasLocalOverrides)
        {
            // Config.Local.json (untracked) carries machine-specific values - LAN addresses, the game
            // path, debug switches - so the tracked Config.json can stay publishable.
            root.Merge(JObject.Parse(File.ReadAllText(LocalConfigFilePath)),
                new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace });
            Logger.Info($"Applied local overrides from {LocalConfigFilePath}");
        }

        Config = root.ToObject<ConfigContainer>()!;

        Config.Loader.Arguments = NormalizeLoaderArguments(Config.Loader.Arguments);
        // Never write merged local overrides back into the tracked Config.json.
        if (!hasLocalOverrides)
            SaveData(Config, ConfigFilePath);
    }

    private static string[] NormalizeLoaderArguments(string[]? arguments)
    {
        // Verified 2026-10-05: Game.exe hangs inside GameBase.dll early init (44 MB, ~54 modules,
        // no UE4 modules, no window, no log, exits after ~2.5 min) whenever -userdir points at a
        // path other than the game's own root. The official launcher passes
        // -userdir="<GameDir>" (see the working Game.log "LogInit: Command Line:").
        // An explicitly configured -userdir therefore wins; only add a fallback when it is absent.
        var result = new List<string>(arguments ?? []);
        var existingIndex = result.FindIndex(x => x.StartsWith("-userdir=", StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
            return result.ToArray();

        var userDataDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Client_User_Data"));
        Directory.CreateDirectory(userDataDirectory);
        result.Add($"-userdir={userDataDirectory}");

        return result.ToArray();
    }

    private static void LoadHotfixData()
    {
        var file = new FileInfo(HotfixFilePath);

        // Generate all necessary versions
        var verList = Extensions.Extensions.GetSupportVersions();

        Logger.Info(I18NManager.Translate("Server.ServerInfo.CurrentVersion",
            verList.Aggregate((current, next) => $"{current}, {next}")));

        if (!file.Exists)
        {
            Hotfix = new HotfixContainer();
            SaveData(Hotfix, HotfixFilePath);
            file.Refresh();
        }

        using (var stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(stream))
        {
            var json = reader.ReadToEnd();
            Hotfix = JsonConvert.DeserializeObject<HotfixContainer>(json)!;
        }

        foreach (var version in verList)
            if (!Hotfix.Hotfixes.TryGetValue(version, out var _))
                Hotfix.Hotfixes[version] = new();

        SaveData(Hotfix, HotfixFilePath);
    }

    private static void SaveData(object data, string path)
    {
        var json = JsonConvert.SerializeObject(data, Formatting.Indented);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        using var writer = new StreamWriter(stream);
        writer.Write(json);
    }

    public static void InitDirectories()
    {
        foreach (var property in Config.Path.GetType().GetProperties())
        {
            var dir = property.GetValue(Config.Path)?.ToString();

            if (!string.IsNullOrEmpty(dir))
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
        }
    }
}