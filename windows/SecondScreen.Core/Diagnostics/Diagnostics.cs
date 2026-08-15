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

    // True when a real virtual "Display 2" is active; false when we fell back to mirroring the
    // primary display (e.g. driver missing or SwDeviceCreate denied without elevation).
    public bool UsingVirtualDisplay { get; set; }

    // Last video-pipeline problem surfaced by the native capture/encoder (empty when healthy).
    // Shown in the UI ("Cek Kesehatan") so a black screen is never a silent mystery.
    public string VideoStatus { get; set; } = "";

    public event EventHandler? Updated;
    public void RaiseUpdated() => Updated?.Invoke(this, EventArgs.Empty);
}
