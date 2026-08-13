// SecondScreen Local — Wire Protocol v1 (C++ mirror)
// Canonical spec: /shared/protocol/PROTOCOL.md. Keep the three language mirrors in sync.
#pragma once
#include <cstdint>

namespace ssl_protocol {

constexpr int    kVersion            = 1;

// Ports (PROTOCOL.md §1)
constexpr uint16_t kDiscoveryUdpPort = 47800;
constexpr uint16_t kControlTcpPort   = 47801;
constexpr uint16_t kVideoUdpPort     = 47802;

// Crypto (PROTOCOL.md §3.2)
constexpr int kSessionKeyBytes = 32; // AES-256
constexpr int kGcmNonceBytes   = 12;
constexpr int kGcmTagBytes     = 16;

// Video packet header (PROTOCOL.md §5)
constexpr uint8_t kVideoMagic0 = 0x53; // 'S'
constexpr uint8_t kVideoMagic1 = 0x56; // 'V'
constexpr int     kVideoHeaderBytes = 22;
constexpr int     kVideoMaxPayload  = 1200;

// flags
constexpr uint8_t kFlagKeyframe   = 0x01;
constexpr uint8_t kFlagLastPacket = 0x02;
constexpr uint8_t kFlagEncrypted  = 0x04;

#pragma pack(push, 1)
struct VideoPacketHeader {
    uint8_t  magic0;             // 0x53
    uint8_t  magic1;             // 0x56
    uint8_t  version;            // 1
    uint8_t  flags;              // keyframe | last | encrypted
    uint32_t frameId;            // big-endian on wire
    uint16_t packetIndex;        // big-endian
    uint16_t packetCount;        // big-endian
    uint64_t captureTimestampUs; // big-endian
    uint16_t payloadLen;         // big-endian
};
#pragma pack(pop)
static_assert(sizeof(VideoPacketHeader) == kVideoHeaderBytes, "header must be 22 bytes");

} // namespace ssl_protocol
