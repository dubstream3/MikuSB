using Google.Protobuf;
using MikuSB.Enums.Packet;

namespace MikuSB.TcpSharp;

public class BasePacket
{
    public ushort CmdId { get; set; }
    public byte[] Body { get; set; }
    public ushort SeqNo { get; set; }
    public ushort PushSeq { get; set; }
    public long Timestamp { get; set; }
    public IMessage? Message { get; set; }
    public PacketFraming Framing { get; set; }
    public int UncompressedBodySize { get; set; }

    public BasePacket(ushort cmdId)
    {
        CmdId = cmdId;
        Body = Array.Empty<byte>();
        SeqNo = 0;
        PushSeq = 0;
        Timestamp = 0;
        Framing = PacketFraming.FourByteLittleEndianLength;
    }

    public BasePacket(ushort cmdId, byte[] body, PacketFraming framing = PacketFraming.FourByteLittleEndianLength)
    {
        CmdId = cmdId;
        Body = body ?? Array.Empty<byte>();
        Framing = framing;
        SeqNo = 0;
        PushSeq = 0;
        Timestamp = 0;
    }

    public void SetData(byte[] data)
    {
        Body = data;
    }

    public void SetData(IMessage message)
    {
        Body = message.ToByteArray();
        Message = message;
    }

    public void SetData(string base64)
    {
        SetData(Convert.FromBase64String(base64));
    }
}