using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SecondScreen.Core;

public sealed class DiscoveredPeer
{
    public string DeviceName { get; init; } = "";
    public IPAddress Address { get; init; } = IPAddress.None;
    public int ControlPort { get; init; } = Protocol.ControlTcpPort;
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
}

// Windows host side of discovery (PROTOCOL.md §2). Listens on UDP 47800 for Android
// SSL_DISCOVER probes and unicasts back an SSL_ANNOUNCE. Optionally also broadcasts
// announcements periodically so idle Android apps can list this PC.
public sealed class DiscoveryService : IDisposable
{
    private readonly string _hostName;
    private UdpClient? _udp;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private Task? _broadcastTask;

    public bool Paired { get; set; }

    public DiscoveryService(string hostName) => _hostName = hostName;

    public void Start(bool periodicBroadcast = true)
    {
        _cts = new CancellationTokenSource();
        _udp = new UdpClient(AddressFamily.InterNetwork);
        _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _udp.EnableBroadcast = true;
        _udp.Client.Bind(new IPEndPoint(IPAddress.Any, Protocol.DiscoveryUdpPort));
        _listenTask = Task.Run(() => ListenLoop(_cts.Token));
        if (periodicBroadcast)
            _broadcastTask = Task.Run(() => BroadcastLoop(_cts.Token));
        Log.Info($"Discovery listening on UDP {Protocol.DiscoveryUdpPort}");
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await _udp!.ReceiveAsync(ct);
                var text = Encoding.UTF8.GetString(result.Buffer);
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                if (root.TryGetProperty("t", out var t) && t.GetString() == Protocol.DiscoverType)
                {
                    var nonce = root.TryGetProperty("nonce", out var n) ? n.GetString() ?? "" : "";
                    await ReplyAnnounce(result.RemoteEndPoint, nonce, ct);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Log.Warn($"Discovery listen error: {ex.Message}"); }
        }
    }

    private async Task ReplyAnnounce(IPEndPoint to, string nonce, CancellationToken ct)
    {
        var payload = new
        {
            t = Protocol.AnnounceType,
            v = Protocol.Version,
            role = "host",
            name = _hostName,
            ip = LocalIp()?.ToString() ?? "",
            controlPort = Protocol.ControlTcpPort,
            paired = Paired,
            nonce
        };
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        await _udp!.SendAsync(bytes, bytes.Length, to);
    }

    private async Task BroadcastLoop(CancellationToken ct)
    {
        var ep = new IPEndPoint(IPAddress.Broadcast, Protocol.DiscoveryUdpPort);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var payload = new
                {
                    t = Protocol.AnnounceType, v = Protocol.Version, role = "host",
                    name = _hostName, ip = LocalIp()?.ToString() ?? "",
                    controlPort = Protocol.ControlTcpPort, paired = Paired, nonce = ""
                };
                byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
                await _udp!.SendAsync(bytes, bytes.Length, ep);
            }
            catch (Exception ex) { Log.Debug($"Broadcast error: {ex.Message}"); }
            await Task.Delay(2000, ct).ContinueWith(_ => { });
        }
    }

    public static IPAddress? LocalIp()
    {
        // Best-effort primary IPv4 by opening a dummy UDP socket to a private address.
        try
        {
            using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            s.Connect("192.168.0.1", 65530);
            return (s.LocalEndPoint as IPEndPoint)?.Address;
        }
        catch
        {
            return Dns.GetHostAddresses(Dns.GetHostName())
                      .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
        }
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        _udp?.Dispose();
    }
}
