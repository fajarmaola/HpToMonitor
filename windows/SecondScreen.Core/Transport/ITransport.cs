using System.Net;

namespace SecondScreen.Core;

// Transport abstraction (ARCHITECTURE.md). The session/video/input layers only ever talk to
// ITransport, so adding USB or Wi-Fi Direct later requires no changes above this line.
//
// Two logical channels are exposed:
//   * Control  — reliable, ordered, framed (TCP for LAN). Used for pairing, config,
//                heartbeat, touch, stats. Frames are opaque byte[] payloads (already
//                encrypted by the session layer once the secure phase starts).
//   * Video    — best-effort datagrams (UDP for LAN). One-directional host -> device.
//
// This is the WINDOWS HOST perspective: it accepts one Android peer, receives control
// frames from it, and pushes video packets to it.
public interface ITransport : IDisposable
{
    string Name { get; }
    bool IsConnected { get; }
    IPEndPoint? PeerAddress { get; }

    // Begin listening/accepting (LAN: TCP listener + UDP video socket).
    Task StartAsync(CancellationToken ct);

    // Send one control frame (length-prefixing handled by the transport).
    Task SendControlFrameAsync(ReadOnlyMemory<byte> payload, CancellationToken ct);

    // Fire-and-forget video datagram (already fully packetized per PROTOCOL.md §5).
    void SendVideoPacket(ReadOnlySpan<byte> packet);

    // Called after the peer reports the UDP port it listens on for video.
    void SetPeerVideoEndpoint(IPAddress address, int port);

    event EventHandler<byte[]>? ControlFrameReceived; // one reassembled control payload
    event EventHandler? PeerConnected;
    event EventHandler? PeerDisconnected;
}
