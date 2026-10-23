namespace MikuSB.Util;

public static class Guid64
{
    public static ulong NewGuid64()
    {
        byte[] guidBytes = Guid.NewGuid().ToByteArray();
        return (ulong)BitConverter.ToUInt32(guidBytes, 0);
    }
}

