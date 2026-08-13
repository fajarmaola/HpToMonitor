package com.secondscreen.local.net

import com.secondscreen.local.shared.Protocol
import java.security.KeyFactory
import java.security.KeyPair
import java.security.KeyPairGenerator
import java.security.SecureRandom
import java.security.spec.ECGenParameterSpec
import java.security.spec.X509EncodedKeySpec
import javax.crypto.Cipher
import javax.crypto.KeyAgreement
import javax.crypto.Mac
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec

// Mirror of the Windows CryptoUtil (PROTOCOL.md §3.2): P-256 ECDH + HKDF-SHA256 + AES-256-GCM.
// Uses only the JDK/Android crypto providers — no third-party crypto, no cloud.
object Crypto {
    private val rng = SecureRandom()

    fun createEcdhKeyPair(): KeyPair {
        val kpg = KeyPairGenerator.getInstance(Protocol.EC_ALGORITHM)
        kpg.initialize(ECGenParameterSpec(Protocol.EC_CURVE))
        return kpg.generateKeyPair()
    }

    // SPKI (X.509 SubjectPublicKeyInfo) bytes — matches C# ExportSubjectPublicKeyInfo.
    fun publicKeySpki(kp: KeyPair): ByteArray = kp.public.encoded

    fun deriveSharedSecret(kp: KeyPair, peerSpki: ByteArray): ByteArray {
        val peerKey = KeyFactory.getInstance(Protocol.EC_ALGORITHM)
            .generatePublic(X509EncodedKeySpec(peerSpki))
        val ka = KeyAgreement.getInstance(Protocol.KEY_AGREEMENT)
        ka.init(kp.private)
        ka.doPhase(peerKey, true)
        return ka.generateSecret() // raw Z (x-coordinate), 32 bytes for P-256
    }

    private fun hkdf(ikm: ByteArray, salt: ByteArray, info: ByteArray, len: Int): ByteArray {
        val mac = Mac.getInstance("HmacSHA256")
        // Extract
        val realSalt = if (salt.isEmpty()) ByteArray(32) else salt
        mac.init(SecretKeySpec(realSalt, "HmacSHA256"))
        val prk = mac.doFinal(ikm)
        // Expand
        mac.init(SecretKeySpec(prk, "HmacSHA256"))
        val out = ByteArray(len)
        var t = ByteArray(0)
        var pos = 0
        var counter = 1
        while (pos < len) {
            mac.reset()
            mac.update(t)
            mac.update(info)
            mac.update(counter.toByte())
            t = mac.doFinal()
            val n = minOf(t.size, len - pos)
            System.arraycopy(t, 0, out, pos, n)
            pos += n
            counter++
        }
        return out
    }

    fun deriveSessionKey(shared: ByteArray, pin: String): ByteArray =
        hkdf(shared, pin.toByteArray(Charsets.US_ASCII),
            Protocol.HKDF_INFO.toByteArray(Charsets.US_ASCII), Protocol.SESSION_KEY_BYTES)

    fun deriveTrustedKey(shared: ByteArray): ByteArray =
        hkdf(shared, ByteArray(0),
            (Protocol.HKDF_INFO + "/trusted").toByteArray(Charsets.US_ASCII), Protocol.SESSION_KEY_BYTES)

    // Output: [12-byte nonce][ciphertext + 16-byte tag]. Matches C# CryptoUtil.Encrypt.
    fun encrypt(key: ByteArray, plaintext: ByteArray): ByteArray {
        val nonce = ByteArray(Protocol.GCM_NONCE_BYTES).also { rng.nextBytes(it) }
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.ENCRYPT_MODE, SecretKeySpec(key, "AES"),
            GCMParameterSpec(Protocol.GCM_TAG_BITS, nonce))
        val ct = cipher.doFinal(plaintext) // ciphertext || tag
        return nonce + ct
    }

    fun decrypt(key: ByteArray, frame: ByteArray): ByteArray {
        val nonce = frame.copyOfRange(0, Protocol.GCM_NONCE_BYTES)
        val ct = frame.copyOfRange(Protocol.GCM_NONCE_BYTES, frame.size)
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.DECRYPT_MODE, SecretKeySpec(key, "AES"),
            GCMParameterSpec(Protocol.GCM_TAG_BITS, nonce))
        return cipher.doFinal(ct)
    }

    // Decrypt a video packet payload where the nonce is prepended by the host packetizer.
    fun decryptVideo(key: ByteArray, payloadWithNonce: ByteArray): ByteArray =
        decrypt(key, payloadWithNonce)
}
