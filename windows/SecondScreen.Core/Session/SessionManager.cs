using System.Diagnostics;
using System.Net;

namespace SecondScreen.Core;

public sealed class SessionOptions
{
    public string HostName { get; set; } = Environment.MachineName;
    public QualityMode Quality { get; set; } = QualityMode.Balanced;
    public bool UseHardwareEncoder { get; set; } = true;
    public bool EncryptVideo { get; set; } = true;
    public bool UseVirtualDisplay { get; set; } = true; // create Display 2 if driver present
}

// The central orchestrator (ARCHITECTURE.md §Session state machine). Owns the transport,
// pairing, video streamer, input injector, virtual display and heartbeat. UI subscribes to
// StateChanged / PinReady / Diagnostics.Updated.
public sealed class SessionManager : IDisposable
{
    private readonly SessionOptions _opts;
    private readonly TrustedDeviceStore _store = new();
    private readonly DiscoveryService _discovery;
    private readonly VirtualDisplayController _vdisplay = new();
    private readonly InputInjector _input = new();
    public Diagnostics Diagnostics { get; } = new();

    private ITransport? _transport;
    private PairingService? _pairing;
    private VideoStreamer? _streamer;
    private AdaptiveController? _adaptive;
    private byte[]? _sessionKey;
    private bool _secure;
    private CancellationTokenSource? _cts;

    private DeviceInfo _peerDevice = new();
    private int _peerVideoPort = Protocol.VideoUdpPort;
    private int _missedPongs;
    private double _rttMs;
    private uint _frameId;

    public SessionState State { get; private set; } = SessionState.Idle;
    public DeviceInfo PeerDevice => _peerDevice;

    public event EventHandler<SessionState>? StateChanged;
    public event EventHandler<string>? PinReady;              // show PIN to user
    public event EventHandler<string>? Error;                 // human-readable error

    public SessionManager(SessionOptions opts)
    {
        _opts = opts;
        _discovery = new DiscoveryService(opts.HostName);
    }

    private void SetState(SessionState s)
    {
        if (State == s) return;
        State = s;
        Log.Info($"Session state -> {s}");
        StateChanged?.Invoke(this, s);
    }

    // Begin discovery and wait for an Android device to connect (LAN transport).
    public async Task StartAsync()
    {
        _cts = new CancellationTokenSource();
        _discovery.Start(periodicBroadcast: true);
        SetState(SessionState.Discovering);
        await BeginTransport();
    }

    private async Task BeginTransport()
    {
        _transport = new LanTransport(); // ITransport — swap for USB/Wi-Fi Direct later
        _transport.ControlFrameReceived += OnControlFrame;
        _transport.PeerConnected += (_, _) => SetState(SessionState.Connecting);
        _transport.PeerDisconnected += (_, _) => OnPeerDisconnected();
        Diagnostics.Connection = _transport.Name;
        try { await _transport.StartAsync(_cts!.Token); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { RaiseError($"Transport error: {ex.Message}"); }
    }

    private void OnControlFrame(object? sender, byte[] payload)
    {
        try
        {
            string json = ControlMessaging.DecodeText(payload, _secure ? _sessionKey : null);
            string type = ControlMessaging.PeekType(json);
            Route(type, json);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Decryption/auth failure in secure phase => wrong key / tampering.
            HandlePairFailure();
        }
        catch (Exception ex) { Log.Warn($"control frame error: {ex.Message}"); }
    }

    private async void Route(string type, string json)
    {
        switch (type)
        {
            case MessageType.Hello: await HandleHello(json); break;
            case MessageType.PairConfirm: await HandlePairConfirm(json); break;
            case MessageType.SessionConfigAck: HandleConfigAck(json); break;
            case MessageType.Ping: await HandlePing(json); break;
            case MessageType.Pong: HandlePong(json); break;
            case MessageType.Touch: HandleTouch(json); break;
            case MessageType.RequestKeyframe: _streamer?.RequestKeyframe(); _adaptive?.NoteKeyframeRequest(); break;
            case MessageType.Stats: HandleStats(json); break;
            case MessageType.DeviceUpdate: HandleDeviceUpdate(json); break;
            case MessageType.Disconnect: Disconnect("peer requested"); break;
        }
    }

    // ---- Pairing ----------------------------------------------------------------------
    private async Task HandleHello(string json)
    {
        var hello = ControlMessaging.Parse<HelloMessage>(json);
        if (hello == null) return;
        if (hello.V != Protocol.Version) { await SendPlain(new DisconnectMessage { Reason = "protocol version mismatch" }); return; }

        _peerDevice = hello.Device;
        _pairing = new PairingService(_store);
        _pairing.PinGenerated += (_, pin) => PinReady?.Invoke(this, pin);

        SetState(SessionState.Pairing);
        var ack = _pairing.HandleHello(hello, _opts.HostName);
        await SendPlain(ack);                 // HELLO_ACK is the last plaintext frame
        _sessionKey = _pairing.SessionKey;
        _secure = true;                        // everything after this is AES-GCM
        _discovery.Paired = true;

        if (_pairing.IsTrustedReconnect)
            Log.Info("Awaiting encrypted PAIR_CONFIRM from trusted device");
    }

    private async Task HandlePairConfirm(string json)
    {
        var msg = ControlMessaging.Parse<TokenMessage>(json);
        // If we reached here, AES-GCM already authenticated the frame => PIN/keys agree.
        _pairing?.ConfirmPaired();
        await SendSecure(new TokenMessage { Type = MessageType.PairOk, Token = msg?.Token ?? "" });
        await StartStreamingSession();
    }

    private void HandlePairFailure()
    {
        bool lockout = _pairing?.RegisterFailure() ?? false;
        RaiseError(lockout ? "Pairing failed: device locked out after too many attempts."
                           : "Pairing failed: wrong PIN. Ask the user to re-enter.");
        // Send plaintext failure (peer cannot decrypt anyway) and reset for a retry.
        _ = SendPlain(new DisconnectMessage { Reason = "pairing failed" });
        _secure = false; _sessionKey = null;
    }

    // ---- Session config & streaming ---------------------------------------------------
    private async Task StartStreamingSession()
    {
        SetState(SessionState.Configuring);

        int w = _peerDevice.Width, h = _peerDevice.Height, hz = _peerDevice.RefreshHz;
        int outputIndex = 0;
        bool usingVirtual = false;

        if (_opts.UseVirtualDisplay && _vdisplay.IsDriverPresent())
        {
            if (_vdisplay.CreateVirtualDisplay(w, h, hz > 0 ? hz : 60))
            {
                // Force EXTEND topology so Windows treats the phone as a separate desktop instead
                // of duplicating the primary display (the "1|2 in one box" / mirror problem).
                await DisplayTopology.EnableExtendModeAsync();
                // Capture the newly-added display (now a distinct output — typically the last index).
                outputIndex = Math.Max(0, NativeSafeOutputCount() - 1);
                usingVirtual = true;
            }
            else
            {
                Log.Warn("Virtual display unavailable — mirroring the primary display. " +
                         "Ensure the app runs as Administrator (SwDeviceCreate needs elevation).");
            }
        }
        else
        {
            Log.Warn("IddCx driver not present — capturing primary display instead of a virtual Display 2.");
        }
        Diagnostics.UsingVirtualDisplay = usingVirtual;

        var (bitrate, fps) = QualityPreset(_opts.Quality, w, h, hz);
        _adaptive = new AdaptiveController(bitrate);

        var cfg = new SessionConfigMessage
        {
            Codec = "h264", Width = w, Height = h, Fps = fps,
            BitrateKbps = bitrate, VideoPort = Protocol.VideoUdpPort,
            EncryptVideo = _opts.EncryptVideo, Orientation = "auto"
        };
        await SendSecure(cfg);

        Diagnostics.Width = w; Diagnostics.Height = h; Diagnostics.Fps = fps;
        Diagnostics.Codec = "H.264"; Diagnostics.RaiseUpdated();

        // Start the encoder; frames flow to the peer once we know its video port (config ack).
        _streamer = new VideoStreamer(_transport!);
        _streamer.FatalError += msg =>
        {
            Diagnostics.VideoStatus = msg;
            Diagnostics.RaiseUpdated();
            RaiseError($"Status video HP: {msg}");
        };
        try
        {
            _streamer.Start(new VideoStreamConfig
            {
                OutputIndex = outputIndex, Width = w, Height = h, Fps = fps,
                BitrateKbps = bitrate, UseHardware = _opts.UseHardwareEncoder,
                EncryptionKey = _opts.EncryptVideo ? _sessionKey : null
            });
        }
        catch (Exception ex)
        {
            RaiseError($"Encoder unavailable: {ex.Message}");
        }
    }

    private static int NativeSafeOutputCount()
    {
        try { return NativeInterop.SslNativeGetOutputCount(); } catch { return 1; }
    }

    private void HandleConfigAck(string json)
    {
        var cfg = ControlMessaging.Parse<SessionConfigMessage>(json);
        if (cfg != null) _peerVideoPort = cfg.VideoPort;
        if (_transport?.PeerAddress != null)
            _transport.SetPeerVideoEndpoint(_transport.PeerAddress.Address, _peerVideoPort);
        _streamer?.RequestKeyframe();
        SetState(SessionState.Streaming);
        StartHeartbeat();
        StartDiagnosticsPump();
    }

    private static (int bitrateKbps, int fps) QualityPreset(QualityMode q, int w, int h, int hz)
    {
        double mp = Math.Max(1, w * (double)h) / 1_000_000.0;
        int cap = hz >= 60 ? 60 : (hz > 0 ? hz : 60);
        return q switch
        {
            QualityMode.Performance => ((int)(mp * 4000), Math.Min(60, cap)),
            QualityMode.Balanced    => ((int)(mp * 6000), Math.Min(60, cap)),
            QualityMode.HighQuality => ((int)(mp * 9000), Math.Min(60, cap)),
            _ => (8000, 60)
        };
    }

    // ---- Heartbeat / reconnect --------------------------------------------------------
    private void StartHeartbeat()
    {
        _ = Task.Run(async () =>
        {
            while (_cts is { IsCancellationRequested: false } && State is SessionState.Streaming or SessionState.Reconnecting)
            {
                try
                {
                    await SendSecure(new PingMessage { Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
                    _missedPongs++;
                    if (_missedPongs >= Protocol.MissedPongsToDrop) EnterReconnecting();

                    var q = _adaptive?.Evaluate(_rttMs);
                    if (q != null)
                    {
                        _streamer?.SetBitrate(q.BitrateKbps);
                        _streamer?.SetFps(q.Fps);
                        await SendSecure(q);
                        Diagnostics.Fps = q.Fps; Diagnostics.RaiseUpdated();
                    }
                }
                catch (Exception ex) { Log.Debug($"heartbeat: {ex.Message}"); }
                await Task.Delay(Protocol.HeartbeatIntervalMs);
            }
        });
    }

    private async Task HandlePing(string json)
    {
        var ping = ControlMessaging.Parse<PingMessage>(json);
        await SendSecure(new PingMessage { Type = MessageType.Pong, Ts = ping?.Ts ?? 0 });
    }

    private void HandlePong(string json)
    {
        var pong = ControlMessaging.Parse<PingMessage>(json);
        if (pong == null) return;
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _rttMs = Math.Max(0, now - pong.Ts);
        _missedPongs = 0;
        Diagnostics.NetworkLatencyMs = _rttMs / 2.0;
        if (State == SessionState.Reconnecting) SetState(SessionState.Streaming);
    }

    private void EnterReconnecting()
    {
        if (State == SessionState.Reconnecting) return;
        SetState(SessionState.Reconnecting);
        Log.Warn("Connection unstable — reconnecting (virtual display kept alive during grace).");
        // NOTE: we deliberately do NOT tear down the virtual display here (power/grace policy).
        _ = Task.Run(async () =>
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < Protocol.ReconnectGraceMs)
            {
                await Task.Delay(1000);
                if (State == SessionState.Streaming) return; // recovered via PONG
            }
            Disconnect("reconnect grace elapsed");
        });
    }

    private void OnPeerDisconnected()
    {
        if (State is SessionState.Streaming) EnterReconnecting();
        else Disconnect("peer socket closed");
    }

    // ---- Input / stats ----------------------------------------------------------------
    private void HandleTouch(string json)
    {
        var t = ControlMessaging.Parse<TouchMessage>(json);
        if (t != null) _input.Handle(t);
    }

    private void HandleStats(string json)
    {
        var s = ControlMessaging.Parse<StatsMessage>(json);
        if (s == null) return;
        Diagnostics.DecodeFps = s.DecodeFps;
        Diagnostics.RenderFps = s.RenderFps;
        Diagnostics.DroppedFrames = s.DroppedFrames;
        Diagnostics.JitterMs = s.JitterMs;
        Diagnostics.RaiseUpdated();
    }

    private void HandleDeviceUpdate(string json)
    {
        var d = ControlMessaging.Parse<DeviceInfo>(json);
        if (d != null) { _peerDevice.Battery = d.Battery; _peerDevice.RefreshHz = d.RefreshHz; }
    }

    private void StartDiagnosticsPump()
    {
        _ = Task.Run(async () =>
        {
            while (State is SessionState.Streaming or SessionState.Reconnecting)
            {
                Diagnostics.BitrateMbps = _streamer?.BitrateMbps ?? 0;
                Diagnostics.HostFramesSent = _streamer?.FramesSent ?? 0;
                Diagnostics.RaiseUpdated();
                await Task.Delay(1000);
            }
        });
    }

    // ---- Send helpers -----------------------------------------------------------------
    private Task SendPlain(object message)
        => _transport?.SendControlFrameAsync(ControlMessaging.BuildFrame(message, null), _cts!.Token) ?? Task.CompletedTask;

    private Task SendSecure(object message)
        => _transport?.SendControlFrameAsync(ControlMessaging.BuildFrame(message, _sessionKey), _cts!.Token) ?? Task.CompletedTask;

    private void RaiseError(string msg) { Log.Error(msg); Error?.Invoke(this, msg); }

    // ---- Teardown ---------------------------------------------------------------------
    public void Disconnect(string reason)
    {
        Log.Info($"Disconnecting: {reason}");
        try { _ = SendSecure(new DisconnectMessage { Reason = reason }); } catch { }
        _streamer?.Stop();
        _vdisplay.Remove();          // safely remove Display 2 (ACCEPTANCE TEST #14)
        _transport?.Dispose();
        _transport = null;
        _secure = false; _sessionKey = null; _missedPongs = 0;
        _discovery.Paired = false;
        SetState(SessionState.Disconnected);
        SetState(SessionState.Idle);
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        _streamer?.Dispose();
        _vdisplay.Dispose();
        _transport?.Dispose();
        _discovery.Dispose();
    }
}
