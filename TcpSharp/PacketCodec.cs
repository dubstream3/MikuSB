using Google.Protobuf;
using MikuSB.Enums.Packet;
using MikuSB.Util;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Net.Sockets;

namespace MikuSB.TcpSharp
{
    public class PacketCodec
    {
        private const int HeaderSize4Byte = 4;
        private const int HeaderSize2Byte = 2;
        private const int MaxPacketLength = 1024 * 1024;
        private const ushort ClientMagic = 0x011F;
        private const int ControlPacketSize = 35;

        private static readonly Logger Logger = new("PacketCodec");

        public PacketCodec()
        {

        }

        public async Task<BasePacket?> ReadPacketAsync(
            Stream stream,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var lengthBuffer = new byte[HeaderSize4Byte];
                if (!await ReadExactAsync(stream, lengthBuffer, cancellationToken))
                {
                    Logger.Debug("Connection closed before packet header");
                    return null;
                }

                var framing = DetectFraming(lengthBuffer);

                switch (framing)
                {
                    case PacketFraming.Control:
                        return await HandleControlPacket(stream, cancellationToken);

                    case PacketFraming.TwoByteBigEndianLength:
                        return await HandleTwoBytePacket(stream, lengthBuffer, cancellationToken);

                    case PacketFraming.FourByteLittleEndianLength:
                        return await HandleFourBytePacket(stream, lengthBuffer, cancellationToken);

                    default:
                        return await HandleUnknownPacket(stream, lengthBuffer, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                Logger.Debug("Packet read cancelled");
                return null;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error reading packet {ex}");
                return null;
            }
        }

        public byte[] Encode(ushort packetId, byte[] payload, PacketFraming framing = PacketFraming.FourByteLittleEndianLength, int uncompressedSize = 0)
        {
            return framing switch
            {
                PacketFraming.TwoByteBigEndianLength => EncodeTwoByteFrame(packetId, payload, uncompressedSize),
                PacketFraming.FourByteLittleEndianLength => EncodeFourByteFrame(packetId, payload),
                _ => EncodeFourByteFrame(packetId, payload)
            };
        }

        public byte[] EncodeRaw(ushort packetId, byte[] payload, PacketFraming framing = PacketFraming.FourByteLittleEndianLength)
        {
            return framing switch
            {
                PacketFraming.TwoByteBigEndianLength => EncodeTwoByteFrame(packetId, payload),
                PacketFraming.FourByteLittleEndianLength => EncodeFourByteFrame(packetId, payload),
                _ => EncodeFourByteFrame(packetId, payload)
            };
        }

        #region Private Methods

        private PacketFraming DetectFraming(byte[] header)
        {
            var firstTwoBytes = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(0, 2));
            var nextTwoBytes = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(2, 2));

            if (firstTwoBytes == ClientMagic && nextTwoBytes == 0)
                return PacketFraming.Control;

            if (firstTwoBytes == ClientMagic && IsValidPacketId(nextTwoBytes))
                return PacketFraming.TwoByteBigEndianLength;

            if (IsValidTwoByteHeader(firstTwoBytes, (ushort)nextTwoBytes))
                return PacketFraming.TwoByteBigEndianLength;

            return PacketFraming.FourByteLittleEndianLength;
        }

        private async Task<BasePacket?> HandleControlPacket(Stream stream, CancellationToken cancellationToken)
        {
            var controlData = new byte[ControlPacketSize];
            if (!await ReadExactAsync(stream, controlData, cancellationToken))
            {
                Logger.Debug("Connection closed during control packet read");
                return null;
            }

            Logger.Debug("Control packet received");
            return new BasePacket(0)
            {
                Framing = PacketFraming.Control,
                Body = Array.Empty<byte>()
            };
        }

        private async Task<BasePacket?> HandleTwoBytePacket(
            Stream stream,
            byte[] header,
            CancellationToken cancellationToken)
        {
            var packetId = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(2, 2));

            var wrapper = new byte[ControlPacketSize];
            if (!await ReadExactAsync(stream, wrapper, cancellationToken))
            {
                Logger.Debug($"Connection closed during wrapper read for packet {packetId}");
                return null;
            }

            var payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(wrapper.AsSpan(6, 2));
            var payload = await ReadPayloadAsync(stream, payloadLength, cancellationToken);

            if (payload == null)
                return null;

            //Logger.Debug($"Packet received (2-byte framing): ID={packetId}, PayloadSize={payload.Length}");

            return new BasePacket(packetId)
            {
                Framing = PacketFraming.TwoByteBigEndianLength,
                Body = payload
            };
        }

        private async Task<BasePacket?> HandleFourBytePacket(
            Stream stream,
            byte[] header,
            CancellationToken cancellationToken)
        {
            var length = BinaryPrimitives.ReadUInt32LittleEndian(header);

            if (length < 2 || length > MaxPacketLength)
            {
                Logger.Warn($"Invalid packet length: {length}");
                return null;
            }

            var frame = new byte[length];
            if (!await ReadExactAsync(stream, frame, cancellationToken))
            {
                Logger.Debug("Connection closed during packet body read");
                return null;
            }

            var packetId = BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(0, 2));
            var payload = frame[2..];

            //Logger.Debug($"Packet received (4-byte framing): ID={packetId}, PayloadSize={payload.Length}");

            return new BasePacket(packetId)
            {
                Framing = PacketFraming.FourByteLittleEndianLength,
                Body = payload
            };
        }

        private async Task<BasePacket?> HandleUnknownPacket(
            Stream stream,
            byte[] header,
            CancellationToken cancellationToken)
        {
            var extraData = await ReadAvailableBytesAsync(stream, cancellationToken);
            var combinedData = new byte[header.Length + extraData.Length];
            header.CopyTo(combinedData, 0);
            extraData.CopyTo(combinedData, header.Length);

            Logger.Warn($"Unknown packet format detected, captured {combinedData.Length} bytes");

            return new BasePacket(0)
            {
                Framing = PacketFraming.Unknown,
                Body = combinedData
            };
        }

        private async Task<byte[]?> ReadPayloadAsync(
            Stream stream,
            int length,
            CancellationToken cancellationToken)
        {
            if (length <= 0)
                return Array.Empty<byte>();

            if (length > MaxPacketLength)
            {
                Logger.Warn($"Payload too large: {length}");
                return null;
            }

            var payload = new byte[length];
            if (!await ReadExactAsync(stream, payload, cancellationToken))
                return null;

            return payload;
        }

        private const int CompressionThreshold = 60000;

        private static byte[] ZlibCompress(byte[] data)
        {
            using var ms = new MemoryStream();
            using (var zlib = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
                zlib.Write(data, 0, data.Length);
            return ms.ToArray();
        }

        private byte[] EncodeTwoByteFrame(ushort packetId, byte[] payload, int uncompressedSize = 0)
        {
            if (payload.Length > CompressionThreshold)
                payload = ZlibCompress(payload);
            var wrappedPayload = WrapPayload(payload);
            var buffer = new byte[HeaderSize4Byte + wrappedPayload.Length];

            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(0, 2), ClientMagic);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(2, 2), packetId);
            wrappedPayload.CopyTo(buffer.AsSpan(HeaderSize4Byte));

            return buffer;
        }

        private byte[] EncodeFourByteFrame(ushort packetId, byte[] payload)
        {
            var buffer = new byte[HeaderSize4Byte + HeaderSize2Byte + payload.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0, 4), (uint)(HeaderSize2Byte + payload.Length));
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4, 2), packetId);
            payload.CopyTo(buffer.AsSpan(HeaderSize4Byte + HeaderSize2Byte));
            return buffer;
        }

        private byte[] WrapPayload(byte[] payload)
        {
            const int wrapperHeaderSize = 35;
            var wrapped = new byte[wrapperHeaderSize + payload.Length];
            BinaryPrimitives.WriteUInt16LittleEndian(wrapped.AsSpan(6, 2), (ushort)payload.Length);
            if (payload.Length >= 2 && payload[0] == 0x78 &&
                (payload[1] == 0x01 || payload[1] == 0x5E || payload[1] == 0x9C || payload[1] == 0xDA))
                wrapped[10] = 2;
            payload.CopyTo(wrapped.AsSpan(wrapperHeaderSize));

            return wrapped;
        }

        private static async Task<bool> ReadExactAsync(
            Stream stream,
            byte[] buffer,
            CancellationToken cancellationToken)
        {
            var offset = 0;
            while (offset < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
                if (read == 0)
                    return false;

                offset += read;
            }
            return true;
        }

        private static async Task<byte[]> ReadAvailableBytesAsync(
            Stream stream,
            CancellationToken cancellationToken)
        {
            if (stream is not NetworkStream networkStream || !networkStream.DataAvailable)
                return Array.Empty<byte>();

            using var ms = new MemoryStream();
            var buffer = new byte[4096];

            while (networkStream.DataAvailable && ms.Length < 16384)
            {
                var read = await networkStream.ReadAsync(buffer, cancellationToken);
                if (read <= 0)
                    break;

                ms.Write(buffer, 0, read);
            }

            return ms.ToArray();
        }

        private static bool IsValidTwoByteHeader(int firstTwoBytes, ushort packetId)
        {
            return firstTwoBytes >= 2
                   && firstTwoBytes <= ushort.MaxValue
                   && IsValidPacketId(packetId);
        }

        private static bool IsValidPacketId(ushort packetId)
        {
            return packetId != 0;
        }

        #endregion
    }
}