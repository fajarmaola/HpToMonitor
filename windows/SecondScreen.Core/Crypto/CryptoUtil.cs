using System.Security.Cryptography;
using System.Text;

namespace SecondScreen.Core;

// Cryptography per PROTOCOL.md §3.2:
//   - Ephemeral ECDH on P-256 (ECDiffieHellman / nistP256)
//   - HKDF-SHA256 with the 6-digit PIN as salt -> 32-byte AES-256 session key
//   - AES-256-GCM authenticated encryption for the secure control phase
// .NET's ECDiffieHellman and AesGcm are used directly (no third-party crypto).
public static class CryptoUtil
{
    // Create an ephemeral P-256 key pair. Returns the object (holds the private key)
    // and the SPKI (SubjectPublicKeyInfo) public key bytes to send on the wire.
    public static (ECDiffieHellman key, byte[] publicSpki) CreateEcdh()
    {
        var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        byte[] spki = ecdh.PublicKey.ExportSubjectPublicKeyInfo();
        return (ecdh, spki);
    }

    // Derive the raw ECDH shared secret (Z) with the peer's SPKI public key.
    public static byte[] DeriveSharedSecret(ECDiffieHellman myKey, byte[] peerSpki)
    {
        using var peer = ECDiffieHellman.Create();
        peer.ImportSubjectPublicKeyInfo(peerSpki, out _);
        // DeriveRawSecretAgreement returns Z (the x-coordinate) without an extra KDF,
        // so we can run our own HKDF that folds in the PIN.
        return myKey.DeriveRawSecretAgreement(peer.PublicKey);
    }

    // HKDF-SHA256(ikm=Z, salt=PIN ascii bytes, info) -> 32 bytes AES-256 key.
    public static byte[] DeriveSessionKey(byte[] sharedSecret, string pin)
    {
        byte[] salt = Encoding.ASCII.GetBytes(pin);
        byte[] info = Encoding.ASCII.GetBytes(Protocol.HkdfInfo);
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, Protocol.SessionKeyBytes, salt, info);
    }

    // For trusted-device reconnect (no PIN): salt is the fixed info, PIN omitted.
    public static byte[] DeriveTrustedKey(byte[] sharedSecret)
    {
        byte[] info = Encoding.ASCII.GetBytes(Protocol.HkdfInfo + "/trusted");
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, Protocol.SessionKeyBytes,
            salt: Array.Empty<byte>(), info);
    }

    public static string GeneratePin()
    {
        int max = (int)Math.Pow(10, Protocol.PinDigits);
        int value = RandomNumberGenerator.GetInt32(0, max);
        return value.ToString().PadLeft(Protocol.PinDigits, '0');
    }

    public static string RandomTokenHex(int bytes = 16)
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes));

    // AES-256-GCM. Output frame layout: [12-byte nonce][ciphertext][16-byte tag].
    public static byte[] Encrypt(byte[] key, byte[] plaintext)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(Protocol.GcmNonceBytes);
        byte[] cipher = new byte[plaintext.Length];
        byte[] tag = new byte[Protocol.GcmTagBytes];
        using var gcm = new AesGcm(key, Protocol.GcmTagBytes);
        gcm.Encrypt(nonce, plaintext, cipher, tag);
        byte[] outBuf = new byte[nonce.Length + cipher.Length + tag.Length];
        Buffer.BlockCopy(nonce, 0, outBuf, 0, nonce.Length);
        Buffer.BlockCopy(cipher, 0, outBuf, nonce.Length, cipher.Length);
        Buffer.BlockCopy(tag, 0, outBuf, nonce.Length + cipher.Length, tag.Length);
        return outBuf;
    }

    // Throws CryptographicException if authentication fails (wrong key / tampering).
    public static byte[] Decrypt(byte[] key, byte[] frame)
    {
        int n = Protocol.GcmNonceBytes, t = Protocol.GcmTagBytes;
        if (frame.Length < n + t) throw new CryptographicException("frame too short");
        var nonce = frame.AsSpan(0, n);
        var tag = frame.AsSpan(frame.Length - t, t);
        var cipher = frame.AsSpan(n, frame.Length - n - t);
        byte[] plain = new byte[cipher.Length];
        using var gcm = new AesGcm(key, t);
        gcm.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }

    // Deterministic per-packet nonce for video (PROTOCOL.md §5): frameId(4) | packetIndex(2) | zeros(6).
    public static byte[] VideoNonce(uint frameId, ushort packetIndex)
    {
        var nonce = new byte[Protocol.GcmNonceBytes];
        nonce[0] = (byte)(frameId >> 24); nonce[1] = (byte)(frameId >> 16);
        nonce[2] = (byte)(frameId >> 8);  nonce[3] = (byte)frameId;
        nonce[4] = (byte)(packetIndex >> 8); nonce[5] = (byte)packetIndex;
        return nonce;
    }

    public static byte[] EncryptWithNonce(byte[] key, byte[] nonce, byte[] plaintext)
    {
        byte[] cipher = new byte[plaintext.Length];
        byte[] tag = new byte[Protocol.GcmTagBytes];
        using var gcm = new AesGcm(key, Protocol.GcmTagBytes);
        gcm.Encrypt(nonce, plaintext, cipher, tag);
        byte[] outBuf = new byte[nonce.Length + cipher.Length + tag.Length];
        Buffer.BlockCopy(nonce, 0, outBuf, 0, nonce.Length);
        Buffer.BlockCopy(cipher, 0, outBuf, nonce.Length, cipher.Length);
        Buffer.BlockCopy(tag, 0, outBuf, nonce.Length + cipher.Length, tag.Length);
        return outBuf;
    }
}
