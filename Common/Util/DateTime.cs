
namespace MikuSB.Util;


public static class DateTimeExtensions
{
    private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static long ToUnixTimestampMilliseconds(this DateTime dateTime)
    {
        return (long)(dateTime - UnixEpoch).TotalMilliseconds;
    }

}
