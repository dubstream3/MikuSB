using System.Security.Cryptography;
using System.Text;

namespace MikuSB.Util.Security;

public class Crypto
{
    private static readonly Random SecureRandom = new();

    // Simple way to create a unique session key
    public static string CreateSessionKey(string accountUid)
    {
        var random = new byte[64];
        SecureRandom.NextBytes(random);

        var temp = accountUid + "." + DateTime.Now.Ticks + "." + SecureRandom;

        try
        {
            var bytes = SHA512.HashData(Encoding.UTF8.GetBytes(temp));
            return Convert.ToBase64String(bytes);
        }
        catch
        {
            var bytes = SHA512.HashData(Encoding.UTF8.GetBytes(temp));
            return Convert.ToBase64String(bytes);
        }
    }
}