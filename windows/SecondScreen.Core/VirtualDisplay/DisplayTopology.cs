using System.Runtime.InteropServices;

namespace SecondScreen.Core;

// Forces Windows into the "Extend" display topology so the IddCx virtual monitor becomes a REAL
// second desktop (Display 2) instead of a mirror/duplicate of the primary display.
//
// Root cause it fixes: after SwDeviceCreate enumerates the virtual monitor, Windows keeps whatever
// topology was last active. If that was "Duplicate these displays", the phone just mirrors the PC
// (the two monitors collapse into one "1|2" box in Display Settings). Calling SetDisplayConfig with
// SDC_TOPOLOGY_EXTEND is exactly what Win+P -> "Extend" does, but done automatically for the user.
//
// Docs: SetDisplayConfig (CCD API), flags SDC_APPLY | SDC_TOPOLOGY_EXTEND.
public static class DisplayTopology
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetDisplayConfig(
        uint numPathArrayElements, nint pathArray,
        uint numModeInfoArrayElements, nint modeInfoArray,
        uint flags);

    private const uint SDC_APPLY = 0x00000080;
    private const uint SDC_TOPOLOGY_INTERNAL = 0x00000001;
    private const uint SDC_TOPOLOGY_EXTEND = 0x00000004;
    private const int ERROR_SUCCESS = 0;

    // Switch all displays to EXTEND. A freshly-added IddCx monitor may take a moment to fully
    // enumerate, so we retry a few times. Returns true once Windows applied the extend topology.
    public static async Task<bool> EnableExtendModeAsync(int retries = 12, int delayMs = 250)
    {
        for (int i = 0; i < retries; i++)
        {
            int rc;
            try
            {
                rc = SetDisplayConfig(0, nint.Zero, 0, nint.Zero, SDC_APPLY | SDC_TOPOLOGY_EXTEND);
            }
            catch (Exception ex)
            {
                Log.Warn($"SetDisplayConfig(extend) threw: {ex.Message}");
                return false;
            }

            if (rc == ERROR_SUCCESS)
            {
                Log.Info("Display topology set to EXTEND — Display 2 is now a separate desktop, not a mirror.");
                return true;
            }

            Log.Debug($"SetDisplayConfig(extend) attempt {i + 1}/{retries} failed rc={rc}; retrying in {delayMs}ms...");
            await Task.Delay(delayMs);
        }

        Log.Warn("Could not switch to EXTEND topology automatically; the phone may still mirror the " +
                 "primary display. User can force it with Win+P -> Extend.");
        return false;
    }

    // Best-effort restore to single (internal) display after Display 2 is removed. Not fatal if it
    // fails — Windows collapses to the remaining monitor on its own when the virtual display goes.
    public static void RestoreInternalOnly()
    {
        try { SetDisplayConfig(0, nint.Zero, 0, nint.Zero, SDC_APPLY | SDC_TOPOLOGY_INTERNAL); }
        catch (Exception ex) { Log.Debug($"RestoreInternalOnly ignored: {ex.Message}"); }
    }
}
