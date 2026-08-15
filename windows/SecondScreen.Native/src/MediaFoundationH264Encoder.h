// H.264 encoder using a Media Foundation Transform (hardware MFT when available, Microsoft
// software H.264 MFT as fallback). Input: captured BGRA texture -> NV12 (Video Processor MFT)
// -> encoder MFT -> Annex-B NAL units delivered via EncodedCallback.
#pragma once
#include "IVideoEncoder.h"
#include <mfidl.h>
#include <mftransform.h>
#include <wrl/client.h>
#include <atomic>
#include <string>
#include <vector>

namespace ssl {

using Microsoft::WRL::ComPtr;

class MediaFoundationH264Encoder : public IVideoEncoder {
public:
    bool Initialize(ID3D11Device* device, const EncoderConfig& cfg) override;
    bool EncodeFrame(ID3D11Texture2D* frame, uint64_t timestampUs) override;
    void RequestKeyframe() override { forceKeyframe_ = true; }
    void SetBitrate(int kbps) override;
    void SetFps(int fps) override;
    void SetCallback(EncodedCallback cb) override { cb_ = std::move(cb); }
    void Shutdown() override;

    const std::string& LastError() const { return lastError_; }

private:
    bool CreateColorConverter();
    bool CreateEncoder(bool hardware);
    bool ConvertToNv12(ID3D11Texture2D* bgra, ComPtr<IMFSample>& nv12Sample, uint64_t tsUs);
    bool DrainEncoder(uint32_t frameId);          // synchronous MFT: pull all ready output
    int  PullOutput(uint32_t frameId);            // deliver one encoded sample: 1=got, 0=need input, -1=err
    bool EncodeAsync(IMFSample* nv12, uint32_t frameId); // async (hardware) MFT event model
    IMFSample* NextNv12Sample();       // pooled D3D11 NV12 output sample for the Video Processor
    void ApplyBitrate();

    ComPtr<ID3D11Device> device_;
    ComPtr<IMFTransform> converter_;   // CLSID_VideoProcessorMFT: BGRA -> NV12
    ComPtr<IMFTransform> encoder_;     // H.264 encoder MFT
    ComPtr<IMFMediaEventGenerator> encoderEvents_; // set when the encoder MFT is asynchronous
    bool async_ = false;               // true when encoder_ is an async (typically hardware) MFT
    ComPtr<IMFDXGIDeviceManager> dxgiManager_;
    UINT resetToken_ = 0;

    EncoderConfig cfg_;
    EncodedCallback cb_;
    std::atomic<bool> forceKeyframe_{true};
    std::atomic<int> pendingBitrateKbps_{0};
    uint32_t frameCounter_ = 0;
    std::string lastError_;
    LONGLONG sampleDuration_ = 0;
    std::vector<ComPtr<IMFSample>> nv12Pool_;  // ring of D3D NV12 output samples for the converter
    size_t nv12Idx_ = 0;
};

} // namespace ssl
