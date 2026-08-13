using System.Security.Cryptography;

namespace SecondScreen.Core;

// Host-side pairing state machine (PROTOCOL.md §3.2). Owns the ephemeral ECDH key, PIN and
// derived AES session key. Trusted devices (already in the store) skip the PIN.
public sealed class PairingService
{
    private readonly TrustedDeviceStore _store;
    private readonly ECDiffieHellman _myKey;
    private readonly byte[] _myPubSpki;

    public string? Pin { get; private set; }
    public byte[]? SessionKey { get; private set; }
    public bool IsTrustedReconnect { get; private set; }
    public string PeerPubB64 { get; private set; } = "";
    public string PeerName { get; private set; } = "";

    // Raised when a fresh PIN must be shown to the user (not for trusted reconnects).
    public event EventHandler<string>? PinGenerated;

    public PairingService(TrustedDeviceStore store)
    {
        _store = store;
        (_myKey, _myPubSpki) = CryptoUtil.CreateEcdh();
    }

    public string MyPublicKeyB64 => Convert.ToBase64String(_myPubSpki);

    // Process an incoming HELLO. Derives the session key and returns the HELLO_ACK to send.
    public HelloAckMessage HandleHello(HelloMessage hello, string hostName)
    {
        PeerPubB64 = hello.PubKey;
        PeerName = hello.Device.Name;
        byte[] peerSpki = Convert.FromBase64String(hello.PubKey);
        byte[] shared = CryptoUtil.DeriveSharedSecret(_myKey, peerSpki);

        bool trusted = _store.IsTrusted(hello.PubKey);
        IsTrustedReconnect = trusted;

        if (trusted)
        {
            // No PIN — derive the trusted-reconnect key.
            SessionKey = CryptoUtil.DeriveTrustedKey(shared);
            Log.Info($"Trusted device reconnect: {PeerName}");
        }
        else
        {
            Pin = CryptoUtil.GeneratePin();
            SessionKey = CryptoUtil.DeriveSessionKey(shared, Pin);
            Log.Info($"Pairing PIN generated for {PeerName}");
            PinGenerated?.Invoke(this, Pin);
        }

        return new HelloAckMessage
        {
            Host = new DeviceInfo { Name = hostName, Os = "Windows" },
            PubKey = MyPublicKeyB64,
            Trusted = trusted
        };
    }

    // Called after a PAIR_CONFIRM decrypted successfully with SessionKey => keys/PIN agree.
    public void ConfirmPaired()
    {
        _store.Trust(PeerPubB64, PeerName);
        Log.Info($"Device paired & trusted: {PeerName}");
    }

    // Returns true if this device is now locked out after too many failures.
    public bool RegisterFailure() => _store.RegisterFailureAndCheckLockout(PeerPubB64);
}
