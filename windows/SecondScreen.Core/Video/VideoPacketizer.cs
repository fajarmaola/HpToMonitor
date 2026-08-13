using System.Buffers.Binary;

namespace SecondScreen.Core;

// Splits an encoded H.264 access unit into UDP packets per PROTOCOL.md §5, optionally
// AES-256-GCM-encrypting each payload. Header is 22 bytes, big-endian.
public sealed class VideoPacketizer
{
    private readonly byte[]? _key; // null => plaintext video

    public VideoPacketizer(byte[]? sessionKey) => _key = sessionKey;

    public IEnumerable<byte[]> Packetize(uint frameId, ulong captureTsUs, bool keyframe, ReadOnlyMemory<byte> frame)
    {
        int maxPayload = Protocol.VideoMaxPayload;
        int total = (frame.Length + maxPayload - 1) / maxPayload;
        if (total == 0) total = 1;

        for (int i = 0; i < total; i++)
        {
            int off = i * maxPayload;
            int len = Math.Min(maxPayload, frame.Length - off);
            ReadOnlyMemory<byte> chunk = frame.Slice(off, len);

            byte flags = 0;
            if (keyframe) flags |= Protocol.FlagKeyframe;
            if (i == total - 1) flags |= Protocol.FlagLastPacket;

            byte[] body;
            if (_key != null)
            {
                flags |= Protocol.FlagEncrypted;
                byte[] nonce = CryptoUtil.VideoNonce(frameId, (ushort)i);
                body = CryptoUtil.EncryptWithNonce(_key, nonce, chunk.ToArray()); // [nonce][ct][tag]
            }
            else
            {
                body = chunk.ToArray();
            }

            var packet = new byte[Protocol.VideoHeaderBytes + body.Length];
            packet[0] = Protocol.VideoMagic[0];
            packet[1] = Protocol.VideoMagic[1];
            packet[2] = (byte)Protocol.Version;
            packet[3] = flags;
            BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(4), frameId);
            BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(8), (ushort)i);
            BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(10), (ushort)total);
            BinaryPrimitives.WriteUInt64BigEndian(packet.AsSpan(12), captureTsUs);
            BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(20), (ushort)body.Length);
            Buffer.BlockCopy(body, 0, packet, Protocol.VideoHeaderBytes, body.Length);
            yield return packet;
        }
    }
}
