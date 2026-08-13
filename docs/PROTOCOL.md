# SecondScreen Local — Wire Protocol v1

This is the **canonical contract** between the Windows host and the Android receiver.
Both sides implement it identically. Mirrors live in:

- `shared/protocol/csharp/Protocol.cs`
- `shared/protocol/kotlin/Protocol.kt`
- `shared/protocol/cpp/Protocol.h`

Keep the three mirrors in sync with this document. All constants (ports, magic numbers,
message type strings) are defined once per language from this spec.

---

## 1. Transports & ports

| Channel     | Transport | Default port | Purpose |
|-------------|-----------|--------------|---------|
| Discovery   | UDP       | 47800        | Find peers on the LAN |
| Control     | TCP       | 47801        | Pairing, negotiation, config, touch, heartbeat |
| Video       | UDP       | 47802        | H.264 packets (real-time) |

Rationale:
- **UDP discovery**: broadcast/one-shot, no connection needed.
- **TCP control**: ordered + reliable; pairing and config must not be lost or reordered.
  Touch events also go here in v1 — they are tiny and correctness (DOWN/UP ordering)
  matters more than shaving a few ms; UDP touch can be added later behind the same
  `ITransport` abstraction.
- **UDP video**: real-time; a late frame is worthless, so we never retransmit. Loss is
  handled by requesting a keyframe, not by resending old packets.

The `ITransport` abstraction (`LanTransport`, `UsbTransport`, `WifiDirectTransport`) hides
the socket details from the session/video/input layers. USB will tunnel the same three
logical channels over `adb reverse`/AOA; Wi-Fi Direct over the p2p group interface.

---

## 2. Discovery (UDP 47800)

Android → broadcast (255.255.255.255:47800), plaintext JSON, one line:

```json
{"t":"SSL_DISCOVER","v":1,"role":"receiver","device":"Galaxy A55","nonce":"<random-hex>"}
```

Windows host listens on 47800 and unicasts back to the sender:

```json
{"t":"SSL_ANNOUNCE","v":1,"role":"host","name":"DESKTOP-OFFICE","ip":"192.168.1.20",
 "controlPort":47801,"paired":false,"nonce":"<echoed>"}
```

Windows MAY also broadcast `SSL_ANNOUNCE` periodically so idle Android apps can list PCs
without probing. `nonce` lets the receiver match replies to its probe.

Discovery is unauthenticated and carries **no secrets** — it only advertises existence and
the control port. All trust is established later during pairing.

---

## 3. Control channel (TCP 47801)

### 3.1 Framing

Every control message is a frame:

```
[ uint32 length (big-endian) ][ payload (length bytes) ]
```

- **Handshake phase** (before a session key exists): `payload` is UTF-8 JSON, plaintext.
- **Secure phase** (after pairing/handshake completes): `payload` is
  `[12-byte nonce][AES-256-GCM ciphertext + 16-byte tag]`; plaintext inside is UTF-8 JSON.

Each JSON message has a `"type"` field (see `MessageType`). Unknown types are ignored.

### 3.2 Handshake & pairing (PIN + ECDH)

Goal: derive a shared AES-256 session key that only the two devices that saw the same PIN
can compute, without any cloud/PKI. Uses ephemeral **ECDH on P-256** (supported natively by
.NET and Android `java.security`) + **HKDF-SHA256** + **AES-256-GCM**.

Sequence (client = Android, server = Windows host):

1. `HELLO` (Android → Windows), plaintext:
   ```json
   {"type":"HELLO","v":1,"device":{"name":"Galaxy A55","os":"Android 14",
     "width":1080,"height":2400,"refreshHz":120,"battery":83},
    "caps":{"codecs":["h264"],"maxBitrateKbps":20000,"hwDecode":true},
    "pubKey":"<base64 SPKI P-256 public key>"}
   ```
2. `HELLO_ACK` (Windows → Android), plaintext:
   ```json
   {"type":"HELLO_ACK","v":1,"host":{"name":"DESKTOP-OFFICE","os":"Windows 11"},
    "pubKey":"<base64 SPKI P-256 public key>",
    "trusted":false}
   ```
   - If the Android public key is already in the Windows **trusted store**, `trusted:true`
     and pairing is skipped (jump to step 6 with the stored key).
3. Windows generates a random **6-digit PIN** (e.g. `482771`) and shows it in the UI. It is
   **not** sent over the wire.
4. Both sides compute:
   ```
   Z          = ECDH(myPriv, peerPub)                 # 32-byte shared secret
   sessionKey = HKDF-SHA256(ikm=Z, salt=PIN_ascii_bytes,
                            info="SecondScreenLocal/v1/session", len=32)
   ```
   Because the PIN is folded into the KDF salt, only a peer that knows the PIN derives the
   same `sessionKey`. Android prompts the user to type the PIN shown on Windows.
5. **Key confirmation**: Android sends `PAIR_CONFIRM` in the **secure phase** (encrypted with
   the derived key):
   ```json
   {"type":"PAIR_CONFIRM","token":"<random-hex>"}
   ```
   Windows decrypts. If AES-GCM authentication **succeeds**, the PIN matched and keys agree;
   Windows replies (encrypted) `PAIR_OK` echoing the token and persists the Android public
   key to its trusted store. If GCM **fails** (wrong PIN / MITM), Windows sends plaintext
   `PAIR_FAIL` and the client may retry. Windows locks the device out after 5 failures.
6. Session is now **secure**; all further control frames are encrypted.

**Security honesty:** this is a *PIN-authenticated ECDH*, not a formally proven PAKE. A
network MITM cannot passively derive the key (they lack the PIN) and gets only online PIN
guesses, which we rate-limit (5 tries → lockout). For higher assurance, swap the KDF step
for SPAKE2/CPace later — the message flow is designed to allow that substitution.

### 3.3 Capability negotiation

After secure phase, Windows sends `SESSION_CONFIG` (encrypted):
```json
{"type":"SESSION_CONFIG","codec":"h264","width":1080,"height":2400,
 "fps":60,"bitrateKbps":12000,"videoPort":47802,"encryptVideo":true,
 "orientation":"auto"}
```
Android replies `SESSION_CONFIG_ACK` with the UDP port it will listen on for video, or an
error if it cannot support the codec/resolution.

### 3.4 Heartbeat & reconnect

- Every 1000 ms each side sends `PING {"type":"PING","ts":<unixMs>}`; peer replies
  `PONG {"type":"PONG","ts":<echoed>}`. RTT/2 estimates one-way network latency.
- If 3 consecutive `PONG`s are missed, the connection is considered lost. The session enters
  **Reconnecting** (max 5 attempts, exponential backoff). The **virtual display is NOT torn
  down** during this grace window (default 15 s) so brief drops don't disrupt the desktop.
- On reconnect, the stored trusted key skips PIN pairing. A fresh keyframe is requested.

### 3.5 Control messages (secure phase)

| type | direction | payload |
|------|-----------|---------|
| `SESSION_CONFIG` / `_ACK` | host↔recv | see 3.3 |
| `PING` / `PONG` | both | `{ts}` |
| `TOUCH` | recv→host | see §4 |
| `REQUEST_KEYFRAME` | recv→host | `{}` (sent on decode error / loss) |
| `STATS` | recv→host | `{decodeFps, renderFps, droppedFrames, jitterMs}` |
| `DEVICE_UPDATE` | recv→host | `{battery, refreshHz, orientation}` |
| `SET_QUALITY` | host→recv | `{bitrateKbps, fps, width, height}` (adaptive) |
| `DISCONNECT` | both | `{reason}` |

---

## 4. Touch / input protocol (Android → Windows, over control channel)

```json
{"type":"TOUCH","deviceId":"<uuid>","pointerId":0,
 "x":0.5123,"y":0.4471,"ts":<unixMs>,"event":"MOVE"}
```

- `event` ∈ `DOWN | MOVE | UP | SCROLL | LONG_PRESS`.
- `x`,`y` are **normalized** `[0.0, 1.0]` relative to the Android surface, so Windows and
  Android resolutions may differ. For `SCROLL`, extra fields `dx`,`dy` (normalized deltas).
- Windows maps normalized coords to the **virtual Display 2** rectangle in the global
  desktop coordinate space (see `ARCHITECTURE.md` §Input mapping) and injects via
  `SendInput` with `MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK`.

Gesture → action mapping (decided on Android, sent as discrete events where useful):

| Gesture | Windows action |
|---------|----------------|
| single tap | left click (DOWN+UP at point) |
| double tap | double click |
| long press | right click |
| one-finger drag | left button held + MOVE |
| two-finger drag | mouse wheel (`SCROLL` with dy) |

---

## 5. Video packetization (UDP 47802)

Each encoded H.264 access unit (frame) is split into packets ≤ ~1200 bytes payload (to stay
under typical MTU). Binary header (all multi-byte fields big-endian):

```
offset size field
0      2    magic = 0x53 0x56 ('S','V')
2      1    version = 1
3      1    flags   bit0=keyframe(IDR) bit1=lastPacketOfFrame bit2=encrypted
4      4    frameId (uint32, monotonic)
8      2    packetIndex (uint16, 0-based)
10     2    packetCount (uint16, total packets in this frame)
12     8    captureTimestampUs (uint64, host monotonic clock, for latency calc)
20     2    payloadLen (uint16)
22     ..   payload
```

- If `flags.encrypted`: `payload` = `[12-byte nonce][AES-256-GCM(ciphertext+tag)]` using the
  session key. Nonce = `frameId(4) || packetIndex(2) || 0x000000000000` counter — unique per
  packet under one session key. `encryptVideo` in `SESSION_CONFIG` toggles this (default on).
- Receiver reassembles by `frameId` using `packetCount`; if any packet of a frame is missing
  when the next frameId starts, the partial frame is dropped and `REQUEST_KEYFRAME` is sent.
- Feed complete access units to `MediaCodec` configured with a `Surface`. **No CPU Bitmap
  decode.**

---

## 6. Versioning

`v` = 1 everywhere. On mismatch the host refuses the session with `DISCONNECT
{"reason":"protocol version mismatch"}`. Additive fields are allowed within v1; breaking
changes bump `v`.
