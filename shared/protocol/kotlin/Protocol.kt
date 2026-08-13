// SecondScreen Local — Wire Protocol v1 (Kotlin mirror)
// Canonical spec: /shared/protocol/PROTOCOL.md. Keep the three language mirrors in sync.
package com.secondscreen.local.shared

object Protocol {
    const val VERSION = 1

    // Ports (PROTOCOL.md §1)
    const val DISCOVERY_UDP_PORT = 47800
    const val CONTROL_TCP_PORT = 47801
    const val VIDEO_UDP_PORT = 47802

    // Discovery payload types
    const val DISCOVER_TYPE = "SSL_DISCOVER"
    const val ANNOUNCE_TYPE = "SSL_ANNOUNCE"

    // Crypto (PROTOCOL.md §3.2)
    const val EC_CURVE = "secp256r1"          // P-256
    const val EC_ALGORITHM = "EC"
    const val KEY_AGREEMENT = "ECDH"
    const val HKDF_INFO = "SecondScreenLocal/v1/session"
    const val SESSION_KEY_BYTES = 32          // AES-256
    const val GCM_NONCE_BYTES = 12
    const val GCM_TAG_BITS = 128
    const val PIN_DIGITS = 6

    // Heartbeat / reconnect
    const val HEARTBEAT_INTERVAL_MS = 1000L
    const val MISSED_PONGS_TO_DROP = 3
    const val RECONNECT_MAX_ATTEMPTS = 5
    const val RECONNECT_GRACE_MS = 15000L

    // Video packet header (PROTOCOL.md §5)
    val VIDEO_MAGIC = byteArrayOf(0x53, 0x56) // 'S','V'
    const val VIDEO_HEADER_BYTES = 22
    const val VIDEO_MAX_PAYLOAD = 1200

    // flags
    const val FLAG_KEYFRAME: Int = 0x01
    const val FLAG_LAST_PACKET: Int = 0x02
    const val FLAG_ENCRYPTED: Int = 0x04
}

object MessageType {
    const val HELLO = "HELLO"
    const val HELLO_ACK = "HELLO_ACK"
    const val PAIR_CONFIRM = "PAIR_CONFIRM"
    const val PAIR_OK = "PAIR_OK"
    const val PAIR_FAIL = "PAIR_FAIL"
    const val SESSION_CONFIG = "SESSION_CONFIG"
    const val SESSION_CONFIG_ACK = "SESSION_CONFIG_ACK"
    const val PING = "PING"
    const val PONG = "PONG"
    const val TOUCH = "TOUCH"
    const val REQUEST_KEYFRAME = "REQUEST_KEYFRAME"
    const val STATS = "STATS"
    const val DEVICE_UPDATE = "DEVICE_UPDATE"
    const val SET_QUALITY = "SET_QUALITY"
    const val DISCONNECT = "DISCONNECT"
}

object TouchEvent {
    const val DOWN = "DOWN"
    const val MOVE = "MOVE"
    const val UP = "UP"
    const val SCROLL = "SCROLL"
    const val LONG_PRESS = "LONG_PRESS"
}
