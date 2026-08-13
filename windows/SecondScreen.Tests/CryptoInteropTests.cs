using System.Security.Cryptography;
using System.Text;
using SecondScreen.Core;
using Xunit;

namespace SecondScreen.Tests;

// Validates the C# crypto/protocol against shared/protocol/TEST_VECTORS.md. The same vectors
// are used by the Kotlin tests, proving byte-identical interop between Windows and Android.
public class CryptoInteropTests
{
    private static byte[] Ikm() => Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();

    private static byte[] Hex(string h) => Convert.FromHexString(h);

    [Fact]
    public void SessionKey_matches_vector()
    {
        byte[] key = CryptoUtil.DeriveSessionKey(Ikm(), "482771");
        Assert.Equal("34758394738a5f6a28968ed494eb58f8f041a289758e8f76b5bdf7c6810a96b3",
            Convert.ToHexString(key).ToLowerInvariant());
    }

    [Fact]
    public void TrustedKey_matches_vector()
    {
        byte[] key = CryptoUtil.DeriveTrustedKey(Ikm());
        Assert.Equal("c22fe8af2f93d94fb85d0f69d273a371c3e73a560fd0012c805513a1a8ddb42e",
            Convert.ToHexString(key).ToLowerInvariant());
    }

    [Fact]
    public void AesGcm_encrypt_with_fixed_nonce_matches_vector()
    {
        byte[] key = Hex("34758394738a5f6a28968ed494eb58f8f041a289758e8f76b5bdf7c6810a96b3");
        byte[] nonce = Hex("000000010000000000000000");
        byte[] pt = Encoding.ASCII.GetBytes("SecondScreen");
        byte[] wire = CryptoUtil.EncryptWithNonce(key, nonce, pt);
        Assert.Equal(
            "000000010000000000000000e61dab2cd74470940e519d5115653563c4630fa1106bd3ee2b688106",
            Convert.ToHexString(wire).ToLowerInvariant());
    }

    [Fact]
    public void AesGcm_roundtrip()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        byte[] pt = Encoding.UTF8.GetBytes("hello secondscreen 123");
        byte[] frame = CryptoUtil.Encrypt(key, pt);
        byte[] back = CryptoUtil.Decrypt(key, frame);
        Assert.Equal(pt, back);
    }

    [Fact]
    public void AesGcm_decrypt_wrong_key_throws()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        byte[] wrong = RandomNumberGenerator.GetBytes(32);
        byte[] frame = CryptoUtil.Encrypt(key, Encoding.UTF8.GetBytes("secret"));
        Assert.ThrowsAny<CryptographicException>(() => CryptoUtil.Decrypt(wrong, frame));
    }

    [Fact]
    public void Ecdh_both_sides_derive_same_session_key()
    {
        var (a, aPub) = CryptoUtil.CreateEcdh();
        var (b, bPub) = CryptoUtil.CreateEcdh();
        byte[] za = CryptoUtil.DeriveSharedSecret(a, bPub);
        byte[] zb = CryptoUtil.DeriveSharedSecret(b, aPub);
        Assert.Equal(za, zb); // ECDH agreement
        // Same PIN -> same session key on both sides.
        Assert.Equal(CryptoUtil.DeriveSessionKey(za, "123456"),
                     CryptoUtil.DeriveSessionKey(zb, "123456"));
        // Different PIN -> different key (PIN authentication property).
        Assert.NotEqual(CryptoUtil.DeriveSessionKey(za, "123456"),
                        CryptoUtil.DeriveSessionKey(zb, "654321"));
    }

    [Fact]
    public void VideoPacketizer_header_is_correct()
    {
        byte[] key = Hex("34758394738a5f6a28968ed494eb58f8f041a289758e8f76b5bdf7c6810a96b3");
        var pk = new VideoPacketizer(null); // plaintext
        byte[] frame = Enumerable.Range(0, 3000).Select(i => (byte)i).ToArray();
        var packets = pk.Packetize(frameId: 7, captureTsUs: 123456, keyframe: true, frame).ToList();

        // 3000 bytes / 1200 => 3 packets
        Assert.Equal(3, packets.Count);
        var p0 = packets[0];
        Assert.Equal(0x53, p0[0]);            // 'S'
        Assert.Equal(0x56, p0[1]);            // 'V'
        Assert.Equal(1, p0[2]);               // version
        Assert.True((p0[3] & Protocol.FlagKeyframe) != 0);
        // frameId big-endian at offset 4
        Assert.Equal(7u, (uint)((p0[4] << 24) | (p0[5] << 16) | (p0[6] << 8) | p0[7]));
        // packetCount big-endian at offset 10 == 3
        Assert.Equal(3, (p0[10] << 8) | p0[11]);
        // last packet has FlagLastPacket
        Assert.True((packets[2][3] & Protocol.FlagLastPacket) != 0);
    }
}
