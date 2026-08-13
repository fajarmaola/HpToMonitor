package com.secondscreen.local.net

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.io.DataInputStream
import java.io.DataOutputStream
import java.net.InetSocketAddress
import java.net.Socket

// Low-level control channel (PROTOCOL.md §3.1): TCP with 4-byte big-endian length-prefixed
// frames. Before the secure phase, payloads are plaintext JSON; after, AES-256-GCM.
class ControlConnection {
    private var socket: Socket? = null
    private var input: DataInputStream? = null
    private var output: DataOutputStream? = null

    @Volatile var sessionKey: ByteArray? = null   // null => handshake (plaintext)
    @Volatile var secure: Boolean = false

    val isConnected: Boolean get() = socket?.isConnected == true && socket?.isClosed == false

    suspend fun connect(host: String, port: Int, timeoutMs: Int = 4000) = withContext(Dispatchers.IO) {
        val s = Socket()
        s.tcpNoDelay = true
        s.connect(InetSocketAddress(host, port), timeoutMs)
        socket = s
        input = DataInputStream(s.getInputStream())
        output = DataOutputStream(s.getOutputStream())
    }

    // Send a JSON message. Encrypts if secure && sessionKey present.
    suspend fun send(message: JSONObject) = withContext(Dispatchers.IO) {
        val json = message.toString().toByteArray(Charsets.UTF_8)
        val payload = if (secure && sessionKey != null) Crypto.encrypt(sessionKey!!, json) else json
        val out = output ?: return@withContext
        synchronized(out) {
            out.writeInt(payload.size)   // big-endian
            out.write(payload)
            out.flush()
        }
    }

    // Blocking read of one frame; returns decoded JSON text or null on EOF/close.
    fun readFrame(): String? {
        val ins = input ?: return null
        val len = try { ins.readInt() } catch (_: Exception) { return null }
        if (len <= 0 || len > 8 * 1024 * 1024) return null
        val buf = ByteArray(len)
        ins.readFully(buf)
        val json = if (secure && sessionKey != null) Crypto.decrypt(sessionKey!!, buf) else buf
        return String(json, Charsets.UTF_8)
    }

    fun close() {
        try { socket?.close() } catch (_: Exception) {}
        socket = null; input = null; output = null
    }
}
