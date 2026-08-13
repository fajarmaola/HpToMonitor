// SecondScreen Local — Wire Protocol v1 (C# mirror)
// Canonical spec: /shared/protocol/PROTOCOL.md. Keep the three language mirrors in sync.
// NOTE: namespace is SecondScreen.Core (not .Shared) so the C# Core project — which has
// ImplicitUsings and file-scoped `namespace SecondScreen.Core` everywhere — resolves these
// symbols without an extra using. The Desktop project's `using SecondScreen.Core;` also covers
// QualityMode/SessionState.
namespace SecondScreen.Core
{
    public static class Protocol
    {
        public const int Version = 1;

        // Ports (see PROTOCOL.md §1)
        public const int DiscoveryUdpPort = 47800;
        public const int ControlTcpPort   = 47801;
        public const int VideoUdpPort      = 47802;

        // Discovery payload types
        public const string DiscoverType = "SSL_DISCOVER";
        public const string AnnounceType = "SSL_ANNOUNCE";

        // Crypto parameters (PROTOCOL.md §3.2)
        public const string EcdhCurve   = "nistP256"; // P-256 / secp256r1
        public const string HkdfInfo    = "SecondScreenLocal/v1/session";
        public const int    SessionKeyBytes = 32;      // AES-256
        public const int    GcmNonceBytes   = 12;
        public const int    GcmTagBytes     = 16;
        public const int    PinDigits       = 6;
        public const int    MaxPairFailures = 5;

        // Timeouts / heartbeat
        public const int HeartbeatIntervalMs = 1000;
        public const int MissedPongsToDrop   = 3;
        public const int ReconnectMaxAttempts = 5;
        public const int ReconnectGraceMs     = 15000;

        // Video packet header (PROTOCOL.md §5)
        public static readonly byte[] VideoMagic = { 0x53, 0x56 }; // 'S','V'
        public const int VideoHeaderBytes = 22;
        public const int VideoMaxPayload  = 1200;
        // flags
        public const byte FlagKeyframe   = 0x01;
        public const byte FlagLastPacket = 0x02;
        public const byte FlagEncrypted  = 0x04;
    }

    // Control channel JSON "type" values (PROTOCOL.md §3)
    public static class MessageType
    {
        public const string Hello           = "HELLO";
        public const string HelloAck         = "HELLO_ACK";
        public const string PairConfirm      = "PAIR_CONFIRM";
        public const string PairOk           = "PAIR_OK";
        public const string PairFail         = "PAIR_FAIL";
        public const string SessionConfig    = "SESSION_CONFIG";
        public const string SessionConfigAck = "SESSION_CONFIG_ACK";
        public const string Ping             = "PING";
        public const string Pong             = "PONG";
        public const string Touch            = "TOUCH";
        public const string RequestKeyframe  = "REQUEST_KEYFRAME";
        public const string Stats            = "STATS";
        public const string DeviceUpdate     = "DEVICE_UPDATE";
        public const string SetQuality       = "SET_QUALITY";
        public const string Disconnect       = "DISCONNECT";
    }

    // Touch event kinds (PROTOCOL.md §4)
    public static class TouchEvent
    {
        public const string Down      = "DOWN";
        public const string Move      = "MOVE";
        public const string Up        = "UP";
        public const string Scroll    = "SCROLL";
        public const string LongPress = "LONG_PRESS";
    }

    public enum SessionState
    {
        Idle, Discovering, Connecting, Pairing, Configuring, Streaming, Reconnecting, Disconnected
    }

    public enum QualityMode { Performance, Balanced, HighQuality }
}
