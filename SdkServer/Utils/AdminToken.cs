using System.Security.Cryptography;
using MikuSB.Util;

namespace MikuSB.SdkServer.Utils;

/// <summary>
/// Bearer token for the admin panel. Generated once (32 random bytes, hex) and written back into
/// Config.json so the operator can copy it into the browser; it is printed to the server log exactly once,
/// on the run that created it. The listener is loopback-only already -- the token is there so that another
/// local process, or anything reaching 127.0.0.1 through a proxy, cannot drive the server with a plain HTTP
/// call.
/// </summary>
public static class AdminToken
{
    private static readonly Logger Logger = new("AdminPanel");
    private static string? _cached;

    public static string Current
    {
        get
        {
            if (_cached != null) return _cached;

            var configured = ConfigManager.Config.HttpServer.AdminToken;
            if (string.IsNullOrWhiteSpace(configured))
            {
                configured = RandomNumberGenerator.GetHexString(32);
                ConfigManager.Config.HttpServer.AdminToken = configured;
                ConfigManager.SaveConfig();
                Logger.Warn($"Admin panel token generated; stored in Config.json as HttpServer.AdminToken: {configured}");
            }

            _cached = configured.Trim();
            return _cached;
        }
    }

    public static bool IsValid(string? candidate)
    {
        if (string.IsNullOrEmpty(candidate)) return false;

        var expected = System.Text.Encoding.UTF8.GetBytes(Current);
        var actual = System.Text.Encoding.UTF8.GetBytes(candidate);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
