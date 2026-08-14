package com.secondscreen.local.ui

import android.content.Context
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue

enum class Lang { ID, EN }

// Tiny runtime localization for the Android app. Default = Indonesian. The choice is persisted
// in SharedPreferences and exposed as a Compose state so toggling recomposes the whole UI.
object I18n {
    var lang by mutableStateOf(Lang.ID)
        private set

    private var prefs: android.content.SharedPreferences? = null

    fun init(ctx: Context) {
        val p = ctx.getSharedPreferences("hpkemonitor", Context.MODE_PRIVATE)
        prefs = p
        lang = if (p.getString("lang", "ID") == "EN") Lang.EN else Lang.ID
    }

    fun toggle() {
        lang = if (lang == Lang.ID) Lang.EN else Lang.ID
        prefs?.edit()?.putString("lang", if (lang == Lang.EN) "EN" else "ID")?.apply()
    }

    fun toggleLabel(): String = if (lang == Lang.ID) "EN" else "ID"

    fun t(key: String): String {
        val v = S[key] ?: return key
        return if (lang == Lang.ID) v.first else v.second
    }

    private val S: Map<String, Pair<String, String>> = mapOf(
        "app.title" to Pair("HP KE MONITOR", "PHONE TO MONITOR"),
        "app.tagline" to Pair("Penerima • layar kedua offline", "Receiver • offline second display"),
        "btn.search" to Pair("Cari PC", "Search for PCs"),
        "btn.searching" to Pair("Mencari…", "Searching…"),
        "lbl.availablePcs" to Pair("PC TERSEDIA", "AVAILABLE PCs"),
        "empty.noPcs" to Pair(
            "Belum ada PC ditemukan. Pastikan aplikasi Windows berjalan di Wi-Fi yang sama, lalu tekan Cari.",
            "No PCs found yet. Make sure the Windows app runs on the same Wi-Fi, then tap Search."),
        "btn.connect" to Pair("Sambung", "Connect"),
        "dlg.pinTitle" to Pair("Masukkan kode sambungan", "Enter pairing code"),
        "dlg.pinBody" to Pair("Ketik kode 6 digit yang muncul di aplikasi Windows.", "Type the 6-digit code shown on the Windows app."),
        "btn.cancel" to Pair("Batal", "Cancel"),
        "state.ready" to Pair("SIAP", "READY"),
        "state.searching" to Pair("MENCARI", "SEARCHING"),
        "state.connecting" to Pair("MENYAMBUNG", "CONNECTING"),
        "state.pairing" to Pair("PAIRING", "PAIRING"),
        "state.configuring" to Pair("MENYIAPKAN", "CONFIGURING"),
        "state.connected" to Pair("TERSAMBUNG", "CONNECTED"),
        "state.reconnecting" to Pair("MENYAMBUNG ULANG", "RECONNECTING"),
        "state.disconnected" to Pair("TERPUTUS", "DISCONNECTED"),
        "state.error" to Pair("ERROR", "ERROR"),
        "conn.title" to Pair("Tersambung! \uD83C\uDF89", "Connected! \uD83C\uDF89"),
        "conn.subtitle" to Pair("HP kamu siap jadi Layar 2 PC.", "Your phone is ready as PC Display 2."),
        "conn.step1" to Pair("Di PC, geser jendela ke layar baru ini (atau atur di Pengaturan Layar).",
            "On the PC, drag a window onto this new display (or arrange it in Display Settings)."),
        "conn.step2" to Pair("Sentuh layar untuk menggerakkan mouse & klik.", "Touch the screen to move the mouse & click."),
        "conn.step3" to Pair("Tekan tombol Kembali untuk keluar & memutus.", "Press Back to exit & disconnect."),
        "conn.start" to Pair("Mulai Tampilkan", "Start Display"),
        "mon.connecting" to Pair("Menyambung…", "Connecting…"),
        "mon.stats" to Pair("Statistik", "Stats"),
        "btn.update" to Pair("Cek Pembaruan", "Check for updates"),
        "upd.checking" to Pair("Memeriksa pembaruan…", "Checking for updates…"),
        "upd.uptodate" to Pair("Aplikasi sudah versi terbaru.", "You're on the latest version."),
        "upd.available" to Pair("Pembaruan tersedia", "Update available"),
        "upd.now" to Pair("Perbarui Sekarang", "Update now"),
        "upd.later" to Pair("Nanti", "Later"),
        "upd.downloading" to Pair("Mengunduh pembaruan…", "Downloading update…"),
        "upd.failed" to Pair("Gagal memperbarui. Coba lagi nanti.", "Update failed. Try again later."),
        "footer" to Pair("PT Teleraya Digital Group • company.teleraya.com", "PT Teleraya Digital Group • company.teleraya.com")
    )
}
