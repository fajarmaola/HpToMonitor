#include "MediaFoundationH264Encoder.h"
#include <mfapi.h>
#include <mferror.h>
#include <codecapi.h>
#include <wmcodecdsp.h>   // CLSID_VideoProcessorMFT
#include <vector>
#include <thread>
#include <chrono>
#include <cstdio>

// Media Foundation H.264 encode. NOTE(hardware): this path requires a real GPU + MF stack to
// validate end-to-end. The API usage below follows the documented MFT contract; the exact
// hardware MFT attributes vary slightly per vendor and may need tuning on target GPUs
// (marked TODO(hardware) inline). It is NOT a mock — it drives the real MFT pipeline.

namespace ssl {

static bool SetGuidAttr(IMFAttributes* a, const GUID& key, const GUID& val) {
    return SUCCEEDED(a->SetGUID(key, val));
}

bool MediaFoundationH264Encoder::Initialize(ID3D11Device* device, const EncoderConfig& cfg) {
    device_ = device;
    cfg_ = cfg;
    sampleDuration_ = cfg.fps > 0 ? (10'000'000LL / cfg.fps) : 166'667LL;

    if (FAILED(MFStartup(MF_VERSION, MFSTARTUP_LITE))) {
        lastError_ = "MFStartup failed"; return false;
    }
    // Share the D3D11 device with MF so encoders can consume DXGI surfaces.
    if (FAILED(MFCreateDXGIDeviceManager(&resetToken_, &dxgiManager_)) ||
        FAILED(dxgiManager_->ResetDevice(device_.Get(), resetToken_))) {
        lastError_ = "MFCreateDXGIDeviceManager failed"; return false;
    }

    if (!CreateColorConverter()) return false;

    bool wantHw = cfg.useHardware;
    if (!CreateEncoder(wantHw)) {
        if (wantHw) { lastError_ += " (retrying software)"; if (!CreateEncoder(false)) return false; }
        else return false;
    }
    return true;
}

bool MediaFoundationH264Encoder::CreateColorConverter() {
    // Video Processor MFT: BGRA (DXGI capture format) -> NV12 (encoder input).
    if (FAILED(CoCreateInstance(CLSID_VideoProcessorMFT, nullptr, CLSCTX_INPROC_SERVER,
                                IID_PPV_ARGS(&converter_)))) {
        lastError_ = "create VideoProcessorMFT failed"; return false;
    }
    ComPtr<IMFAttributes> attrs;
    if (SUCCEEDED(converter_->GetAttributes(&attrs)))
        attrs->SetUINT32(MF_SA_D3D11_AWARE, TRUE);
    converter_->ProcessMessage(MFT_MESSAGE_SET_D3D_MANAGER,
        reinterpret_cast<ULONG_PTR>(dxgiManager_.Get()));

    // Input BGRA (ARGB32).
    ComPtr<IMFMediaType> in;
    MFCreateMediaType(&in);
    in->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
    in->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_ARGB32);
    MFSetAttributeSize(in.Get(), MF_MT_FRAME_SIZE, cfg_.width, cfg_.height);
    MFSetAttributeRatio(in.Get(), MF_MT_FRAME_RATE, cfg_.fps, 1);
    if (FAILED(converter_->SetInputType(0, in.Get(), 0))) { lastError_ = "conv SetInputType"; return false; }

    // Output NV12.
    ComPtr<IMFMediaType> out;
    MFCreateMediaType(&out);
    out->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
    out->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_NV12);
    MFSetAttributeSize(out.Get(), MF_MT_FRAME_SIZE, cfg_.width, cfg_.height);
    MFSetAttributeRatio(out.Get(), MF_MT_FRAME_RATE, cfg_.fps, 1);
    if (FAILED(converter_->SetOutputType(0, out.Get(), 0))) { lastError_ = "conv SetOutputType"; return false; }

    // MUST notify streaming so the D3D-aware processor allocates its output sample pool; without
    // this the first ProcessOutput fails ("conv ProcessOutput").
    converter_->ProcessMessage(MFT_MESSAGE_NOTIFY_BEGIN_STREAMING, 0);
    converter_->ProcessMessage(MFT_MESSAGE_NOTIFY_START_OF_STREAM, 0);
    return true;
}

bool MediaFoundationH264Encoder::CreateEncoder(bool hardware) {
    // Enumerate H.264 encoder MFTs, preferring hardware.
    MFT_REGISTER_TYPE_INFO outInfo{ MFMediaType_Video, MFVideoFormat_H264 };
    UINT32 flags = MFT_ENUM_FLAG_SORTANDFILTER |
                   (hardware ? MFT_ENUM_FLAG_HARDWARE : MFT_ENUM_FLAG_SYNCMFT);
    IMFActivate** activates = nullptr;
    UINT32 count = 0;
    if (FAILED(MFTEnumEx(MFT_CATEGORY_VIDEO_ENCODER, flags, nullptr, &outInfo, &activates, &count))
        || count == 0) {
        lastError_ = hardware ? "no hardware H.264 MFT" : "no H.264 MFT";
        return false;
    }
    HRESULT hr = activates[0]->ActivateObject(IID_PPV_ARGS(&encoder_));
    for (UINT32 i = 0; i < count; ++i) activates[i]->Release();
    CoTaskMemFree(activates);
    if (FAILED(hr)) { lastError_ = "ActivateObject encoder"; return false; }

    ComPtr<IMFAttributes> attrs;
    if (SUCCEEDED(encoder_->GetAttributes(&attrs))) {
        UINT32 isAsync = 0;
        attrs->GetUINT32(MF_TRANSFORM_ASYNC, &isAsync);
        async_ = isAsync != 0;
        if (async_) attrs->SetUINT32(MF_TRANSFORM_ASYNC_UNLOCK, TRUE); // required before use
        if (hardware)
            encoder_->ProcessMessage(MFT_MESSAGE_SET_D3D_MANAGER,
                reinterpret_cast<ULONG_PTR>(dxgiManager_.Get()));
    }
    if (async_ && FAILED(encoder_.As(&encoderEvents_))) {
        lastError_ = "async encoder MFT has no IMFMediaEventGenerator"; return false;
    }

    // Output type must be set before input type for encoders.
    ComPtr<IMFMediaType> out;
    MFCreateMediaType(&out);
    out->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
    out->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_H264);
    out->SetUINT32(MF_MT_AVG_BITRATE, cfg_.bitrateKbps * 1000);
    out->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive);
    out->SetUINT32(MF_MT_MPEG2_PROFILE, eAVEncH264VProfile_Base);
    MFSetAttributeSize(out.Get(), MF_MT_FRAME_SIZE, cfg_.width, cfg_.height);
    MFSetAttributeRatio(out.Get(), MF_MT_FRAME_RATE, cfg_.fps, 1);
    MFSetAttributeRatio(out.Get(), MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
    if (FAILED(encoder_->SetOutputType(0, out.Get(), 0))) { lastError_ = "enc SetOutputType"; return false; }

    ComPtr<IMFMediaType> in;
    MFCreateMediaType(&in);
    in->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
    in->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_NV12);
    in->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive);
    MFSetAttributeSize(in.Get(), MF_MT_FRAME_SIZE, cfg_.width, cfg_.height);
    MFSetAttributeRatio(in.Get(), MF_MT_FRAME_RATE, cfg_.fps, 1);
    if (FAILED(encoder_->SetInputType(0, in.Get(), 0))) { lastError_ = "enc SetInputType"; return false; }

    // Low-latency + CBR configuration via ICodecAPI when supported.
    ComPtr<ICodecAPI> codec;
    if (SUCCEEDED(encoder_.As(&codec))) {
        VARIANT v; VariantInit(&v);
        v.vt = VT_UI4; v.ulVal = eAVEncCommonRateControlMode_CBR;
        codec->SetValue(&CODECAPI_AVEncCommonRateControlMode, &v);
        v.ulVal = cfg_.bitrateKbps * 1000;
        codec->SetValue(&CODECAPI_AVEncCommonMeanBitRate, &v);
        // TODO(hardware): CODECAPI_AVLowLatencyMode is supported on most HW encoders; verify
        // per-GPU. Enabling it minimizes encoder buffering for the latency targets.
        VARIANT lat; VariantInit(&lat); lat.vt = VT_BOOL; lat.boolVal = VARIANT_TRUE;
        codec->SetValue(&CODECAPI_AVLowLatencyMode, &lat);
    }

    encoder_->ProcessMessage(MFT_MESSAGE_NOTIFY_BEGIN_STREAMING, 0);
    encoder_->ProcessMessage(MFT_MESSAGE_NOTIFY_START_OF_STREAM, 0);
    return true;
}

bool MediaFoundationH264Encoder::ConvertToNv12(ID3D11Texture2D* bgra, ComPtr<IMFSample>& outSample,
                                               uint64_t tsUs) {
    // Wrap the captured texture in an IMFSample and push through the video processor.
    ComPtr<IMFMediaBuffer> inBuf;
    if (FAILED(MFCreateDXGISurfaceBuffer(__uuidof(ID3D11Texture2D), bgra, 0, FALSE, &inBuf)))
        { lastError_ = "MFCreateDXGISurfaceBuffer(in)"; return false; }
    ComPtr<IMFSample> inSample;
    MFCreateSample(&inSample);
    inSample->AddBuffer(inBuf.Get());
    inSample->SetSampleTime((LONGLONG)(tsUs * 10)); // 100ns units
    inSample->SetSampleDuration(sampleDuration_);

    if (FAILED(converter_->ProcessInput(0, inSample.Get(), 0))) { lastError_ = "conv ProcessInput"; return false; }

    MFT_OUTPUT_STREAM_INFO si{};
    converter_->GetOutputStreamInfo(0, &si);
    // Let the MFT allocate the NV12 output sample (D3D-aware processor provides one).
    MFT_OUTPUT_DATA_BUFFER odb{};
    DWORD status = 0;
    ComPtr<IMFSample> conv;
    bool providesSamples = (si.dwFlags & (MFT_OUTPUT_STREAM_PROVIDES_SAMPLES |
                                          MFT_OUTPUT_STREAM_CAN_PROVIDE_SAMPLES)) != 0;
    if (!providesSamples) {
        MFCreateSample(&conv);
        ComPtr<IMFMediaBuffer> b;
        MFCreateMemoryBuffer(si.cbSize ? si.cbSize : cfg_.width * cfg_.height * 3 / 2, &b);
        conv->AddBuffer(b.Get());
        odb.pSample = conv.Get();
    }
    HRESULT hr = converter_->ProcessOutput(0, 1, &odb, &status);
    if (hr == MF_E_TRANSFORM_STREAM_CHANGE) {
        // The processor wants its output type (re)negotiated. Re-select NV12 and retry once.
        ComPtr<IMFMediaType> t;
        for (DWORD i = 0; converter_->GetOutputAvailableType(0, i, &t) == S_OK; ++i) {
            GUID sub{}; t->GetGUID(MF_MT_SUBTYPE, &sub);
            if (sub == MFVideoFormat_NV12) { converter_->SetOutputType(0, t.Get(), 0); break; }
            t.Reset();
        }
        odb = MFT_OUTPUT_DATA_BUFFER{}; status = 0;
        if (!providesSamples && conv) odb.pSample = conv.Get();
        hr = converter_->ProcessOutput(0, 1, &odb, &status);
    }
    if (FAILED(hr)) {
        char b[64]; snprintf(b, sizeof(b), "conv ProcessOutput=0x%08X", (unsigned)hr);
        lastError_ = b; return false;
    }
    outSample = odb.pSample; // if provided by MFT, take ownership
    if (providesSamples && odb.pSample) odb.pSample->Release() /* balanced by ComPtr assign */;
    outSample->SetSampleTime((LONGLONG)(tsUs * 10));
    outSample->SetSampleDuration(sampleDuration_);
    return true;
}

bool MediaFoundationH264Encoder::EncodeFrame(ID3D11Texture2D* frame, uint64_t tsUs) {
    ApplyBitrate();

    ComPtr<IMFSample> nv12;
    if (!ConvertToNv12(frame, nv12, tsUs)) return false;

    if (forceKeyframe_.exchange(false)) {
        // Request an IDR on the next frame.
        ComPtr<ICodecAPI> codec;
        if (SUCCEEDED(encoder_.As(&codec))) {
            VARIANT v; VariantInit(&v); v.vt = VT_UI4; v.ulVal = 1;
            codec->SetValue(&CODECAPI_AVEncVideoForceKeyFrame, &v);
        }
    }

    // Hardware encoders are usually ASYNC MFTs: driving them with plain ProcessInput/ProcessOutput
    // yields NO output (black phone). They must be pumped via METransformNeedInput/HaveOutput events.
    if (async_) return EncodeAsync(nv12.Get(), frameCounter_++);

    HRESULT hr = encoder_->ProcessInput(0, nv12.Get(), 0);
    if (hr == MF_E_NOTACCEPTING) { DrainEncoder(frameCounter_); hr = encoder_->ProcessInput(0, nv12.Get(), 0); }
    if (FAILED(hr)) { lastError_ = "enc ProcessInput"; return false; }
    return DrainEncoder(frameCounter_++);
}

// Async (hardware) MFT event pump: feed one input when the MFT asks, then drain ready output.
bool MediaFoundationH264Encoder::EncodeAsync(IMFSample* nv12, uint32_t frameId) {
    using namespace std::chrono;
    auto deadline = steady_clock::now() + milliseconds(1000);
    bool fed = false;
    while (!fed && steady_clock::now() < deadline) {
        ComPtr<IMFMediaEvent> ev;
        HRESULT hr = encoderEvents_->GetEvent(MF_EVENT_FLAG_NO_WAIT, &ev);
        if (hr == MF_E_NO_EVENTS_AVAILABLE) { std::this_thread::sleep_for(milliseconds(1)); continue; }
        if (FAILED(hr)) { lastError_ = "async GetEvent"; return false; }
        MediaEventType met = 0; ev->GetType(&met);
        if (met == METransformNeedInput) {
            if (FAILED(encoder_->ProcessInput(0, nv12, 0))) { lastError_ = "async ProcessInput"; return false; }
            fed = true;
        } else if (met == METransformHaveOutput) {
            if (PullOutput(frameId) < 0) return false;
        }
    }
    if (!fed) { lastError_ = "async encoder sent no METransformNeedInput within 1s"; return false; }

    // Collect any immediately-available encoded output (low latency); leftover is caught next frame.
    for (int i = 0; i < 8; ++i) {
        ComPtr<IMFMediaEvent> ev;
        HRESULT hr = encoderEvents_->GetEvent(MF_EVENT_FLAG_NO_WAIT, &ev);
        if (hr == MF_E_NO_EVENTS_AVAILABLE || FAILED(hr)) break;
        MediaEventType met = 0; ev->GetType(&met);
        if (met == METransformHaveOutput) { if (PullOutput(frameId) < 0) return false; }
    }
    return true;
}

bool MediaFoundationH264Encoder::DrainEncoder(uint32_t frameId) {
    int r;
    do { r = PullOutput(frameId); } while (r == 1);
    return r >= 0;
}

// Pull one encoded access unit and deliver it. Returns 1 = delivered, 0 = need more input, -1 = error.
int MediaFoundationH264Encoder::PullOutput(uint32_t frameId) {
    MFT_OUTPUT_STREAM_INFO si{};
    encoder_->GetOutputStreamInfo(0, &si);

    MFT_OUTPUT_DATA_BUFFER odb{};
    DWORD status = 0;
    ComPtr<IMFSample> outSample;
    bool provides = (si.dwFlags & (MFT_OUTPUT_STREAM_PROVIDES_SAMPLES |
                                   MFT_OUTPUT_STREAM_CAN_PROVIDE_SAMPLES)) != 0;
    if (!provides) {
        MFCreateSample(&outSample);
        ComPtr<IMFMediaBuffer> b;
        MFCreateMemoryBuffer(si.cbSize ? si.cbSize : (1 << 20), &b);
        outSample->AddBuffer(b.Get());
        odb.pSample = outSample.Get();
    }
    HRESULT hr = encoder_->ProcessOutput(0, 1, &odb, &status);
    if (hr == MF_E_TRANSFORM_NEED_MORE_INPUT) return 0;
    if (FAILED(hr)) { lastError_ = "enc ProcessOutput"; return -1; }

    ComPtr<IMFSample> got = odb.pSample;
    if (provides && odb.pSample) odb.pSample->Release();

    UINT32 clean = 0;
    got->GetUINT32(MFSampleExtension_CleanPoint, &clean);
    bool keyframe = clean != 0;

    ComPtr<IMFMediaBuffer> buf;
    got->ConvertToContiguousBuffer(&buf);
    BYTE* data = nullptr; DWORD maxLen = 0, curLen = 0;
    buf->Lock(&data, &maxLen, &curLen);
    LONGLONG st = 0; got->GetSampleTime(&st);
    if (cb_ && curLen > 0)
        cb_(frameId, (uint64_t)(st / 10), keyframe, data, (int)curLen);
    buf->Unlock();
    return 1;
}

void MediaFoundationH264Encoder::ApplyBitrate() {
    int kbps = pendingBitrateKbps_.exchange(0);
    if (kbps <= 0) return;
    ComPtr<ICodecAPI> codec;
    if (SUCCEEDED(encoder_.As(&codec))) {
        VARIANT v; VariantInit(&v); v.vt = VT_UI4; v.ulVal = kbps * 1000;
        codec->SetValue(&CODECAPI_AVEncCommonMeanBitRate, &v);
    }
}

void MediaFoundationH264Encoder::SetBitrate(int kbps) { pendingBitrateKbps_ = kbps; }
void MediaFoundationH264Encoder::SetFps(int fps) {
    if (fps > 0) sampleDuration_ = 10'000'000LL / fps;
}

void MediaFoundationH264Encoder::Shutdown() {
    if (encoder_) {
        encoder_->ProcessMessage(MFT_MESSAGE_NOTIFY_END_OF_STREAM, 0);
        encoder_->ProcessMessage(MFT_MESSAGE_NOTIFY_END_STREAMING, 0);
    }
    encoder_.Reset();
    converter_.Reset();
    dxgiManager_.Reset();
    MFShutdown();
}

} // namespace ssl
