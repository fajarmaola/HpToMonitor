namespace SecondScreen.Core;

// Aggregated diagnostics shown in the UI and the Android overlay (STATISTICS OVERLAY).
public sealed class Diagnostics
{
    public double NetworkLatencyMs { get; set; }   // one-way estimate = RTT / 2
    public double BitrateMbps { get; set; }
    public string Codec { get; set; } = "H.264";
    public string Connection { get; set; } = "LAN";
    public int Width { get; set; }
    public int Height { get; set; }
    public int Fps { get; set; }
    public long HostFramesSent { get; set; }
    public double DecodeFps { get; set; }          // reported by receiver STATS
    public double RenderFps { get; set; }
    public long DroppedFrames { get; set; }
    public double JitterMs { get; set; }

    public event EventHandler? Updated;
    public void RaiseUpdated() => Updated?.Invoke(this, EventArgs.Empty);
}
