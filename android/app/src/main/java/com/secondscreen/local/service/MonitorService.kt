package com.secondscreen.local.service

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.Context
import android.content.Intent
import android.os.Build
import android.os.IBinder
import android.os.PowerManager

// Foreground service that keeps SecondScreen alive while acting as a display (WAKE_LOCK +
// ongoing notification). Uses the connectedDevice foreground service type.
class MonitorService : Service() {
    private var wakeLock: PowerManager.WakeLock? = null

    override fun onCreate() {
        super.onCreate()
        val pm = getSystemService(Context.POWER_SERVICE) as PowerManager
        wakeLock = pm.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "SecondScreen:monitor").apply {
            setReferenceCounted(false)
            acquire(6 * 60 * 60 * 1000L) // safety cap 6h
        }
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        startForeground(NOTIF_ID, buildNotification())
        return START_STICKY
    }

    private fun buildNotification(): Notification {
        val channelId = "ssl_monitor"
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val nm = getSystemService(NotificationManager::class.java)
            if (nm.getNotificationChannel(channelId) == null) {
                nm.createNotificationChannel(
                    NotificationChannel(channelId, "Monitor Mode", NotificationManager.IMPORTANCE_LOW)
                )
            }
        }
        val builder = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O)
            Notification.Builder(this, channelId) else @Suppress("DEPRECATION") Notification.Builder(this)
        return builder
            .setContentTitle("SecondScreen Local")
            .setContentText("Acting as a second display")
            .setSmallIcon(android.R.drawable.stat_sys_data_bluetooth) // placeholder system icon
            .setOngoing(true)
            .build()
    }

    override fun onDestroy() {
        try { wakeLock?.release() } catch (_: Exception) {}
        super.onDestroy()
    }

    override fun onBind(intent: Intent?): IBinder? = null

    companion object {
        private const val NOTIF_ID = 42
        fun start(ctx: Context) {
            val i = Intent(ctx, MonitorService::class.java)
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) ctx.startForegroundService(i)
            else ctx.startService(i)
        }
        fun stop(ctx: Context) { ctx.stopService(Intent(ctx, MonitorService::class.java)) }
    }
}
