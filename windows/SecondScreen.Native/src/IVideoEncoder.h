// Abstraction over a video encoder so HEVC can be added later (IVideoEncoder / H264Encoder /
// HEVCEncoder), mirroring the interface split requested in the problem statement.
#pragma once
#include <cstdint>
#include <functional>
#include <d3d11.h>

namespace ssl {

struct EncoderConfig {
    int width = 1080;
    int height = 2400;
    int fps = 60;
    int bitrateKbps = 12000;
    bool useHardware = true;
};

// Called with one encoded Annex-B access unit.
using EncodedCallback = std::function<void(uint32_t frameId, uint64_t tsUs, bool keyframe,
                                           const uint8_t* data, int len)>;

class IVideoEncoder {
public:
    virtual ~IVideoEncoder() = default;
    virtual bool Initialize(ID3D11Device* device, const EncoderConfig& cfg) = 0;
    // Encode one captured BGRA/NV12 texture. Returns false on fatal error.
    virtual bool EncodeFrame(ID3D11Texture2D* frame, uint64_t timestampUs) = 0;
    virtual void RequestKeyframe() = 0;
    virtual void SetBitrate(int kbps) = 0;
    virtual void SetFps(int fps) = 0;
    virtual void SetCallback(EncodedCallback cb) = 0;
    virtual void Shutdown() = 0;
};

} // namespace ssl
