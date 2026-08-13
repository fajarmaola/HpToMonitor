package com.secondscreen.local

import com.secondscreen.local.net.Crypto
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Test

// Validates the Kotlin crypto against shared/protocol/TEST_VECTORS.md — the SAME vectors the
// C# tests use — proving byte-identical interop between Android and Windows.
class CryptoInteropTest {

    private fun ikm() = ByteArray(32) { it.toByte() }
    private fun hex(b: ByteArray) = b.joinToString("") { "%02x".format(it) }
    private fun unhex(s: String) = ByteArray(s.length / 2) { s.substring(it * 2, it * 2 + 2).toInt(16).toByte() }

    @Test
    fun sessionKey_matches_vector() {
        val key = Crypto.deriveSessionKey(ikm(), "482771")
        assertEquals("34758394738a5f6a28968ed494eb58f8f041a289758e8f76b5bdf7c6810a96b3", hex(key))
    }

    @Test
    fun trustedKey_matches_vector() {
        val key = Crypto.deriveTrustedKey(ikm())
        assertEquals("c22fe8af2f93d94fb85d0f69d273a371c3e73a560fd0012c805513a1a8ddb42e", hex(key))
    }

    @Test
    fun decrypt_wireFrame_produced_by_csharp() {
        // The exact [nonce|ciphertext|tag] bytes a Windows/C# host would send.
        val key = unhex("34758394738a5f6a28968ed494eb58f8f041a289758e8f76b5bdf7c6810a96b3")
        val wire = unhex("000000010000000000000000e61dab2cd74470940e519d5115653563c4630fa1106bd3ee2b688106")
        val pt = Crypto.decrypt(key, wire)
        assertEquals("SecondScreen", String(pt, Charsets.US_ASCII))
    }

    @Test
    fun aesgcm_roundtrip() {
        val key = ByteArray(32) { (it * 7).toByte() }
        val pt = "hello secondscreen 123".toByteArray()
        assertArrayEquals(pt, Crypto.decrypt(key, Crypto.encrypt(key, pt)))
    }

    @Test
    fun ecdh_both_sides_derive_same_session_key() {
        val a = Crypto.createEcdhKeyPair()
        val b = Crypto.createEcdhKeyPair()
        val za = Crypto.deriveSharedSecret(a, Crypto.publicKeySpki(b))
        val zb = Crypto.deriveSharedSecret(b, Crypto.publicKeySpki(a))
        assertArrayEquals(za, zb)
        assertArrayEquals(Crypto.deriveSessionKey(za, "123456"), Crypto.deriveSessionKey(zb, "123456"))
        assertNotEquals(hex(Crypto.deriveSessionKey(za, "123456")), hex(Crypto.deriveSessionKey(zb, "654321")))
    }
}
