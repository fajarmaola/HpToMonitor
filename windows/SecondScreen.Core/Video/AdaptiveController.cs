namespace SecondScreen.Core;

// Adaptive bitrate/FPS/resolution controller (ARCHITECTURE.md §Adaptive quality).
// Fed RTT (from heartbeat), keyframe-request rate (proxy for loss) and receiver STATS.
// Emits SET_QUALITY suggestions; the SessionManager applies them to the encoder and forwards
// them to the receiver.
public sealed class AdaptiveController
{
    private readonly int _targetBitrateKbps;
    private readonly int[] _fpsTiers = { 60, 45, 30 };
    private int _fpsTier;              // index into _fpsTiers
    private int _currentBitrateKbps;
    private DateTime _lastChange = DateTime.MinValue;
    private int _keyframeRequestsWindow;
    private DateTime _windowStart = DateTime.UtcNow;

    public int CurrentBitrateKbps => _currentBitrateKbps;
    public int CurrentFps => _fpsTiers[_fpsTier];

    public AdaptiveController(int targetBitrateKbps)
    {
        _targetBitrateKbps = targetBitrateKbps;
        _currentBitrateKbps = targetBitrateKbps;
    }

    public void NoteKeyframeRequest() => _keyframeRequestsWindow++;

    // Call ~once per second with the latest measured RTT in ms. Returns a SetQualityMessage if
    // a change is warranted, else null.
    public SetQualityMessage? Evaluate(double rttMs)
    {
        var now = DateTime.UtcNow;
        if ((now - _windowStart).TotalSeconds < 1.0) return null;
        int krps = _keyframeRequestsWindow;
        _keyframeRequestsWindow = 0;
        _windowStart = now;

        if ((now - _lastChange).TotalSeconds < 2.0) return null; // don't thrash

        // Heuristic loss signal: repeated keyframe requests => packet loss.
        bool severe = krps >= 3 || rttMs > 250;
        bool moderate = krps >= 1 || rttMs > 120;

        if (severe)
        {
            if (_fpsTier < _fpsTiers.Length - 1) { _fpsTier++; return Change(); }
            _currentBitrateKbps = Math.Max(1500, (int)(_currentBitrateKbps * 0.6));
            return Change();
        }
        if (moderate)
        {
            _currentBitrateKbps = Math.Max(2000, (int)(_currentBitrateKbps * 0.8));
            return Change();
        }
        // Healthy: recover gently.
        if (_currentBitrateKbps < _targetBitrateKbps)
        {
            _currentBitrateKbps = Math.Min(_targetBitrateKbps, _currentBitrateKbps + 1000);
            return Change();
        }
        if (_fpsTier > 0) { _fpsTier--; return Change(); }
        return null;
    }

    private SetQualityMessage Change()
    {
        _lastChange = DateTime.UtcNow;
        Log.Debug($"Adaptive => {_currentBitrateKbps}kbps @ {CurrentFps}fps");
        return new SetQualityMessage { BitrateKbps = _currentBitrateKbps, Fps = CurrentFps };
    }
}
