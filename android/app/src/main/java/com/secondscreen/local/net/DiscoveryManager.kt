package com.secondscreen.local.net

import com.secondscreen.local.shared.Protocol
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.InetSocketAddress
import java.util.UUID

data class HostPeer(val name: String, val ip: String, val controlPort: Int, val paired: Boolean)

// Android side of discovery (PROTOCOL.md §2): broadcast SSL_DISCOVER, collect SSL_ANNOUNCE.
class DiscoveryManager {

    // Sends a broadcast probe and gathers announces for [durationMs]. Returns unique hosts.
    suspend fun discover(durationMs: Long = 2500): List<HostPeer> = withContext(Dispatchers.IO) {
        val found = LinkedHashMap<String, HostPeer>()
        val socket = DatagramSocket(null).apply {
            reuseAddress = true
            broadcast = true
            soTimeout = 400
            bind(InetSocketAddress(0))
        }
        try {
            val nonce = UUID.randomUUID().toString().replace("-", "")
            val probe = JSONObject()
                .put("t", Protocol.DISCOVER_TYPE)
                .put("v", Protocol.VERSION)
                .put("role", "receiver")
                .put("device", android.os.Build.MODEL)
                .put("nonce", nonce)
                .toString().toByteArray()

            val bcast = InetAddress.getByName("255.255.255.255")
            socket.send(DatagramPacket(probe, probe.size, bcast, Protocol.DISCOVERY_UDP_PORT))

            val deadline = System.currentTimeMillis() + durationMs
            val buf = ByteArray(2048)
            while (System.currentTimeMillis() < deadline) {
                try {
                    val pkt = DatagramPacket(buf, buf.size)
                    socket.receive(pkt)
                    val json = JSONObject(String(pkt.data, 0, pkt.length))
                    if (json.optString("t") == Protocol.ANNOUNCE_TYPE) {
                        val ip = json.optString("ip").ifEmpty { pkt.address.hostAddress ?: "" }
                        val host = HostPeer(
                            name = json.optString("name", "PC"),
                            ip = ip,
                            controlPort = json.optInt("controlPort", Protocol.CONTROL_TCP_PORT),
                            paired = json.optBoolean("paired", false)
                        )
                        if (ip.isNotEmpty()) found[ip] = host
                    }
                } catch (_: java.net.SocketTimeoutException) {
                    // keep waiting until deadline
                }
            }
        } finally {
            socket.close()
        }
        found.values.toList()
    }
}
