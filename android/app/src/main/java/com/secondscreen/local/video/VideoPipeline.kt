package com.secondscreen.local.video

import android.view.Surface
import com.secondscreen.local.net.ConnectionManager
import com.secondscreen.local.net.SessionConfig
import kotlinx.coroutines.*

// Wires the UDP receiver to the MediaCodec decoder and periodically reports STATS back to the
// Windows host for the dashboard/overlay.
class VideoPipeline(
    private val connection: ConnectionManager,
    private val config: SessionConfig
) {
    private var receiver: VideoReceiver? = null
    private var decoder: H264Decoder? = null
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)

    val decodeFps: Double get() = decoder?.decodeFps ?: 0.0
    val droppedFrames: Long get() = receiver?.packetsLost ?: 0L
    val packetsReceived: Long get() = receiver?.packetsReceived ?: 0L
    val framesReassembled: Long get() = receiver?.framesReassembled ?: 0L
    val decodedFrames: Long get() = decoder?.decodedFrames ?: 0L

    fun start(surface: Surface) {
        val dec = H264Decoder(surface, config.width, config.height).apply { start() }
        decoder = dec

        receiver = VideoReceiver(
            port = config.videoPort,
            sessionKey = if (config.encryptVideo) connection.sessionKey() else null,
            onFrame = { unit -> dec.submit(unit) },
            onNeedKeyframe = { connection.requestKeyframe() }
        ).also { it.start() }

        // Ask for an initial keyframe so decoding can begin immediately.
        connection.requestKeyframe()
        startStatsReporter()
    }

    private fun startStatsReporter() {
        scope.launch {
            while (isActive) {
                // Until the first frame decodes, keep asking for a keyframe — the initial one may
                // have been sent before our UDP socket was listening.
                if (decodedFrames == 0L) connection.requestKeyframe()
                connection.sendStats(decodeFps, decodeFps, droppedFrames, 0.0)
                delay(1000)
            }
        }
    }

    fun stop() {
        receiver?.stop()
        decoder?.stop()
        scope.cancel()
    }
}
