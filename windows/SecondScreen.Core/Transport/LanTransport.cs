using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace SecondScreen.Core;

// LAN transport (fully implemented MVP path).
//   Control: TcpListener on 47801, accepts one Android client, 4-byte BE length-prefixed frames.
//   Video:   UdpClient sending packets to the peer's reported video endpoint.
public sealed class LanTransport : ITransport
{
    public string Name => "LAN";
    public bool IsConnected => _client?.Connected ?? false;
    public IPEndPoint? PeerAddress { get; private set; }

    public event EventHandler<byte[]>? ControlFrameReceived;
    public event EventHandler? PeerConnected;
    public event EventHandler? PeerDisconnected;

    private TcpListener? _listener;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private UdpClient? _videoUdp;
    private IPEndPoint? _peerVideoEp;
    private CancellationTokenSource? _cts;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public async Task StartAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _videoUdp = new UdpClient(AddressFamily.InterNetwork);
        _listener = new TcpListener(IPAddress.Any, Protocol.ControlTcpPort);
        _listener.Start();
        Log.Info($"LAN control listener on TCP {Protocol.ControlTcpPort}");

        _client = await _listener.AcceptTcpClientAsync(_cts.Token);
        _client.NoDelay = true; // low-latency control
        _stream = _client.GetStream();
        PeerAddress = _client.Client.RemoteEndPoint as IPEndPoint;
        // Default the video endpoint to the peer IP at the standard port until it tells us otherwise.
        if (PeerAddress != null)
            _peerVideoEp = new IPEndPoint(PeerAddress.Address, Protocol.VideoUdpPort);
        Log.Info($"LAN peer connected: {PeerAddress}");
        PeerConnected?.Invoke(this, EventArgs.Empty);

        _ = Task.Run(() => ReadLoop(_cts.Token));
    }

    private async Task ReadLoop(CancellationToken ct)
    {
        var lenBuf = new byte[4];
        try
        {
            while (!ct.IsCancellationRequested && _stream != null)
            {
                await ReadExact(_stream, lenBuf, 4, ct);
                int len = BinaryPrimitives.ReadInt32BigEndian(lenBuf);
                if (len <= 0 || len > 8 * 1024 * 1024) throw new InvalidDataException($"bad frame len {len}");
                var payload = new byte[len];
                await ReadExact(_stream, payload, len, ct);
                ControlFrameReceived?.Invoke(this, payload);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Warn($"LAN read loop ended: {ex.Message}"); }
        finally { PeerDisconnected?.Invoke(this, EventArgs.Empty); }
    }

    private static async Task ReadExact(NetworkStream s, byte[] buf, int count, CancellationToken ct)
    {
        int off = 0;
        while (off < count)
        {
            int n = await s.ReadAsync(buf.AsMemory(off, count - off), ct);
            if (n == 0) throw new IOException("peer closed");
            off += n;
        }
    }

    public async Task SendControlFrameAsync(ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        if (_stream == null) return;
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);
        await _writeLock.WaitAsync(ct);
        try
        {
            await _stream.WriteAsync(header, ct);
            await _stream.WriteAsync(payload, ct);
            await _stream.FlushAsync(ct);
        }
        finally { _writeLock.Release(); }
    }

    public void SendVideoPacket(ReadOnlySpan<byte> packet)
    {
        if (_videoUdp == null || _peerVideoEp == null) return;
        try { _videoUdp.Send(packet.ToArray(), packet.Length, _peerVideoEp); }
        catch (Exception ex) { Log.Debug($"video send error: {ex.Message}"); }
    }

    public void SetPeerVideoEndpoint(IPAddress address, int port)
        => _peerVideoEp = new IPEndPoint(address, port);

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        _stream?.Dispose();
        _client?.Dispose();
        _listener?.Stop();
        _videoUdp?.Dispose();
    }
}
