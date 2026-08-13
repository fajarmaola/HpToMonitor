package com.secondscreen.local.monitor

import android.app.Activity
import android.os.Build
import android.view.View
import android.view.WindowManager
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat

// Makes the phone behave like a dedicated monitor using only Android-sanctioned mechanisms
// (immersive fullscreen, keep-screen-on, optional lock task). It does NOT try to kill other
// apps — Android security forbids that, and we don't fake it.
class MonitorModeManager(private val activity: Activity) {

    fun enter() {
        // Keep the screen on and at full brightness while acting as a display.
        activity.window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        val lp = activity.window.attributes
        lp.screenBrightness = WindowManager.LayoutParams.BRIGHTNESS_OVERRIDE_FULL
        activity.window.attributes = lp

        // Immersive: hide status + navigation bars; they reappear only on user swipe (OS policy).
        WindowCompat.setDecorFitsSystemWindows(activity.window, false)
        val controller = WindowInsetsControllerCompat(activity.window, activity.window.decorView)
        controller.hide(WindowInsetsCompat.Type.systemBars())
        controller.systemBarsBehavior =
            WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE

        // Draw under cutouts to use the whole panel.
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
            activity.window.attributes.layoutInDisplayCutoutMode =
                WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_SHORT_EDGES
        }
    }

    // Screen pinning (lock task) reduces accidental interruptions. For a non-DPC app the user
    // must confirm the system prompt; we degrade gracefully if unavailable.
    fun tryStartLockTask() {
        try { activity.startLockTask() } catch (e: Exception) {
            android.util.Log.i("SSL", "lock task not available: ${e.message}")
        }
    }

    fun exit() {
        try { activity.stopLockTask() } catch (_: Exception) {}
        activity.window.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        WindowCompat.setDecorFitsSystemWindows(activity.window, true)
        val controller = WindowInsetsControllerCompat(activity.window, activity.window.decorView)
        controller.show(WindowInsetsCompat.Type.systemBars())
    }
}
