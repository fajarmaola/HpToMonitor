# Crypto & Protocol Interop Test Vectors (v1)

Use these to confirm the C# (Windows) and Kotlin (Android) implementations produce
**byte-identical** results. Vectors were generated independently (Python `hmac`/`hashlib` +
`cryptography` AES-GCM) so both language ports can be checked against a neutral reference.

## 1. Encoding invariants (must match)
| Item | Value |
|------|-------|
| ECDH curve | P-256 / secp256r1 (`nistP256`) |
| Public key encoding | SubjectPublicKeyInfo / X.509 DER. C#: `ExportSubjectPublicKeyInfo()`, Kotlin: `PublicKey.getEncoded()` |
| ECDH shared secret | raw Z (x-coordinate), 32 bytes. C#: `DeriveRawSecretAgreement`, Kotlin: `KeyAgreement("ECDH").generateSecret()` |
| KDF | HKDF-SHA256 (RFC 5869), output 32 bytes |
| HKDF salt (session) | ASCII bytes of the 6-digit PIN |
| HKDF salt (trusted) | empty → treated as 32 zero bytes |
| HKDF info (session) | `SecondScreenLocal/v1/session` |
| HKDF info (trusted) | `SecondScreenLocal/v1/session/trusted` |
| AEAD | AES-256-GCM, 12-byte nonce, 16-byte tag |
| Control frame layout | `[nonce(12)][ciphertext][tag(16)]` |
| Video packet payload (encrypted) | same layout; nonce = `frameId(4 BE) ‖ packetIndex(2 BE) ‖ zeros(6)` |
| Wire integers | big-endian everywhere (TCP length prefix, video header, JSON numbers) |

## 2. HKDF-SHA256 vector (session key)
```
IKM (Z)   = 000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f
salt(PIN) = "482771"
info      = "SecondScreenLocal/v1/session"
=> SESSION_KEY (32B) = 34758394738a5f6a28968ed494eb58f8f041a289758e8f76b5bdf7c6810a96b3
```

## 3. HKDF-SHA256 vector (trusted-reconnect key)
```
IKM (Z)   = 000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f
salt      = (empty)
info      = "SecondScreenLocal/v1/session/trusted"
=> TRUSTED_KEY (32B) = c22fe8af2f93d94fb85d0f69d273a371c3e73a560fd0012c805513a1a8ddb42e
```

## 4. AES-256-GCM vector
```
key       = 34758394738a5f6a28968ed494eb58f8f041a289758e8f76b5bdf7c6810a96b3  (SESSION_KEY above)
nonce     = 000000010000000000000000            (frameId=1, packetIndex=0)
plaintext = "SecondScreen"  (ASCII)
ciphertext‖tag = e61dab2cd74470940e519d5115653563c4630fa1106bd3ee2b688106
full wire frame [nonce‖ct‖tag] =
  000000010000000000000000e61dab2cd74470940e519d5115653563c4630fa1106bd3ee2b688106
```

## 5. How to check
- **C#**: `CryptoUtil.DeriveSessionKey(ikm, "482771")` must equal `SESSION_KEY`.
  `CryptoUtil.EncryptWithNonce(key, nonce, "SecondScreen")` must equal the full wire frame.
- **Kotlin**: `Crypto.deriveSessionKey(ikm, "482771")` must equal `SESSION_KEY`.
  `Crypto.encrypt(...)` uses a random nonce, so to reproduce the vector inject the fixed nonce
  in a unit test (or verify round-trip: `decrypt(key, wireFrame) == plaintext`).

Since ECDH keys are random per session, the ECDH step is verified by the round-trip
(both sides deriving the same `SESSION_KEY` from the same PIN) rather than a fixed vector.
