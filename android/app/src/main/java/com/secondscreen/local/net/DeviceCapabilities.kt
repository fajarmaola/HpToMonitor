package com.secondscreen.local.net

import android.content.Context
import android.os.BatteryManager
import android.util.DisplayMetrics
import android.view.WindowManager
import org.json.JSONObject
import com.secondscreen.local.shared.Protocol

// Gathers this device's display + capability info for the HELLO message (PROTOCOL.md §3.2).
object DeviceCapabilities {

    fun deviceInfoJson(ctx: Context): JSONObject {
        val wm = ctx.getSystemService(Context.WINDOW_SERVICE) as WindowManager
        val metrics = DisplayMetrics()
        @Suppress("DEPRECATION")
        wm.defaultDisplay.getRealMetrics(metrics)
        val refresh = try {
            @Suppress("DEPRECATION")
            wm.defaultDisplay.refreshRate.toInt()
        } catch (_: Exception) { 60 }

        val bm = ctx.getSystemService(Context.BATTERY_SERVICE) as BatteryManager
        val battery = bm.getIntProperty(BatteryManager.BATTERY_PROPERTY_CAPACITY)

        return JSONObject()
            .put("name", "${android.os.Build.MANUFACTURER} ${android.os.Build.MODEL}")
            .put("os", "Android ${android.os.Build.VERSION.RELEASE}")
            .put("width", metrics.widthPixels)
            .put("height", metrics.heightPixels)
            .put("refreshHz", refresh)
            .put("battery", battery)
    }

    fun capabilitiesJson(): JSONObject =
        JSONObject()
            .put("codecs", org.json.JSONArray(listOf("h264")))
            .put("maxBitrateKbps", 20000)
            .put("hwDecode", true)

    fun screenWidth(ctx: Context): Int {
        val wm = ctx.getSystemService(Context.WINDOW_SERVICE) as WindowManager
        val m = DisplayMetrics()
        @Suppress("DEPRECATION")
        wm.defaultDisplay.getRealMetrics(m)
        return m.widthPixels
    }

    fun screenHeight(ctx: Context): Int {
        val wm = ctx.getSystemService(Context.WINDOW_SERVICE) as WindowManager
        val m = DisplayMetrics()
        @Suppress("DEPRECATION")
        wm.defaultDisplay.getRealMetrics(m)
        return m.heightPixels
    }
}
