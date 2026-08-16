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

    // Raised when the native capture/encode pipeline fails to produce frames (with the real reason).
    public event Action<string>? FatalError;

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

        // The native worker inits capture+encoder asynchronously, so poll its status and surface
        // the real error instead of silently showing a black phone with 0 bitrate.
        StartWatchdog();
    }

    private void StartWatchdog()
    {
        _ = Task.Run(async () =>
        {
            // Give the capture+encoder init a moment to settle.
            for (int i = 0; i < 6 && _running; i++)
            {
                await Task.Delay(400);
                int status = NativeInterop.SslNativeGetStatus();
                if (status < 0)
                {
                    string err = NativeInterop.LastError();
                    Log.Error($"Video pipeline failed (status {status}): {err}");
                    FatalError?.Invoke(err.Length > 0 ? err : $"Video pipeline error (status {status})");
                    return;
                }
                if (status == 1) break; // capturing OK
            }

            // Capturing reported OK — verify frames are actually flowing.
            if (!_running) return;
            await Task.Delay(1500);
            if (_running && FramesSent == 0)
            {
                // Native side self-heals (CPU converter / software-encoder fallback) — give it time.
                await Task.Delay(5000);
            }
            if (_running && FramesSent == 0)
            {
                string err = NativeInterop.LastError();
                Log.Error($"Video pipeline sent 0 frames. {err}");
                FatalError?.Invoke(err.Length > 0
                    ? err
                    : "No video frames were produced (capture returned nothing).");
            }
            else if (_running && NativeInterop.LastError().Contains("PRIMARY"))
            {
                // Fell back to primary-display mirror — the user must know (extend didn't work).
                Log.Warn(NativeInterop.LastError());
                FatalError?.Invoke(NativeInterop.LastError());
            }
            else if (_running && NativeInterop.LastError().Length > 0)
            {
                // Frames are flowing: the pipeline recovered on its own. Log, don't alarm.
                Log.Warn(NativeInterop.LastError());
            }
        });
    }

    private byte[]? _spsPps; // cached Annex-B SPS+PPS, re-injected before keyframes that lack them

    private static void OnEncodedFrame(uint frameId, ulong tsUs, int isKeyframe, IntPtr data, int len, IntPtr user)
    {
        if (user == IntPtr.Zero || len <= 0) return;
        var self = (VideoStreamer?)GCHandle.FromIntPtr(user).Target;
        if (self is not { _running: true }) return;

        var frame = new byte[len];
        Marshal.Copy(data, frame, 0, len);

        // Android MediaCodec can only start decoding from SPS/PPS. Encoders often emit them ONLY
        // on the very first sample, but the phone joins LATE (after tapping "Mulai Tampilkan"),
        // so cache SPS/PPS here and prepend them to every keyframe that lacks them.
        bool keyframe = isKeyframe != 0;
        var headers = ExtractSpsPps(frame);
        if (headers != null) self._spsPps = headers;
        else if (keyframe && self._spsPps != null)
        {
            var patched = new byte[self._spsPps.Length + frame.Length];
            Buffer.BlockCopy(self._spsPps, 0, patched, 0, self._spsPps.Length);
            Buffer.BlockCopy(frame, 0, patched, self._spsPps.Length, frame.Length);
            frame = patched;
        }

        int sent = 0;
        foreach (var pkt in self._packetizer.Packetize(frameId, tsUs, keyframe, frame))
        {
            self._transport.SendVideoPacket(pkt);
            self._bytesWindow += pkt.Length;
            // Pace large keyframe bursts slightly so Wi-Fi doesn't drop the tail packets.
            if (++sent % 24 == 0) Thread.Sleep(1);
        }
        self.FramesSent++;
        self.UpdateBitrate();
    }

    // Returns concatenated SPS+PPS NAL units (with start codes) when BOTH occur in the frame.
    private static byte[]? ExtractSpsPps(byte[] d)
    {
        byte[]? sps = null, pps = null;
        int start = -1, type = 0;
        void Take(int end)
        {
            if (start < 0) return;
            if (type == 7) sps = d.AsSpan(start, end - start).ToArray();
            else if (type == 8) pps = d.AsSpan(start, end - start).ToArray();
        }
        int i = 0;
        while (i + 3 < d.Length)
        {
            bool sc3 = d[i] == 0 && d[i + 1] == 0 && d[i + 2] == 1;
            bool sc4 = d[i] == 0 && d[i + 1] == 0 && d[i + 2] == 0 && d[i + 3] == 1;
            if (sc3 || sc4)
            {
                Take(i);
                int sc = sc3 ? 3 : 4;
                if (i + sc >= d.Length) { start = -1; break; }
                start = i; type = d[i + sc] & 0x1F;
                i += sc + 1;
            }
            else i++;
        }
        Take(d.Length);
        if (sps == null || pps == null) return null;
        var outBuf = new byte[sps.Length + pps.Length];
        Buffer.BlockCopy(sps, 0, outBuf, 0, sps.Length);
        Buffer.BlockCopy(pps, 0, outBuf, sps.Length, pps.Length);
        return outBuf;
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
