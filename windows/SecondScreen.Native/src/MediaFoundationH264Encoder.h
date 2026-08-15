// H.264 encoder using a Media Foundation Transform (hardware MFT when available, Microsoft
// software H.264 MFT as fallback). Input: captured BGRA texture -> NV12 (ID3D11VideoProcessor,
// with a CPU converter fallback) -> encoder MFT -> Annex-B NAL units via EncodedCallback.
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
    bool CreateEncoder(bool hardware);
    bool ConvertToNv12(ID3D11Texture2D* bgra, ComPtr<IMFSample>& nv12Sample, uint64_t tsUs);
    bool ConvertToNv12Gpu(ID3D11Texture2D* bgra, ComPtr<IMFSample>& nv12Sample, uint64_t tsUs);
    bool ConvertToNv12Cpu(ID3D11Texture2D* bgra, ComPtr<IMFSample>& nv12Sample, uint64_t tsUs);
    bool DrainEncoder(uint32_t frameId);          // synchronous MFT: pull all ready output
    int  PullOutput(uint32_t frameId);            // deliver one encoded sample: 1=got, 0=need input, -1=err
    bool EncodeAsync(IMFSample* nv12, uint32_t frameId); // async (hardware) MFT event model
    bool InitVideoProcessor();          // ID3D11VideoProcessor for reliable BGRA->NV12 on GPU
    ID3D11Texture2D* NextNv12Texture(); // pooled NV12 output textures (avoids async-encoder race)
    void ApplyBitrate();

    ComPtr<ID3D11Device> device_;
    ComPtr<IMFTransform> encoder_;     // H.264 encoder MFT
    ComPtr<IMFMediaEventGenerator> encoderEvents_; // set when the encoder MFT is asynchronous
    bool async_ = false;               // true when encoder_ is an async (typically hardware) MFT
    ComPtr<IMFDXGIDeviceManager> dxgiManager_;
    UINT resetToken_ = 0;

    // GPU color-space converter (BGRA capture -> NV12 encoder input) via the Direct3D 11 video API.
    ComPtr<ID3D11VideoDevice> vdevice_;
    ComPtr<ID3D11VideoContext> vcontext_;
    ComPtr<ID3D11VideoProcessorEnumerator> vpEnum_;
    ComPtr<ID3D11VideoProcessor> vproc_;
    ComPtr<ID3D11Texture2D> inCopy_;      // BGRA copy with RENDER_TARGET bind for the VP input view
    ComPtr<ID3D11Texture2D> stagingBgra_; // CPU-readback copy for the software converter fallback
    bool useCpuConvert_ = false;          // set when the GPU video processor is unusable

    EncoderConfig cfg_;
    EncodedCallback cb_;
    std::atomic<bool> forceKeyframe_{true};
    std::atomic<int> pendingBitrateKbps_{0};
    uint32_t frameCounter_ = 0;
    std::string lastError_;
    LONGLONG sampleDuration_ = 0;
    std::vector<ComPtr<ID3D11Texture2D>> nv12TexPool_;  // NV12 outputs for VideoProcessorBlt
    size_t nv12Idx_ = 0;
};

} // namespace ssl
