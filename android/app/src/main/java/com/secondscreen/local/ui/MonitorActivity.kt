package com.secondscreen.local.ui

import android.graphics.Color
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.view.Gravity
import android.view.SurfaceHolder
import android.view.SurfaceView
import android.view.View
import android.widget.Button
import android.widget.FrameLayout
import android.widget.TextView
import androidx.activity.ComponentActivity
import com.secondscreen.local.Session
import com.secondscreen.local.input.TouchInputManager
import com.secondscreen.local.monitor.MonitorModeManager
import com.secondscreen.local.net.ClientState
import com.secondscreen.local.service.MonitorService
import com.secondscreen.local.video.VideoPipeline
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch

// MONITOR MODE: full-screen receiver. Renders the Windows stream straight to a SurfaceView,
// forwards touch to Windows, and shows an optional diagnostics overlay.
class MonitorActivity : ComponentActivity() {

    private lateinit var monitorMode: MonitorModeManager
    private lateinit var surfaceView: SurfaceView
    private lateinit var overlay: TextView
    private var overlayVisible = true
    private var pipeline: VideoPipeline? = null
    private val ui = Handler(Looper.getMainLooper())
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main)

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        monitorMode = MonitorModeManager(this)

        val root = FrameLayout(this).apply { setBackgroundColor(Color.BLACK) }
        surfaceView = SurfaceView(this)
        root.addView(surfaceView, FrameLayout.LayoutParams(
            FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT))

        overlay = TextView(this).apply {
            setTextColor(Color.parseColor("#2ED47A"))
            setBackgroundColor(Color.parseColor("#99000000"))
            textSize = 12f
            setPadding(20, 16, 20, 16)
            text = "Connecting…"
        }
        root.addView(overlay, FrameLayout.LayoutParams(
            FrameLayout.LayoutParams.WRAP_CONTENT, FrameLayout.LayoutParams.WRAP_CONTENT).apply {
            gravity = Gravity.TOP or Gravity.START; topMargin = 40; leftMargin = 40
        })

        val toggle = Button(this).apply {
            text = "Stats"
            alpha = 0.4f
            setOnClickListener { toggleOverlay() }
        }
        root.addView(toggle, FrameLayout.LayoutParams(
            FrameLayout.LayoutParams.WRAP_CONTENT, FrameLayout.LayoutParams.WRAP_CONTENT).apply {
            gravity = Gravity.TOP or Gravity.END; topMargin = 20; rightMargin = 20
        })

        setContentView(root)

        val connection = Session.connection
        val config = connection?.config?.value
        if (connection == null || config == null) { finish(); return }

        surfaceView.holder.addCallback(object : SurfaceHolder.Callback {
            override fun surfaceCreated(holder: SurfaceHolder) {
                pipeline = VideoPipeline(connection, config).also {
                    Session.pipeline = it
                    it.start(holder.surface)
                }
                TouchInputManager(surfaceView, connection).attach()
            }
            override fun surfaceChanged(h: SurfaceHolder, f: Int, w: Int, ht: Int) {}
            override fun surfaceDestroyed(holder: SurfaceHolder) { pipeline?.stop() }
        })

        MonitorService.start(this)
        observeState(connection)
        startOverlayUpdates(connection, config)
    }

    override fun onResume() {
        super.onResume()
        monitorMode.enter()
        monitorMode.tryStartLockTask()
    }

    private fun toggleOverlay() {
        overlayVisible = !overlayVisible
        overlay.visibility = if (overlayVisible) View.VISIBLE else View.GONE
    }

    private fun observeState(connection: com.secondscreen.local.net.ConnectionManager) {
        scope.launch {
            connection.state.collect { st ->
                if (st == ClientState.Disconnected || st == ClientState.Error) {
                    finish()
                }
            }
        }
    }

    private fun startOverlayUpdates(connection: com.secondscreen.local.net.ConnectionManager,
                                    config: com.secondscreen.local.net.SessionConfig) {
        val runnable = object : Runnable {
            override fun run() {
                val fps = pipeline?.decodeFps ?: 0.0
                val dropped = pipeline?.droppedFrames ?: 0L
                val lat = connection.latencyMs.value
                overlay.text = buildString {
                    append("FPS: ${"%.0f".format(fps)}\n")
                    append("Latency: ${"%.0f".format(lat)} ms\n")
                    append("Codec: ${config.codec.uppercase()}\n")
                    append("Resolution: ${config.width}x${config.height}\n")
                    append("Dropped: $dropped\n")
                    append("Encrypted: ${if (config.encryptVideo) "yes" else "no"}")
                }
                ui.postDelayed(this, 1000)
            }
        }
        ui.post(runnable)
    }

    override fun onDestroy() {
        monitorMode.exit()
        MonitorService.stop(this)
        pipeline?.stop()
        Session.pipeline = null
        super.onDestroy()
    }

    @Deprecated("Back exits monitor mode and disconnects")
    override fun onBackPressed() {
        Session.connection?.disconnect()
        super.onBackPressed()
    }
}
