using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace MikuSB.Configuration;

public class HotfixContainer
{
    public bool UseLocalCache { get; set; } = false;
    public Dictionary<string, HotfixManfiset> Hotfixes { get; set; } = new();
    public Dictionary<string, string> AesKeys { get; set; } = new ();

    public static string ExtractVersionNumber(string? version)
    {
        try
        {
            return version == null ? "" : version[..version.IndexOf('_')];
        }
        catch
        {
            return "";
        }
    }
}

public class HotfixManfiset
{
    [JsonPropertyName("Asb")] public AsbData Asb { get; set; } = new();
    [JsonPropertyName("AsbPreDownload")] public AsbPreDownloadData AsbPreDownload { get; set; } = new();
    [JsonPropertyName("Audio")] public AudioData Audio { get; set; } = new();
    [JsonPropertyName("AudioPreDownload")] public AudioPreDownloadData AudioPreDownload { get; set; } = new();
    [JsonPropertyName("VideoEncrypt")] public VideoEncryptData VideoEncrypt { get; set; } = new();
}

public class AsbData
{
    [JsonPropertyName("android")] public PlatformInfo Android { get; set; } = new();
    [JsonPropertyName("iphone")] public PlatformInfo Iphone { get; set; } = new();
    [JsonPropertyName("pc")] public PlatformInfo Pc { get; set; } = new();
}

public class AsbPreDownloadData
{
    [JsonPropertyName("android")] public PlatformEncryptedInfo Android { get; set; } = new();
    [JsonPropertyName("iphone")] public PlatformEncryptedInfo Iphone { get; set; } = new();
}

public class AudioData
{
    [JsonPropertyName("platform")] public Dictionary<string, string> Platform { get; set; } = new();
    [JsonPropertyName("revision")] public int Revision { get; set; }
}

public class AudioPreDownloadData
{
    [JsonPropertyName("enable_time")] public long EnableTime { get; set; }
    [JsonPropertyName("platform")] public Dictionary<string, string> Platform { get; set; } = new();
    [JsonPropertyName("revision")] public int Revision { get; set; }
}

public class VideoEncryptData
{
    [JsonPropertyName("filename")] public string FileName { get; set; } = "";
}

public class PlatformInfo
{
    [JsonPropertyName("enable_time")] public long EnableTime { get; set; }
    [JsonPropertyName("revision")] public string Revision { get; set; } = "";
    [JsonPropertyName("suffix")] public string Suffix { get; set; } = "";
}

public class PlatformEncryptedInfo : PlatformInfo
{
    [JsonPropertyName("encrypt_key")] public string EncryptKey { get; set; } = "";
}