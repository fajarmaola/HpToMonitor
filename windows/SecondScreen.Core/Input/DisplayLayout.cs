using System.Runtime.InteropServices;

namespace SecondScreen.Core;

// Enumerates Windows monitors and the virtual-desktop bounding rectangle so touch coordinates
// can be mapped to the correct display (ARCHITECTURE.md §Input mapping).
public sealed class MonitorRect
{
    public int Index { get; init; }
    public int Left { get; init; }
    public int Top { get; init; }
    public int Right { get; init; }
    public int Bottom { get; init; }
    public bool IsPrimary { get; init; }
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public override string ToString() => $"#{Index} [{Left},{Top} {Width}x{Height}]{(IsPrimary ? " primary" : "")}";
}

public static class DisplayLayout
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    private const uint MONITORINFOF_PRIMARY = 1;

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    private delegate bool MonitorEnumProc(IntPtr hMon, IntPtr hdc, ref RECT rect, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    private const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77,
                      SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;

    public static (int x, int y, int w, int h) VirtualScreen() =>
        (GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_YVIRTUALSCREEN),
         GetSystemMetrics(SM_CXVIRTUALSCREEN), GetSystemMetrics(SM_CYVIRTUALSCREEN));

    public static List<MonitorRect> GetMonitors()
    {
        var list = new List<MonitorRect>();
        int idx = 0;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr hdc, ref RECT r, IntPtr d) =>
        {
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(hMon, ref mi))
            {
                list.Add(new MonitorRect
                {
                    Index = idx++,
                    Left = mi.rcMonitor.left, Top = mi.rcMonitor.top,
                    Right = mi.rcMonitor.right, Bottom = mi.rcMonitor.bottom,
                    IsPrimary = (mi.dwFlags & MONITORINFOF_PRIMARY) != 0
                });
            }
            return true;
        }, IntPtr.Zero);
        return list;
    }

    // Heuristic: the non-primary monitor is the virtual/Android display (once IddCx is active).
    // Falls back to the primary monitor if only one display exists.
    public static MonitorRect GetTargetDisplay()
    {
        var mons = GetMonitors();
        var secondary = mons.FirstOrDefault(m => !m.IsPrimary);
        return secondary ?? mons.FirstOrDefault() ?? new MonitorRect { Right = 1920, Bottom = 1080 };
    }
}
