using System.Runtime.InteropServices;

namespace SecondScreen.Core;

// Makes the freshly-created IddCx virtual monitor an ACTIVE, EXTENDED "Display 2".
//
// Why the simple SetDisplayConfig(SDC_TOPOLOGY_EXTEND) was not enough:
//   The virtual monitor is added only a moment before we call it. The topology shortcut returns
//   ERROR_SUCCESS immediately but extends only to the displays that were ready at that instant, so
//   the brand-new virtual head is left INACTIVE ("Hanya tampilkan di 1" / Show only on 1). Nothing
//   renders to the phone and the desktop cannot spill onto it.
//
// Robust fix (CCD API, the approach virtual-display projects use):
//   1. Wait until QueryDisplayConfig(QDC_ALL_PATHS) reports the virtual head as an available but
//      inactive path.
//   2. Explicitly mark that path ACTIVE (invalidate its mode indices so Windows picks defaults).
//   3. Apply with SDC_USE_SUPPLIED_DISPLAY_CONFIG so Windows enables exactly the paths we supply.
//   4. Fall back to the SDC_TOPOLOGY_EXTEND shortcut if the supplied-config path fails.
public static class DisplayTopology
{
    // ---- P/Invoke -------------------------------------------------------------------------
    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(
        uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements, [Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
        ref uint numModeInfoArrayElements, [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
        nint currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int SetDisplayConfig(
        uint numPathArrayElements, [In] DISPLAYCONFIG_PATH_INFO[]? pathArray,
        uint numModeInfoArrayElements, [In] DISPLAYCONFIG_MODE_INFO[]? modeInfoArray,
        uint flags);

    private const int ERROR_SUCCESS = 0;

    private const uint QDC_ALL_PATHS = 0x00000001;
    private const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;

    private const uint SDC_APPLY = 0x00000080;
    private const uint SDC_USE_SUPPLIED_DISPLAY_CONFIG = 0x00000020;
    private const uint SDC_SAVE_TO_DATABASE = 0x00000200;
    private const uint SDC_ALLOW_CHANGES = 0x00000400;
    private const uint SDC_TOPOLOGY_INTERNAL = 0x00000001;
    private const uint SDC_TOPOLOGY_EXTEND = 0x00000004;

    private const uint DISPLAYCONFIG_PATH_ACTIVE = 0x00000001;
    private const uint DISPLAYCONFIG_PATH_MODE_IDX_INVALID = 0xFFFFFFFF;

    // ---- Public API -----------------------------------------------------------------------

    // Ensures the virtual head is an active, extended display. Retries because the IddCx monitor
    // needs a short moment to enumerate after SwDeviceCreate.
    public static async Task<bool> EnableExtendModeAsync(int retries = 20, int delayMs = 300)
    {
        for (int i = 0; i < retries; i++)
        {
            switch (TryActivateInactiveDisplays())
            {
                case Result.Extended:
                    Log.Info("Display 2 activated + extended (separate desktop, drag windows onto it).");
                    return true;
                case Result.AlreadyExtended:
                    Log.Info("Multiple displays already active/extended.");
                    return true;
                case Result.NotReady:
                    break; // virtual head not enumerated yet — wait and retry
                case Result.Failed:
                    break; // transient failure — wait and retry
            }
            await Task.Delay(delayMs);
        }

        // Last resort: ask Windows for its built-in extend topology.
        try
        {
            int rc = SetDisplayConfig(0, null, 0, null, SDC_APPLY | SDC_TOPOLOGY_EXTEND);
            if (rc == ERROR_SUCCESS)
            {
                Log.Warn("Fell back to SDC_TOPOLOGY_EXTEND shortcut (supplied-config activation did not settle).");
                return true;
            }
            Log.Warn($"SDC_TOPOLOGY_EXTEND fallback failed rc={rc}. User can force Win+P -> Extend.");
        }
        catch (Exception ex) { Log.Warn($"Extend fallback threw: {ex.Message}"); }
        return false;
    }

    private enum Result { Extended, AlreadyExtended, NotReady, Failed }

    private static Result TryActivateInactiveDisplays()
    {
        int rc = GetDisplayConfigBufferSizes(QDC_ALL_PATHS, out uint pathCount, out uint modeCount);
        if (rc != ERROR_SUCCESS || pathCount == 0) return Result.NotReady;

        var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
        var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
        rc = QueryDisplayConfig(QDC_ALL_PATHS, ref pathCount, paths, ref modeCount, modes, nint.Zero);
        if (rc != ERROR_SUCCESS) return Result.Failed;

        int activeCount = 0, activatable = 0;
        for (int i = 0; i < pathCount; i++)
        {
            bool isActive = (paths[i].flags & DISPLAYCONFIG_PATH_ACTIVE) != 0;
            if (isActive) { activeCount++; continue; }

            // An available-but-inactive path with a valid target is a display we can turn on
            // (this is the virtual head we just created, and possibly other detached monitors).
            if (paths[i].targetInfo.targetAvailable && paths[i].targetInfo.id != 0)
            {
                paths[i].flags |= DISPLAYCONFIG_PATH_ACTIVE;
                paths[i].sourceInfo.modeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
                paths[i].targetInfo.modeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
                activatable++;
            }
        }

        if (activatable == 0)
            return activeCount >= 2 ? Result.AlreadyExtended : Result.NotReady;

        // Let Windows compute modes/positions for the newly-activated paths and persist the layout.
        rc = SetDisplayConfig(pathCount, paths, modeCount, modes,
            SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES | SDC_SAVE_TO_DATABASE);

        if (rc == ERROR_SUCCESS) return Result.Extended;
        Log.Debug($"SetDisplayConfig(supplied) rc={rc}; will retry.");
        return Result.Failed;
    }

    private static int ActivePathCount()
    {
        try
        {
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint p, out uint m) != ERROR_SUCCESS)
                return 0;
            var paths = new DISPLAYCONFIG_PATH_INFO[p];
            var modes = new DISPLAYCONFIG_MODE_INFO[m];
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref p, paths, ref m, modes, nint.Zero) != ERROR_SUCCESS)
                return 0;
            return (int)p;
        }
        catch { return 0; }
    }

    // Best-effort collapse to a single display. NOT called automatically on disconnect so we never
    // switch off a user's real external monitor; Windows drops the virtual head on its own.
    public static void RestoreInternalOnly()
    {
        try { SetDisplayConfig(0, null, 0, null, SDC_APPLY | SDC_TOPOLOGY_INTERNAL); }
        catch (Exception ex) { Log.Debug($"RestoreInternalOnly ignored: {ex.Message}"); }
    }

    // ---- CCD structs ----------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_RATIONAL { public uint Numerator; public uint Denominator; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINTL { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate;
        public uint scanLineOrdering;
        [MarshalAs(UnmanagedType.Bool)] public bool targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_VIDEO_SIGNAL_INFO
    {
        public ulong pixelRate;
        public DISPLAYCONFIG_RATIONAL hSyncFreq;
        public DISPLAYCONFIG_RATIONAL vSyncFreq;
        public uint activeSizeCx;
        public uint activeSizeCy;
        public uint totalSizeCx;
        public uint totalSizeCy;
        public uint videoStandard;
        public uint scanLineOrdering;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_TARGET_MODE
    {
        public DISPLAYCONFIG_VIDEO_SIGNAL_INFO targetVideoSignalInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_SOURCE_MODE
    {
        public uint width;
        public uint height;
        public uint pixelFormat;
        public POINTL position;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_DESKTOP_IMAGE_INFO
    {
        public POINTL PathSourceSize;
        public int DesktopImageRegionLeft;
        public int DesktopImageRegionTop;
        public int DesktopImageRegionRight;
        public int DesktopImageRegionBottom;
        public int DesktopImageClipLeft;
        public int DesktopImageClipTop;
        public int DesktopImageClipRight;
        public int DesktopImageClipBottom;
    }

    // Union of target/source/desktop image mode. Sized to the largest member (target = 48 bytes).
    [StructLayout(LayoutKind.Explicit)]
    private struct DISPLAYCONFIG_MODE_INFO_UNION
    {
        [FieldOffset(0)] public DISPLAYCONFIG_TARGET_MODE targetMode;
        [FieldOffset(0)] public DISPLAYCONFIG_SOURCE_MODE sourceMode;
        [FieldOffset(0)] public DISPLAYCONFIG_DESKTOP_IMAGE_INFO desktopImageInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_MODE_INFO
    {
        public uint infoType;
        public uint id;
        public LUID adapterId;
        public DISPLAYCONFIG_MODE_INFO_UNION modeInfo;
    }
}
