package com.secondscreen.local.net

import android.content.Context
import com.secondscreen.local.shared.MessageType
import com.secondscreen.local.shared.Protocol
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import org.json.JSONObject
import java.security.KeyPair
import java.util.UUID

enum class ClientState { Idle, Discovering, Connecting, Pairing, AwaitingPin, Configuring, Streaming, Reconnecting, Disconnected, Error }

data class SessionConfig(
    val codec: String, val width: Int, val height: Int, val fps: Int,
    val bitrateKbps: Int, val videoPort: Int, val encryptVideo: Boolean
)

// Client-side orchestrator (mirror of the Windows SessionManager). Runs the handshake,
// pairing, session config, heartbeat, and routes touch/stats. Exposes state + config + PIN
// requests as flows for the UI.
class ConnectionManager(private val appContext: Context) {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val control = ControlConnection()
    private lateinit var keyPair: KeyPair
    private var sharedSecret: ByteArray? = null
    private var trusted = false
    val deviceId: String = UUID.randomUUID().toString()

    private val _state = MutableStateFlow(ClientState.Idle)
    val state: StateFlow<ClientState> = _state
    private val _error = MutableStateFlow<String?>(null)
    val error: StateFlow<String?> = _error
    private val _config = MutableStateFlow<SessionConfig?>(null)
    val config: StateFlow<SessionConfig?> = _config
    private val _needPin = MutableStateFlow(false)
    val needPin: StateFlow<Boolean> = _needPin
    val latencyMs = MutableStateFlow(0.0)

    // Continuation for the PIN the user types.
    @Volatile private var pinDeferred: CompletableDeferred<String>? = null
    @Volatile private var host: HostPeer? = null

    fun submitPin(pin: String) { pinDeferred?.complete(pin) }

    fun connect(peer: HostPeer) {
        host = peer
        scope.launch {
            try {
                _state.value = ClientState.Connecting
                control.connect(peer.ip, peer.controlPort)
                keyPair = Crypto.createEcdhKeyPair()
                startReadLoop()
                sendHello()
            } catch (e: Exception) {
                fail("Connection failed: ${e.message}")
            }
        }
    }

    private suspend fun sendHello() {
        val hello = JSONObject()
            .put("type", MessageType.HELLO)
            .put("v", Protocol.VERSION)
            .put("device", DeviceCapabilities.deviceInfoJson(appContext))
            .put("caps", DeviceCapabilities.capabilitiesJson())
            .put("pubKey", android.util.Base64.encodeToString(
                Crypto.publicKeySpki(keyPair), android.util.Base64.NO_WRAP))
        control.send(hello)
        _state.value = ClientState.Pairing
    }

    private fun startReadLoop() {
        scope.launch {
            while (isActive) {
                val text = withContext(Dispatchers.IO) { control.readFrame() }
                if (text == null) { onSocketClosed(); break }
                try { route(JSONObject(text)) }
                catch (e: Exception) { android.util.Log.w("SSL", "route error: ${e.message}") }
            }
        }
    }

    private suspend fun route(msg: JSONObject) {
        when (msg.optString("type")) {
            MessageType.HELLO_ACK -> handleHelloAck(msg)
            MessageType.PAIR_OK -> handlePairOk()
            MessageType.PAIR_FAIL -> fail("Pairing failed (wrong PIN?)")
            MessageType.SESSION_CONFIG -> handleSessionConfig(msg)
            MessageType.PING -> control.send(JSONObject().put("type", MessageType.PONG).put("ts", msg.optLong("ts")))
            MessageType.PONG -> { /* client rarely pings; host drives heartbeat */ }
            MessageType.SET_QUALITY -> { /* decoder auto-adapts to incoming stream */ }
            MessageType.DISCONNECT -> fail("Host disconnected: ${msg.optString("reason")}")
        }
    }

    private suspend fun handleHelloAck(msg: JSONObject) {
        val peerSpki = android.util.Base64.decode(msg.getString("pubKey"), android.util.Base64.DEFAULT)
        sharedSecret = Crypto.deriveSharedSecret(keyPair, peerSpki)
        trusted = msg.optBoolean("trusted", false)

        if (trusted) {
            control.sessionKey = Crypto.deriveTrustedKey(sharedSecret!!)
        } else {
            _needPin.value = true
            _state.value = ClientState.AwaitingPin
            val deferred = CompletableDeferred<String>()
            pinDeferred = deferred
            val pin = deferred.await()          // suspends until UI provides the PIN
            _needPin.value = false
            control.sessionKey = Crypto.deriveSessionKey(sharedSecret!!, pin)
        }
        control.secure = true                   // everything after HELLO_ACK is encrypted

        // Confirm: if our derived key matches the host's, this frame decrypts on their side.
        val token = UUID.randomUUID().toString().replace("-", "")
        control.send(JSONObject().put("type", MessageType.PAIR_CONFIRM).put("token", token))
        _state.value = ClientState.Configuring
    }

    private fun handlePairOk() {
        android.util.Log.i("SSL", "Paired OK")
    }

    private suspend fun handleSessionConfig(msg: JSONObject) {
        val cfg = SessionConfig(
            codec = msg.optString("codec", "h264"),
            width = msg.getInt("width"),
            height = msg.getInt("height"),
            fps = msg.optInt("fps", 60),
            bitrateKbps = msg.optInt("bitrateKbps", 8000),
            videoPort = msg.optInt("videoPort", Protocol.VIDEO_UDP_PORT),
            encryptVideo = msg.optBoolean("encryptVideo", true)
        )
        _config.value = cfg

        // Reply with the UDP port we will listen on for video.
        control.send(JSONObject()
            .put("type", MessageType.SESSION_CONFIG_ACK)
            .put("codec", cfg.codec)
            .put("videoPort", cfg.videoPort))

        _state.value = ClientState.Streaming
        startHeartbeatSender()
    }

    // Client sends periodic device updates + stats; host drives PING but we also measure RTT
    // by echoing. Here we push STATS/DEVICE_UPDATE for the Windows dashboard overlay.
    private fun startHeartbeatSender() {
        scope.launch {
            while (isActive && _state.value == ClientState.Streaming) {
                try {
                    control.send(JSONObject()
                        .put("type", MessageType.DEVICE_UPDATE)
                        .put("battery", batteryLevel())
                        .put("refreshHz", 60))
                } catch (_: Exception) {}
                delay(3000)
            }
        }
    }

    private fun batteryLevel(): Int = try {
        val bm = appContext.getSystemService(Context.BATTERY_SERVICE) as android.os.BatteryManager
        bm.getIntProperty(android.os.BatteryManager.BATTERY_PROPERTY_CAPACITY)
    } catch (_: Exception) { -1 }

    fun sessionKey(): ByteArray? = control.sessionKey

    fun sendTouch(pointerId: Int, x: Float, y: Float, event: String, dx: Float = 0f, dy: Float = 0f) {
        scope.launch {
            try {
                control.send(JSONObject()
                    .put("type", MessageType.TOUCH)
                    .put("deviceId", deviceId)
                    .put("pointerId", pointerId)
                    .put("x", x.toDouble())
                    .put("y", y.toDouble())
                    .put("dx", dx.toDouble())
                    .put("dy", dy.toDouble())
                    .put("ts", System.currentTimeMillis())
                    .put("event", event))
            } catch (_: Exception) {}
        }
    }

    fun requestKeyframe() {
        scope.launch { try { control.send(JSONObject().put("type", MessageType.REQUEST_KEYFRAME)) } catch (_: Exception) {} }
    }

    fun sendStats(decodeFps: Double, renderFps: Double, dropped: Long, jitterMs: Double) {
        scope.launch {
            try {
                control.send(JSONObject()
                    .put("type", MessageType.STATS)
                    .put("decodeFps", decodeFps)
                    .put("renderFps", renderFps)
                    .put("droppedFrames", dropped)
                    .put("jitterMs", jitterMs))
            } catch (_: Exception) {}
        }
    }

    private fun onSocketClosed() {
        if (_state.value == ClientState.Streaming) {
            _state.value = ClientState.Reconnecting
            // Grace-window reconnect (PROTOCOL.md §3.4).
            scope.launch {
                var attempt = 0
                while (attempt < Protocol.RECONNECT_MAX_ATTEMPTS && _state.value == ClientState.Reconnecting) {
                    attempt++
                    delay(1500L * attempt)
                    val h = host ?: break
                    try {
                        control.connect(h.ip, h.controlPort)
                        control.secure = false
                        keyPair = Crypto.createEcdhKeyPair()
                        startReadLoop()
                        sendHello()
                        return@launch
                    } catch (_: Exception) {}
                }
                fail("Connection lost")
            }
        } else {
            _state.value = ClientState.Disconnected
        }
    }

    private fun fail(message: String) {
        _error.value = message
        _state.value = ClientState.Error
    }

    fun disconnect() {
        scope.launch { try { control.send(JSONObject().put("type", MessageType.DISCONNECT).put("reason", "user")) } catch (_: Exception) {} }
        control.close()
        _state.value = ClientState.Disconnected
    }

    fun shutdown() {
        control.close()
        scope.cancel()
    }
}
