using System.Net;

namespace SecondScreen.Core;

// USB transport — Phase 3 (see MVP PRIORITY in the problem statement). The interface is in
// place now so the video/session/input layers never change when USB lands.
//
// Design (documented for the future implementation):
//   Android is a USB device; the PC is the host. The lowest-friction approach that needs no
//   custom kernel USB driver is to reuse ADB's port forwarding:
//     * `adb reverse tcp:47801 tcp:47801`  (Android connects to PC control server via USB)
//     * `adb forward tcp:47802 tcp:47802`  (video tunnelled over the USB TCP channel)
//   giving ~USB latency without raw AOA. A raw AOA (Android Open Accessory) or MTP-free bulk
//   endpoint path can be added for even lower overhead.
//
// TODO(hardware): requires a physical USB connection + adb on the PC and USB-debugging on the
// phone. Cannot be implemented/verified in the authoring container. When implemented, this
// class shells out to `adb` to set up the tunnels, then delegates to the same TCP/UDP logic
// as LanTransport over 127.0.0.1.
public sealed class UsbTransport : ITransport
{
    public string Name => "USB";
    public bool IsConnected => false;
    public IPEndPoint? PeerAddress => null;

    public event EventHandler<byte[]>? ControlFrameReceived;
    public event EventHandler? PeerConnected;
    public event EventHandler? PeerDisconnected;

    public Task StartAsync(CancellationToken ct)
        => throw new NotImplementedException(
            "USB transport is Phase 3. Implement adb reverse/forward tunnels, then reuse the " +
            "LAN TCP/UDP path over loopback. See class doc + docs/ARCHITECTURE.md.");

    public Task SendControlFrameAsync(ReadOnlyMemory<byte> payload, CancellationToken ct)
        => throw new NotImplementedException("USB transport is Phase 3.");

    public void SendVideoPacket(ReadOnlySpan<byte> packet)
        => throw new NotImplementedException("USB transport is Phase 3.");

    public void SetPeerVideoEndpoint(IPAddress address, int port) { }

    public void Dispose() { }
}
