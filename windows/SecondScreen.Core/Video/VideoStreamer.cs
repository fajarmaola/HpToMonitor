using System.Runtime.InteropServices;

namespace SecondScreen.Core;

public sealed class VideoStreamConfig
{
    public int OutputIndex { get; set; }        // which display to capture
    public int Width { get; set; } = 1080;
    public int Height { get; set; } = 2400;
    public int Fps { get; set; } = 60;
    public int BitrateKbps { get; set; } = 12000;
    public bool UseHardware { get; set; } = true;
    public byte[]? EncryptionKey { get; set; }  // null => plaintext video
}

// Owns the native capture+encoder and pushes packetized frames to the transport.
public sealed class VideoStreamer : IDisposable
{
    private readonly ITransport _transport;
    private VideoPacketizer _packetizer = new(null);
    private NativeInterop.EncodedFrameCallback? _callback; // keep alive vs. GC
    private GCHandle _selfHandle;
    private bool _running;

    // Diagnostics counters.
    public long FramesSent { get; private set; }
    public double BitrateMbps { get; private set; }
    private long _bytesWindow;
    private DateTime _windowStart = DateTime.UtcNow;

    public VideoStreamer(ITransport transport) => _transport = transport;

    public void Start(VideoStreamConfig cfg)
    {
        _packetizer = new VideoPacketizer(cfg.EncryptionKey);
        _callback = OnEncodedFrame;
        _selfHandle = GCHandle.Alloc(this);
        int rc = NativeInterop.SslNativeStart(cfg.OutputIndex, cfg.Fps, cfg.BitrateKbps,
            cfg.UseHardware ? 1 : 0, _callback, GCHandle.ToIntPtr(_selfHandle));
        if (rc != 0)
            throw new InvalidOperationException($"Encoder start failed ({rc}): {NativeInterop.LastError()}");
        _running = true;
        Log.Info($"VideoStreamer started: output={cfg.OutputIndex} {cfg.Width}x{cfg.Height}@{cfg.Fps} {cfg.BitrateKbps}kbps hw={cfg.UseHardware}");
    }

    private static void OnEncodedFrame(uint frameId, ulong tsUs, int isKeyframe, IntPtr data, int len, IntPtr user)
    {
        if (user == IntPtr.Zero || len <= 0) return;
        var self = (VideoStreamer?)GCHandle.FromIntPtr(user).Target;
        if (self is not { _running: true }) return;

        var frame = new byte[len];
        Marshal.Copy(data, frame, 0, len);
        foreach (var pkt in self._packetizer.Packetize(frameId, tsUs, isKeyframe != 0, frame))
        {
            self._transport.SendVideoPacket(pkt);
            self._bytesWindow += pkt.Length;
        }
        self.FramesSent++;
        self.UpdateBitrate();
    }

    private void UpdateBitrate()
    {
        var now = DateTime.UtcNow;
        double sec = (now - _windowStart).TotalSeconds;
        if (sec >= 1.0)
        {
            BitrateMbps = _bytesWindow * 8.0 / 1_000_000.0 / sec;
            _bytesWindow = 0;
            _windowStart = now;
        }
    }

    public void RequestKeyframe() { if (_running) NativeInterop.SslNativeRequestKeyframe(); }
    public void SetBitrate(int kbps) { if (_running) NativeInterop.SslNativeSetBitrate(kbps); }
    public void SetFps(int fps) { if (_running) NativeInterop.SslNativeSetFps(fps); }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        try { NativeInterop.SslNativeStop(); } catch (Exception ex) { Log.Warn($"encoder stop: {ex.Message}"); }
        if (_selfHandle.IsAllocated) _selfHandle.Free();
        _callback = null;
    }

    public void Dispose() => Stop();
}
