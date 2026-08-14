using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SecondScreen.Core;

// Result of a one-step driver install/ensure operation.
public sealed class DriverInstallResult
{
    public bool Success;         // driver is now installed (or was already)
    public bool Skipped;         // already installed with an equal/newer version
    public bool RebootRequired;  // Windows asked for a reboot (pnputil 3010)
    public string Message = "";
}

// One-step virtual-display driver installer, driven entirely from the desktop app so the user
// never has to run pnputil/PowerShell by hand.
//
//   * GetInstalledVersion() reads `pnputil /enum-drivers` (no elevation needed) and finds our
//     SecondScreenDisplay.inf entry + its version — label-agnostic so it also works on a
//     localized (e.g. Indonesian) Windows.
//   * EnsureInstalledAsync() compares the bundled INF version with the installed one and only
//     installs/updates when needed (a single UAC prompt via pnputil /add-driver /install).
//   * EnableTestSigningAsync() offers a one-click way to turn on test-signing when the driver
//     is not WHQL/EV signed (still requires a reboot — a Windows rule we cannot bypass).
public static class DriverInstaller
{
    public const string InfFileName = "SecondScreenDisplay.inf";

    // Locate the driver package that ships next to the desktop .exe (installer copies it to
    // "driver\"). SSL_DRIVER_DIR can override the folder for developer builds.
    public static string? FindInfPath()
    {
        var candidates = new List<string>();
        var env = Environment.GetEnvironmentVariable("SSL_DRIVER_DIR");
        if (!string.IsNullOrWhiteSpace(env)) candidates.Add(Path.Combine(env, InfFileName));
        var baseDir = AppContext.BaseDirectory;
        candidates.Add(Path.Combine(baseDir, "driver", InfFileName));
        candidates.Add(Path.Combine(baseDir, InfFileName));
        foreach (var c in candidates)
            if (File.Exists(c)) return c;
        return null;
    }

    // Parse the DriverVer line of the bundled INF, e.g. "DriverVer = 07/01/2025,1.0.0.0".
    public static Version? GetBundledVersion(string infPath)
    {
        try
        {
            foreach (var line in File.ReadAllLines(infPath))
            {
                var t = line.Trim();
                if (!t.StartsWith("DriverVer", StringComparison.OrdinalIgnoreCase)) continue;
                var m = Regex.Match(t, @"(\d+\.\d+\.\d+\.\d+)");
                if (m.Success && Version.TryParse(m.Value, out var v)) return v;
            }
        }
        catch { /* ignore */ }
        return null;
    }

    // Returns the installed driver version, or null if our driver is not installed.
    public static Version? GetInstalledVersion()
    {
        var output = RunCapture("pnputil.exe", "/enum-drivers");
        if (string.IsNullOrEmpty(output)) return null;

        // Blocks are separated by blank lines; find the one that mentions our INF.
        var blocks = Regex.Split(output, @"\r?\n\s*\r?\n");
        foreach (var b in blocks)
        {
            if (b.IndexOf("secondscreendisplay.inf", StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            var m = Regex.Match(b, @"(\d+\.\d+\.\d+\.\d+)");
            if (m.Success && Version.TryParse(m.Value, out var v)) return v;
            return new Version(0, 0, 0, 0); // installed, version unreadable
        }
        return null;
    }

    public static bool IsInstalled() => GetInstalledVersion() != null;

    // Best-effort test-signing check. bcdedit query needs admin, so a non-elevated call typically
    // fails -> we return null ("unknown"). true/false only when we can actually read it.
    public static bool? IsTestSigningOn()
    {
        var outp = RunCapture("bcdedit.exe", "/enum {current}");
        if (string.IsNullOrEmpty(outp)) return null;
        if (outp.IndexOf("denied", StringComparison.OrdinalIgnoreCase) >= 0 ||
            outp.IndexOf("Access is", StringComparison.OrdinalIgnoreCase) >= 0)
            return null;
        var m = Regex.Match(outp, @"testsigning\s+(Yes|No|On|Off)", RegexOptions.IgnoreCase);
        if (!m.Success) return false; // bcdedit omits the line when test-signing is off
        var v = m.Groups[1].Value.ToLowerInvariant();
        return v == "yes" || v == "on";
    }

    // Install the driver only if it is missing or older than the bundled package.
    public static async Task<DriverInstallResult> EnsureInstalledAsync(Action<string>? log = null)
    {
        var r = new DriverInstallResult();

        var inf = FindInfPath();
        if (inf == null)
        {
            r.Message = "Berkas driver (SecondScreenDisplay.inf) tidak ditemukan di folder aplikasi.";
            log?.Invoke(r.Message);
            return r;
        }

        log?.Invoke("Memeriksa driver Layar 2 yang terpasang…");
        var installed = await Task.Run(GetInstalledVersion);
        var bundled = GetBundledVersion(inf);

        if (installed != null && (bundled == null || installed >= bundled))
        {
            r.Success = true;
            r.Skipped = true;
            r.Message = $"Driver sudah terpasang (versi {installed}). Instalasi dilewati.";
            log?.Invoke(r.Message);
            return r;
        }

        log?.Invoke(installed == null
            ? "Memasang driver Layar 2… (setujui dialog Windows/UAC)"
            : $"Memperbarui driver ({installed} → {bundled})… (setujui UAC)");

        var (ok, code, cancelled) = await RunElevatedAsync("pnputil.exe",
            $"/add-driver \"{inf}\" /install");

        if (cancelled)
        {
            r.Message = "Instalasi driver dibatalkan pada dialog UAC.";
            log?.Invoke(r.Message);
            return r;
        }

        if (ok && (code == 0 || code == 3010))
        {
            r.Success = true;
            r.RebootRequired = code == 3010;
            r.Message = r.RebootRequired
                ? "Driver terpasang. Windows meminta restart agar Layar 2 aktif penuh."
                : "Driver Layar 2 berhasil dipasang.";
            log?.Invoke(r.Message);
            return r;
        }

        r.Message = $"Gagal memasang driver (kode {code}). " +
                    "Jika muncul peringatan tanda tangan, aktifkan Test Signing lalu coba lagi.";
        log?.Invoke(r.Message);
        return r;
    }

    // Best-effort removal (used by uninstall/troubleshooting).
    public static async Task<DriverInstallResult> UninstallAsync(Action<string>? log = null)
    {
        var r = new DriverInstallResult();
        log?.Invoke("Menghapus driver Layar 2…");
        var (ok, code, cancelled) = await RunElevatedAsync("pnputil.exe",
            $"/delete-driver {InfFileName} /uninstall /force");
        if (cancelled) { r.Message = "Dibatalkan pada UAC."; return r; }
        r.Success = ok && (code == 0 || code == 3010);
        r.RebootRequired = code == 3010;
        r.Message = r.Success ? "Driver dihapus." : $"Gagal menghapus (kode {code}).";
        log?.Invoke(r.Message);
        return r;
    }

    // Turn on Windows test-signing (needed for a self/test-signed driver). Requires a reboot.
    public static async Task<DriverInstallResult> EnableTestSigningAsync(Action<string>? log = null)
    {
        var r = new DriverInstallResult();
        log?.Invoke("Mengaktifkan Test Signing… (setujui UAC)");
        var (ok, code, cancelled) = await RunElevatedAsync("bcdedit.exe", "/set testsigning on");
        if (cancelled) { r.Message = "Dibatalkan pada UAC."; return r; }
        r.Success = ok && code == 0;
        r.RebootRequired = r.Success;
        r.Message = r.Success
            ? "Test Signing aktif. Restart Windows, lalu tekan Mulai lagi."
            : $"Gagal mengaktifkan Test Signing (kode {code}).";
        log?.Invoke(r.Message);
        return r;
    }

    // ---- process helpers ----------------------------------------------------------------
    private static string? RunCapture(string file, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return null;
            string outp = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return outp;
        }
        catch { return null; }
    }

    private static async Task<(bool ok, int code, bool cancelled)> RunElevatedAsync(string file, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = true,
                Verb = "runas",              // triggers the UAC elevation prompt
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return (false, -1, false);
            await p.WaitForExitAsync();
            return (true, p.ExitCode, false);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // User declined the UAC prompt (ERROR_CANCELLED) or elevation not available.
            return (false, -1, true);
        }
        catch
        {
            return (false, -1, false);
        }
    }
}
