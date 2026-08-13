using System.Net;

namespace SecondScreen.Core;

// Wi-Fi Direct transport — Phase 4. Interface present now for forward-compatibility.
//
// Design (documented for the future implementation):
//   Use the Windows.Devices.WiFiDirect WinRT APIs to create/advertise a P2P group. Crucially,
//   Wi-Fi Direct on Windows uses a *separate* virtual adapter, so the PC keeps its normal
//   internet Wi-Fi connection alive (a hard requirement in the problem statement). Once the
//   p2p group is up, both sides get link-local IPv4s on the p2p interface and we reuse the
//   exact same TCP(control)/UDP(video) logic as LanTransport, just bound to that interface.
//
// TODO(hardware): requires a real Wi-Fi Direct capable adapter + driver and cannot be built
// or validated in the authoring container. When implemented, bind sockets to the p2p adapter
// address and delegate to the shared framing/packetization code.
public sealed class WifiDirectTransport : ITransport
{
    public string Name => "Wi-Fi Direct";
    public bool IsConnected => false;
    public IPEndPoint? PeerAddress => null;

    public event EventHandler<byte[]>? ControlFrameReceived;
    public event EventHandler? PeerConnected;
    public event EventHandler? PeerDisconnected;

    public Task StartAsync(CancellationToken ct)
        => throw new NotImplementedException(
            "Wi-Fi Direct transport is Phase 4. Use Windows.Devices.WiFiDirect to form a P2P " +
            "group on a separate virtual adapter (keeps internet Wi-Fi active), then reuse the " +
            "LAN TCP/UDP path bound to the p2p interface.");

    public Task SendControlFrameAsync(ReadOnlyMemory<byte> payload, CancellationToken ct)
        => throw new NotImplementedException("Wi-Fi Direct transport is Phase 4.");

    public void SendVideoPacket(ReadOnlySpan<byte> packet)
        => throw new NotImplementedException("Wi-Fi Direct transport is Phase 4.");

    public void SetPeerVideoEndpoint(IPAddress address, int port) { }

    public void Dispose() { }
}
