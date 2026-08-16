package com.secondscreen.local.video

import android.media.MediaCodec
import android.media.MediaFormat
import android.view.Surface
import java.nio.ByteBuffer

// Hardware H.264 decoder rendering directly to a Surface (no CPU Bitmap decode — see
// PROTOCOL.md §5 and the problem statement's pipeline requirement).
class H264Decoder(
    private val surface: Surface,
    private val width: Int,
    private val height: Int
) {
    private var codec: MediaCodec? = null
    private var configured = false

    @Volatile var decodedFrames = 0L
    @Volatile var lastDecodeTimeMs = 0L
    private var lastFpsWindowStart = System.currentTimeMillis()
    private var framesInWindow = 0
    @Volatile var decodeFps = 0.0

    fun start() {
        val mime = MediaFormat.MIMETYPE_VIDEO_AVC
        val format = MediaFormat.createVideoFormat(mime, width, height).apply {
            setInteger(MediaFormat.KEY_MAX_INPUT_SIZE, width * height)
            // Low-latency decode where supported (API 30+). Reduces reordering buffers.
            if (android.os.Build.VERSION.SDK_INT >= 30) setInteger(MediaFormat.KEY_LOW_LATENCY, 1)
        }
        codec = MediaCodec.createDecoderByType(mime).also {
            it.configure(format, surface, null, 0)
            it.start()
        }
        configured = true
    }

    // Feed one access unit. SPS/PPS are carried in-band before the first IDR (encoder is
    // configured for that), so MediaCodec parses them from the stream.
    fun submit(unit: DecodedUnit) {
        val c = codec ?: return
        try {
            var inIndex = c.dequeueInputBuffer(10_000)
            if (inIndex < 0 && unit.keyframe) {
                // Never drop a keyframe (it may carry SPS/PPS) — retry up to ~100ms.
                var tries = 0
                while (inIndex < 0 && tries++ < 10) inIndex = c.dequeueInputBuffer(10_000)
            }
            if (inIndex >= 0) {
                val buf: ByteBuffer? = c.getInputBuffer(inIndex)
                buf?.clear()
                buf?.put(unit.data)
                val flags = if (unit.keyframe) MediaCodec.BUFFER_FLAG_KEY_FRAME else 0
                c.queueInputBuffer(inIndex, 0, unit.data.size, unit.timestampUs, flags)
            }
            drainOutput(c)
        } catch (e: IllegalStateException) {
            android.util.Log.w("SSL", "decoder submit: ${e.message}")
        }
    }

    private fun drainOutput(c: MediaCodec) {
        val info = MediaCodec.BufferInfo()
        while (true) {
            val outIndex = c.dequeueOutputBuffer(info, 0)
            if (outIndex < 0) break
            // render = true -> pushed straight to the Surface (zero-copy display path).
            c.releaseOutputBuffer(outIndex, true)
            decodedFrames++
            framesInWindow++
            val now = System.currentTimeMillis()
            if (now - lastFpsWindowStart >= 1000) {
                decodeFps = framesInWindow * 1000.0 / (now - lastFpsWindowStart)
                framesInWindow = 0
                lastFpsWindowStart = now
            }
            lastDecodeTimeMs = now
        }
    }

    fun stop() {
        try { codec?.stop() } catch (_: Exception) {}
        try { codec?.release() } catch (_: Exception) {}
        codec = null
        configured = false
    }
}
