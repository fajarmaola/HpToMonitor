using System.Collections.Generic;

namespace SecondScreen.Desktop;

public enum AppLang { ID, EN }

// Lightweight runtime localization. Default language is Indonesian; the choice is persisted by
// AppSettings. UI elements re-read strings via T(key) whenever Changed fires.
public static class Loc
{
    public static AppLang Current = AppLang.ID;
    public static event Action? Changed;

    public static void Set(AppLang lang)
    {
        if (Current == lang) return;
        Current = lang;
        Changed?.Invoke();
    }

    public static void ToggleLang() => Set(Current == AppLang.ID ? AppLang.EN : AppLang.ID);

    // Label shown ON the toggle button = the language you'll switch TO.
    public static string ToggleLabel() => Current == AppLang.ID ? "EN" : "ID";

    private static readonly Dictionary<string, (string id, string en)> S = new()
    {
        ["tagline"] = ("Ubah HP jadi layar kedua PC — offline", "Turn your phone into a second PC display — offline"),
        ["lbl.device"] = ("PERANGKAT ANDROID", "ANDROID DEVICE"),
        ["lbl.connection"] = ("KONEKSI", "CONNECTION"),
        ["lbl.resolution"] = ("RESOLUSI", "RESOLUTION"),
        ["lbl.latency"] = ("LATENSI", "LATENCY"),
        ["lbl.performance"] = ("PERFORMA", "PERFORMANCE"),
        ["q.performance"] = ("Hemat (lancar)", "Performance (smooth)"),
        ["q.balanced"] = ("Seimbang", "Balanced"),
        ["q.high"] = ("Kualitas Tinggi", "High Quality"),
        ["chk.vd"] = ("Buat Layar 2 virtual (driver IddCx)", "Create virtual Display 2 (IddCx driver)"),
        ["chk.enc"] = ("Enkripsi aliran video (AES-256-GCM)", "Encrypt video stream (AES-256-GCM)"),
        ["chk.hw"] = ("Utamakan encoder H.264 hardware", "Prefer hardware H.264 encoder"),
        ["pin.prompt"] = ("Sebuah perangkat ingin terhubung. Masukkan kode ini di Android:", "A device wants to connect. Enter this code on Android:"),
        ["driver.hint"] = ("Driver Layar 2 dipasang oleh installer. Lihat status di ‘Cek Kesehatan’.", "The Display 2 driver is set up by the installer. See status in ‘Health Check’."),
        ["btn.start"] = ("Mulai", "Start"),
        ["btn.disconnect"] = ("Putuskan", "Disconnect"),
        ["btn.display"] = ("Pengaturan Layar", "Display Settings"),
        ["btn.health"] = ("Cek Kesehatan", "Health Check"),
        ["badge.disconnected"] = ("TERPUTUS", "DISCONNECTED"),
        ["badge.waiting"] = ("MENUNGGU", "WAITING"),
        ["badge.searching"] = ("MENCARI", "SEARCHING"),
        ["badge.connecting"] = ("MENYAMBUNG", "CONNECTING"),
        ["badge.pairing"] = ("PAIRING", "PAIRING"),
        ["badge.connected"] = ("TERSAMBUNG", "CONNECTED"),
        ["badge.reconnecting"] = ("MENYAMBUNG ULANG", "RECONNECTING"),
        ["pin.title"] = ("HP ke Monitor — Kode Sambungan", "HP ke Monitor — Pairing Code"),
        ["start.fail"] = ("Gagal memulai", "Failed to start"),
        // Health window
        ["h.title"] = ("Cek Kesehatan", "Health Check"),
        ["h.subtitle"] = ("Status komponen & perbaikan cepat", "Component status & quick fixes"),
        ["h.driver"] = ("Driver Layar 2", "Display 2 driver"),
        ["h.testsigning"] = ("Test Signing Windows", "Windows test signing"),
        ["h.network"] = ("Koneksi / Jaringan", "Connection / Network"),
        ["h.fix"] = ("Perbaiki", "Fix"),
        ["h.enable"] = ("Aktifkan", "Enable"),
        ["h.opennet"] = ("Buka Pengaturan Jaringan", "Open network settings"),
        ["h.uninstall"] = ("Uninstall Bersih Driver", "Clean-uninstall driver"),
        ["h.refresh"] = ("Muat Ulang", "Refresh"),
        ["h.close"] = ("Tutup", "Close"),
        ["h.checking"] = ("memeriksa…", "checking…"),
        ["h.installed"] = ("terpasang", "installed"),
        ["h.notinstalled"] = ("belum terpasang", "not installed"),
        ["h.on"] = ("aktif", "on"),
        ["h.off"] = ("nonaktif", "off"),
        ["h.unknown"] = ("tidak diketahui (perlu admin)", "unknown (needs admin)"),
        ["h.uninstall.confirm"] = ("Lepas driver Layar 2 dan kembalikan ke satu layar? Windows mungkin minta restart.", "Remove the Display 2 driver and return to a single display? Windows may ask to reboot."),
        ["h.reboot"] = ("Perlu restart Windows agar perubahan aktif penuh.", "A Windows reboot is needed for changes to fully apply."),
    };

    public static string T(string key) =>
        S.TryGetValue(key, out var v) ? (Current == AppLang.ID ? v.id : v.en) : key;
}
