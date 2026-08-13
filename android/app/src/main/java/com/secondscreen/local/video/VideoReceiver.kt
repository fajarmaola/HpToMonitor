package com.secondscreen.local.video

import com.secondscreen.local.net.Crypto
import com.secondscreen.local.shared.Protocol
import kotlinx.coroutines.*
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetSocketAddress
import java.nio.ByteBuffer

// One reassembled H.264 access unit (frame).
class DecodedUnit(val data: ByteArray, val timestampUs: Long, val keyframe: Boolean)

// Receives UDP video packets (PROTOCOL.md §5), decrypts (if enabled) and reassembles frames.
// Complete access units are pushed to [onFrame]; missing packets trigger [onNeedKeyframe].
class VideoReceiver(
    private val port: Int,
    private val sessionKey: ByteArray?,
    private val onFrame: (DecodedUnit) -> Unit,
    private val onNeedKeyframe: () -> Unit
) {
    private var socket: DatagramSocket? = null
    private var job: Job? = null
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

    // Reassembly state for the in-flight frame.
    private var curFrameId = -1L
    private var expectedCount = 0
    private var receivedCount = 0
    private var curKeyframe = false
    private var curTsUs = 0L
    private var chunks = arrayOfNulls<ByteArray>(0)

    @Volatile var packetsLost = 0L

    fun start() {
        val s = DatagramSocket(null).apply {
            reuseAddress = true
            receiveBufferSize = 4 * 1024 * 1024
            bind(InetSocketAddress(port))
        }
        socket = s
        job = scope.launch {
            val buf = ByteArray(1500)
            while (isActive) {
                try {
                    val pkt = DatagramPacket(buf, buf.size)
                    s.receive(pkt)
                    handlePacket(pkt.data, pkt.length)
                } catch (e: Exception) {
                    if (isActive) android.util.Log.w("SSL", "video recv: ${e.message}")
                }
            }
        }
    }

    private fun handlePacket(data: ByteArray, len: Int) {
        if (len < Protocol.VIDEO_HEADER_BYTES) return
        val bb = ByteBuffer.wrap(data, 0, len)
        if (bb.get() != Protocol.VIDEO_MAGIC[0] || bb.get() != Protocol.VIDEO_MAGIC[1]) return
        bb.get() // version
        val flags = bb.get().toInt() and 0xFF
        val frameId = (bb.int.toLong() and 0xFFFFFFFFL)
        val packetIndex = bb.short.toInt() and 0xFFFF
        val packetCount = bb.short.toInt() and 0xFFFF
        val tsUs = bb.long
        val payloadLen = bb.short.toInt() and 0xFFFF
        if (payloadLen <= 0 || bb.remaining() < payloadLen) return

        var payload = ByteArray(payloadLen)
        bb.get(payload)
        if ((flags and Protocol.FLAG_ENCRYPTED) != 0 && sessionKey != null) {
            payload = try { Crypto.decryptVideo(sessionKey, payload) }
                      catch (e: Exception) { return } // auth fail => drop
        }

        val keyframe = (flags and Protocol.FLAG_KEYFRAME) != 0

        // New frame started before finishing the previous one => previous frame incomplete.
        if (frameId != curFrameId) {
            if (curFrameId != -1L && receivedCount < expectedCount) {
                packetsLost++
                onNeedKeyframe()
            }
            curFrameId = frameId
            expectedCount = packetCount
            receivedCount = 0
            curKeyframe = keyframe
            curTsUs = tsUs
            chunks = arrayOfNulls(packetCount)
        }

        if (packetIndex < chunks.size && chunks[packetIndex] == null) {
            chunks[packetIndex] = payload
            receivedCount++
        }

        if (receivedCount == expectedCount && expectedCount > 0) {
            val total = chunks.sumOf { it?.size ?: 0 }
            val frame = ByteArray(total)
            var off = 0
            for (c in chunks) { if (c != null) { System.arraycopy(c, 0, frame, off, c.size); off += c.size } }
            onFrame(DecodedUnit(frame, curTsUs, curKeyframe))
            curFrameId = -1L
        }
    }

    fun stop() {
        job?.cancel()
        try { socket?.close() } catch (_: Exception) {}
        scope.cancel()
    }
}
