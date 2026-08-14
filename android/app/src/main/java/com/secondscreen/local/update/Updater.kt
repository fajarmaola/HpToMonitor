package com.secondscreen.local.update

import android.content.Context
import android.content.Intent
import android.net.Uri
import androidx.core.content.FileProvider
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.io.File
import java.net.HttpURLConnection
import java.net.URL

data class ReleaseInfo(
    val available: Boolean,
    val latestVersion: String,
    val currentVersion: String,
    val apkUrl: String?,
    val notes: String,
    val message: String
)

// Checks GitHub Releases for a newer APK and installs it. Internet is used ONLY here (on demand);
// everything else in the app is offline/LAN.
object Updater {
    private const val REPO_OWNER = "fajarmaola"
    private const val REPO_NAME = "HpToMonitor"

    suspend fun check(currentVersion: String): ReleaseInfo = withContext(Dispatchers.IO) {
        try {
            val api = "https://api.github.com/repos/$REPO_OWNER/$REPO_NAME/releases/latest"
            val json = httpGet(api) ?: return@withContext fail(currentVersion, "Tidak dapat menghubungi GitHub.")
            val root = JSONObject(json)
            var apkUrl: String? = null
            var versionJsonUrl: String? = null
            val assets = root.optJSONArray("assets")
            if (assets != null) {
                for (i in 0 until assets.length()) {
                    val a = assets.getJSONObject(i)
                    val name = a.optString("name")
                    val dl = a.optString("browser_download_url")
                    if (name.endsWith(".apk", true)) apkUrl = dl
                    else if (name.equals("version.json", true)) versionJsonUrl = dl
                }
            }
            var latest = root.optString("tag_name", "")
            var notes = ""
            if (versionJsonUrl != null) {
                val vj = httpGet(versionJsonUrl)
                if (vj != null) {
                    val vo = JSONObject(vj)
                    latest = vo.optString("version", latest)
                    notes = vo.optString("notes", "")
                }
            }
            latest = latest.trimStart('v', 'V')
            val available = compare(latest, currentVersion) > 0 && apkUrl != null
            ReleaseInfo(available, latest, currentVersion, apkUrl, notes,
                if (apkUrl == null) "Aset APK tidak ditemukan di rilis." else "")
        } catch (e: Exception) {
            fail(currentVersion, "Gagal memeriksa pembaruan: ${e.message}")
        }
    }

    suspend fun download(ctx: Context, url: String): File? = withContext(Dispatchers.IO) {
        try {
            val out = File(ctx.cacheDir, "HPkeMonitor-update.apk")
            if (out.exists()) out.delete()
            val conn = URL(url).openConnection() as HttpURLConnection
            conn.instanceFollowRedirects = true
            conn.connectTimeout = 20000
            conn.readTimeout = 60000
            conn.inputStream.use { input -> out.outputStream().use { input.copyTo(it) } }
            conn.disconnect()
            out
        } catch (e: Exception) { null }
    }

    fun install(ctx: Context, apk: File) {
        val uri: Uri = FileProvider.getUriForFile(ctx, ctx.packageName + ".fileprovider", apk)
        val intent = Intent(Intent.ACTION_VIEW).apply {
            setDataAndType(uri, "application/vnd.android.package-archive")
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_ACTIVITY_NEW_TASK)
        }
        ctx.startActivity(intent)
    }

    private fun fail(cur: String, msg: String) = ReleaseInfo(false, "", cur, null, "", msg)

    private fun httpGet(urlStr: String): String? {
        return try {
            val conn = URL(urlStr).openConnection() as HttpURLConnection
            conn.instanceFollowRedirects = true
            conn.connectTimeout = 15000
            conn.readTimeout = 20000
            conn.setRequestProperty("User-Agent", "HPkeMonitor-Updater")
            conn.setRequestProperty("Accept", "application/vnd.github+json")
            val code = conn.responseCode
            if (code in 200..299) conn.inputStream.bufferedReader().use { it.readText() } else null
        } catch (e: Exception) { null }
    }

    private fun compare(a: String, b: String): Int {
        val pa = parse(a); val pb = parse(b)
        for (i in 0 until 4) {
            val x = pa.getOrElse(i) { 0 }; val y = pb.getOrElse(i) { 0 }
            if (x != y) return x.compareTo(y)
        }
        return 0
    }

    private fun parse(v: String): List<Int> {
        val list = mutableListOf<Int>()
        for (p in v.split('.', '-', '+', ' ')) {
            val n = p.toIntOrNull()
            if (n != null) list.add(n) else if (list.isNotEmpty()) break
        }
        return list
    }
}
