using MikuSB.Proto;
using Newtonsoft.Json;
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MikuSB.Util.Extensions;

public static partial class Extensions
{
    #region Regex

    [GeneratedRegex(@"CN|OS|BETA|PROD|CECREATION|Android|Win|iOS")]
    public static partial Regex VersionRegex();

    [GeneratedRegex(@"(?<=Avatar_)(.*?)(?=_Config)")]
    public static partial Regex AvatarConfigRegex();

    [GeneratedRegex(@"(?<=Avatar_RogueBattleevent)(.*?)(?=_Config.json)")]
    public static partial Regex BattleEventDataRegex();

    [GeneratedRegex(@"coin(\d+)tier")]
    public static partial Regex ProductRegex();

    #endregion

    public static string GetCurrentLanguage()
    {
        var uiCulture = CultureInfo.CurrentUICulture;
        return uiCulture.Name switch
        {
            "zh-CN" => "CHS",
            "zh-TW" => "CHT",
            "ja-JP" => "JP",
            _ => "EN"
        };
    }

    public static List<string> GetSupportVersions()
    {
        var verList = new List<string>();
        if (GameConstants.GAME_VERSION[^1] == '5')
            for (var i = 1; i < 6; i++)
                verList.Add(GameConstants.GAME_VERSION + i.ToString());
        else
            verList.Add(GameConstants.GAME_VERSION);

        return verList;
    }

    public static T RandomElement<T>(this List<T> values)
    {
        var index = new Random().Next(values.Count);
        return values[index];
    }

    public static string RandomKey(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var random = new Random();
        return new string(Enumerable.Repeat(chars, length)
            .Select(s => s[random.Next(s.Length)]).ToArray());
    }

    public static ICollection<T> Clone<T>(this ICollection<T> values)
    {
        List<T> list = [.. values];

        return list;
    }

    public static int RandomInt(int from, int to)
    {
        return new Random().Next(from, to);
    }

    public static string GetSha256Hash(string input)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var builder = new StringBuilder();
        for (int i = 0; i < bytes.Length; i++) builder.Append(bytes[i].ToString("x2"));
        return builder.ToString();
    }

    public static void SafeAdd<T>(this List<T> list, T item)
    {
        if (!list.Contains(item)) list.Add(item);
    }

    public static void SafeAddRange<T>(this List<T> list, List<T> item)
    {
        foreach (var i in item) list.SafeAdd(i);
    }

    public static long GetUnixSec()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    public static long ToUnixSec(this DateTime dt)
    {
        return new DateTimeOffset(dt).ToUnixTimeSeconds();
    }

    public static long GetUnixMs()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    public static string ToArrayString<T>(this List<T> list)
    {
        return list.JoinFormat(", ", "");
    }

    public static string ToJsonString<TK, TV>(this Dictionary<TK, TV> dic) where TK : notnull
    {
        return JsonConvert.SerializeObject(dic);
    }

    public static byte[] StringToByteArray(string hex)
    {
        if (hex.Length % 2 == 1)
            throw new Exception("The binary key cannot have an odd number of digits");

        byte[] arr = new byte[hex.Length >> 1];

        for (int i = 0; i < hex.Length >> 1; ++i)
        {
            arr[i] = (byte)((GetHexVal(hex[i << 1]) << 4) + (GetHexVal(hex[(i << 1) + 1])));
        }

        return arr;
    }

    public static int GetHexVal(char hex)
    {
        int val = (int)hex;
        //For uppercase A-F letters:
        //return val - (val < 58 ? 48 : 55);
        //For lowercase a-f letters:
        //return val - (val < 58 ? 48 : 87);
        //Or the two combined, but a bit slower:
        return val - (val < 58 ? 48 : (val < 97 ? 55 : 87));
    }

    #region Kcp Utils

    public static string JoinFormat<T>(this IEnumerable<T> list, string separator,
        string formatString)
    {
        formatString = string.IsNullOrWhiteSpace(formatString) ? "{0}" : formatString;
        return string.Join(separator,
            list.Select(item => string.Format(formatString, item)));
    }

    public static void WriteConvID(this BinaryWriter bw, long convId)
    {
        //bw.Write(convId);
        bw.Write((int)(convId >> 32));
        bw.Write((int)(convId & 0xFFFFFFFF));
    }

    public static long GetNextAvailableIndex<T>(this SortedList<long, T> sortedList)
    {
        long key = 1;
        long count = sortedList.Count;
        long counter = 0;
        do
        {
            if (count == 0) break;
            var nextKeyInList = sortedList.Keys.ElementAt((Index)counter++);
            if (key != nextKeyInList) break;
            key = nextKeyInList + 1;
        } while (count != 1 && counter != count && key == sortedList.Keys.ElementAt((Index)counter));

        return key;
    }

    public static long AddNext<T>(this SortedList<long, T> sortedList, T item)
    {
        var key = sortedList.GetNextAvailableIndex();
        sortedList.Add(key, item);
        return key;
    }

    public static int ReadInt32BE(this BinaryReader br)
    {
        return BinaryPrimitives.ReadInt32BigEndian(br.ReadBytes(sizeof(int)));
    }

    public static uint ReadUInt32BE(this BinaryReader br)
    {
        return BinaryPrimitives.ReadUInt32BigEndian(br.ReadBytes(sizeof(uint)));
    }

    public static ushort ReadUInt16BE(this BinaryReader br)
    {
        return BinaryPrimitives.ReadUInt16BigEndian(br.ReadBytes(sizeof(ushort)));
    }

    public static void WriteUInt16BE(this BinaryWriter bw, ushort value)
    {
        Span<byte> data = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16BigEndian(data, value);
        bw.Write(data);
    }

    public static void WriteInt32BE(this BinaryWriter bw, int value)
    {
        Span<byte> data = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(data, value);
        bw.Write(data);
    }

    public static void WriteUInt32BE(this BinaryWriter bw, uint value)
    {
        Span<byte> data = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(data, value);
        bw.Write(data);
    }

    public static void WriteUInt64BE(this BinaryWriter bw, ulong value)
    {
        Span<byte> data = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64BigEndian(data, value);
        bw.Write(data);
    }

    #endregion
}