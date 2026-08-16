using System.Runtime.InteropServices;

namespace SecondScreen.Core;

// Rotates the virtual display (Display 2) to match the phone's physical orientation using
// ChangeDisplaySettingsEx + DM_DISPLAYORIENTATION. Applied dynamically (not saved to registry)
// so a fresh session always starts unrotated.
public static class DisplayRotation
{
    private const int ENUM_CURRENT_SETTINGS = -1;
    private const int DISP_CHANGE_SUCCESSFUL = 0;
    private const uint DM_PELSWIDTH = 0x80000, DM_PELSHEIGHT = 0x100000, DM_DISPLAYORIENTATION = 0x80;
    private const uint DMDO_DEFAULT = 0, DMDO_90 = 1, DMDO_180 = 2, DMDO_270 = 3;
    private const uint DD_ATTACHED = 0x1, DD_PRIMARY = 0x4, DD_MIRROR = 0x8;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public uint dmFields;
        public int dmPositionX, dmPositionY;
        public uint dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public uint dmICMMethod, dmICMIntent, dmMediaType, dmDitherType,
                    dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE dd, uint dwFlags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE dm);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(string lpszDeviceName, ref DEVMODE dm, IntPtr hwnd, uint dwflags, IntPtr lParam);

    // The virtual display adapter (matched by driver name), falling back to any attached
    // non-primary display.
    public static string? FindVirtualDisplayDeviceName()
    {
        string? fallback = null;
        var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
        for (uint i = 0; EnumDisplayDevices(null, i, ref dd, 0); i++)
        {
            bool attached = (dd.StateFlags & DD_ATTACHED) != 0;
            bool primary = (dd.StateFlags & DD_PRIMARY) != 0;
            bool mirror = (dd.StateFlags & DD_MIRROR) != 0;
            if (attached && !mirror &&
                (dd.DeviceString.Contains("SecondScreen", StringComparison.OrdinalIgnoreCase) ||
                 dd.DeviceString.Contains("HP ke Monitor", StringComparison.OrdinalIgnoreCase)))
                return dd.DeviceName;
            if (attached && !primary && !mirror) fallback ??= dd.DeviceName;
            dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
        }
        return fallback;
    }

    // degrees = phone rotation (0/90/180/270). Swaps width/height when switching between
    // portrait and landscape, as ChangeDisplaySettingsEx requires.
    public static bool SetRotation(int degrees)
    {
        string? device = FindVirtualDisplayDeviceName();
        if (device == null) { Log.Warn("rotation: virtual display device not found"); return false; }

        var dm = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
        if (!EnumDisplaySettings(device, ENUM_CURRENT_SETTINGS, ref dm))
        { Log.Warn($"rotation: EnumDisplaySettings failed for {device}"); return false; }

        uint target = ((degrees / 90) % 4) switch { 1 => DMDO_90, 2 => DMDO_180, 3 => DMDO_270, _ => DMDO_DEFAULT };
        if (dm.dmDisplayOrientation == target) return true;

        bool curSwapped = dm.dmDisplayOrientation is DMDO_90 or DMDO_270;
        bool newSwapped = target is DMDO_90 or DMDO_270;
        if (curSwapped != newSwapped) (dm.dmPelsWidth, dm.dmPelsHeight) = (dm.dmPelsHeight, dm.dmPelsWidth);
        dm.dmDisplayOrientation = target;
        dm.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYORIENTATION;

        int rc = ChangeDisplaySettingsEx(device, ref dm, IntPtr.Zero, 0, IntPtr.Zero);
        Log.Info($"rotation {degrees}° on {device}: rc={rc} ({dm.dmPelsWidth}x{dm.dmPelsHeight})");
        return rc == DISP_CHANGE_SUCCESSFUL;
    }
}
