#include "MediaFoundationH264Encoder.h"
#include <mfapi.h>
#include <mferror.h>
#include <codecapi.h>
#include <d3d11_4.h>      // ID3D11Multithread
#include <vector>
#include <thread>
#include <chrono>
#include <cstdio>

// Media Foundation H.264 encode. NOTE(hardware): this path requires a real GPU + MF stack to
// validate end-to-end. The API usage below follows the documented MFT contract; the exact
// hardware MFT attributes vary slightly per vendor and may need tuning on target GPUs
// (marked TODO(hardware) inline). It is NOT a mock — it drives the real MFT pipeline.

namespace ssl {

bool MediaFoundationH264Encoder::Initialize(ID3D11Device* device, const EncoderConfig& cfg) {
    device_ = device;
    cfg_ = cfg;
    sampleDuration_ = cfg.fps > 0 ? (10'000'000LL / cfg.fps) : 166'667LL;

    if (FAILED(MFStartup(MF_VERSION, MFSTARTUP_LITE))) {
        lastError_ = "MFStartup failed"; return false;
    }
    // MANDATORY when this device is shared with MF hardware MFTs: without multithread
    // protection the encoder's own threads race the capture thread on the immediate context
    // and drivers fail with random DXGI_ERROR_INVALID_CALL.
    {
        ComPtr<ID3D11DeviceContext> ctx;
        device_->GetImmediateContext(&ctx);
        ComPtr<ID3D11Multithread> mt;
        if (SUCCEEDED(ctx.As(&mt))) mt->SetMultithreadProtected(TRUE);
    }
    // Share the D3D11 device with MF so encoders can consume DXGI surfaces.
    if (FAILED(MFCreateDXGIDeviceManager(&resetToken_, &dxgiManager_)) ||
        FAILED(dxgiManager_->ResetDevice(device_.Get(), resetToken_))) {
        lastError_ = "MFCreateDXGIDeviceManager failed"; return false;
    }

    // GPU BGRA->NV12 when the driver supports it; otherwise the CPU converter takes over.
    useCpuConvert_ = false;
    if (!InitVideoProcessor()) useCpuConvert_ = true;

    bool wantHw = cfg.useHardware;
    if (!CreateEncoder(wantHw)) {
        if (wantHw) { lastError_ += " (retrying software)"; if (!CreateEncoder(false)) return false; }
        else return false;
    }
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

bool MediaFoundationH264Encoder::InitVideoProcessor() {
    if (vproc_) return true;
    if (FAILED(device_.As(&vdevice_))) { lastError_ = "no ID3D11VideoDevice"; return false; }
    ComPtr<ID3D11DeviceContext> ctx;
    device_->GetImmediateContext(&ctx);
    if (FAILED(ctx.As(&vcontext_))) { lastError_ = "no ID3D11VideoContext"; return false; }

    D3D11_VIDEO_PROCESSOR_CONTENT_DESC cd{};
    cd.InputFrameFormat = D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE;
    cd.InputWidth = (UINT)cfg_.width;  cd.InputHeight = (UINT)cfg_.height;
    cd.OutputWidth = (UINT)cfg_.width; cd.OutputHeight = (UINT)cfg_.height;
    cd.Usage = D3D11_VIDEO_USAGE_PLAYBACK_NORMAL;
    if (FAILED(vdevice_->CreateVideoProcessorEnumerator(&cd, &vpEnum_)))
        { lastError_ = "CreateVideoProcessorEnumerator"; return false; }

    // Verify the driver actually supports BGRA input and NV12 output on this path.
    UINT support = 0;
    if (FAILED(vpEnum_->CheckVideoProcessorFormat(DXGI_FORMAT_B8G8R8A8_UNORM, &support)) ||
        !(support & D3D11_VIDEO_PROCESSOR_FORMAT_SUPPORT_INPUT))
        { lastError_ = "VP: BGRA input unsupported"; return false; }
    if (FAILED(vpEnum_->CheckVideoProcessorFormat(DXGI_FORMAT_NV12, &support)) ||
        !(support & D3D11_VIDEO_PROCESSOR_FORMAT_SUPPORT_OUTPUT))
        { lastError_ = "VP: NV12 output unsupported"; return false; }

    if (FAILED(vdevice_->CreateVideoProcessor(vpEnum_.Get(), 0, &vproc_)))
        { lastError_ = "CreateVideoProcessor"; return false; }

    // Full-range RGB in -> limited-range BT.709 NV12 out (matches the CPU converter).
    D3D11_VIDEO_PROCESSOR_COLOR_SPACE inCs{};
    inCs.Usage = 0; inCs.RGB_Range = 0;
    inCs.Nominal_Range = D3D11_VIDEO_PROCESSOR_NOMINAL_RANGE_0_255;
    vcontext_->VideoProcessorSetStreamColorSpace(vproc_.Get(), 0, &inCs);
    D3D11_VIDEO_PROCESSOR_COLOR_SPACE outCs{};
    outCs.Usage = 0; outCs.YCbCr_Matrix = 1; // BT.709
    outCs.Nominal_Range = D3D11_VIDEO_PROCESSOR_NOMINAL_RANGE_16_235;
    vcontext_->VideoProcessorSetOutputColorSpace(vproc_.Get(), &outCs);
    return true;
}

// Returns one of a ring of NV12 output textures (pool avoids a race with the zero-copy async encoder).
ID3D11Texture2D* MediaFoundationH264Encoder::NextNv12Texture() {
    if (nv12TexPool_.empty()) {
        for (int i = 0; i < 6; ++i) {
            D3D11_TEXTURE2D_DESC d{};
            d.Width = (UINT)cfg_.width; d.Height = (UINT)cfg_.height;
            d.MipLevels = 1; d.ArraySize = 1;
            d.Format = DXGI_FORMAT_NV12; d.SampleDesc.Count = 1;
            d.Usage = D3D11_USAGE_DEFAULT;
            d.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
            ComPtr<ID3D11Texture2D> tex;
            if (FAILED(device_->CreateTexture2D(&d, nullptr, tex.GetAddressOf()))) return nullptr;
            nv12TexPool_.push_back(tex);
        }
    }
    ID3D11Texture2D* t = nv12TexPool_[nv12Idx_ % nv12TexPool_.size()].Get();
    ++nv12Idx_;
    return t;
}

// BGRA capture texture -> NV12. Try the GPU video processor first; if this driver rejects any
// step, permanently fall back to a CPU converter that works on every machine.
bool MediaFoundationH264Encoder::ConvertToNv12(ID3D11Texture2D* bgra, ComPtr<IMFSample>& outSample,
                                               uint64_t tsUs) {
    if (!useCpuConvert_) {
        if (ConvertToNv12Gpu(bgra, outSample, tsUs)) return true;
        useCpuConvert_ = true; // remember: GPU path is broken on this driver
    }
    return ConvertToNv12Cpu(bgra, outSample, tsUs);
}

bool MediaFoundationH264Encoder::ConvertToNv12Gpu(ID3D11Texture2D* bgra, ComPtr<IMFSample>& outSample,
                                                  uint64_t tsUs) {
    if (!InitVideoProcessor()) return false;

    ComPtr<ID3D11DeviceContext> ctx;
    device_->GetImmediateContext(&ctx);

    // Copy the captured BGRA into a RENDER_TARGET texture. Input views only accept bind flags
    // 0 / DECODER / VIDEO_ENCODER / RENDER_TARGET / UNORDERED_ACCESS — SHADER_RESOURCE alone
    // makes CreateVideoProcessorInputView fail (the exact bug seen in the field).
    if (!inCopy_) {
        D3D11_TEXTURE2D_DESC d{}; bgra->GetDesc(&d);
        d.Usage = D3D11_USAGE_DEFAULT; d.CPUAccessFlags = 0; d.MiscFlags = 0;
        d.BindFlags = D3D11_BIND_RENDER_TARGET;
        if (FAILED(device_->CreateTexture2D(&d, nullptr, inCopy_.GetAddressOf())))
            { lastError_ = "create input-copy texture"; return false; }
    }
    ctx->CopyResource(inCopy_.Get(), bgra);

    ID3D11Texture2D* nv12 = NextNv12Texture();
    if (!nv12) { lastError_ = "create NV12 texture"; return false; }

    D3D11_VIDEO_PROCESSOR_INPUT_VIEW_DESC ivd{};
    ivd.ViewDimension = D3D11_VPIV_DIMENSION_TEXTURE2D;
    ivd.Texture2D.MipSlice = 0; ivd.Texture2D.ArraySlice = 0;
    ComPtr<ID3D11VideoProcessorInputView> iview;
    HRESULT hr = vdevice_->CreateVideoProcessorInputView(inCopy_.Get(), vpEnum_.Get(), &ivd, &iview);
    if (FAILED(hr)) {
        char b[64]; snprintf(b, sizeof(b), "CreateVPInputView=0x%08X", (unsigned)hr);
        lastError_ = b; return false;
    }

    D3D11_VIDEO_PROCESSOR_OUTPUT_VIEW_DESC ovd{};
    ovd.ViewDimension = D3D11_VPOV_DIMENSION_TEXTURE2D;
    ovd.Texture2D.MipSlice = 0;
    ComPtr<ID3D11VideoProcessorOutputView> oview;
    hr = vdevice_->CreateVideoProcessorOutputView(nv12, vpEnum_.Get(), &ovd, &oview);
    if (FAILED(hr)) {
        char b[64]; snprintf(b, sizeof(b), "CreateVPOutputView=0x%08X", (unsigned)hr);
        lastError_ = b; return false;
    }

    D3D11_VIDEO_PROCESSOR_STREAM stream{};
    stream.Enable = TRUE;
    stream.pInputSurface = iview.Get();
    hr = vcontext_->VideoProcessorBlt(vproc_.Get(), oview.Get(), 0, 1, &stream);
    if (FAILED(hr)) {
        char b[48]; snprintf(b, sizeof(b), "VideoProcessorBlt=0x%08X", (unsigned)hr);
        lastError_ = b; return false;
    }

    // Wrap the NV12 texture as an IMFSample for the encoder.
    ComPtr<IMFMediaBuffer> buf;
    if (FAILED(MFCreateDXGISurfaceBuffer(__uuidof(ID3D11Texture2D), nv12, 0, FALSE, &buf)))
        { lastError_ = "MFCreateDXGISurfaceBuffer(nv12)"; return false; }
    ComPtr<IMFSample> s;
    MFCreateSample(&s);
    s->AddBuffer(buf.Get());
    s->SetSampleTime((LONGLONG)(tsUs * 10)); // 100ns units
    s->SetSampleDuration(sampleDuration_);
    outSample = s;
    return true;
}

// Software BGRA->NV12 (BT.709 limited range) into a system-memory MF 2D buffer. Slower than the
// GPU path but immune to driver quirks — guarantees the phone gets frames on any machine.
bool MediaFoundationH264Encoder::ConvertToNv12Cpu(ID3D11Texture2D* bgra, ComPtr<IMFSample>& outSample,
                                                  uint64_t tsUs) {
    ComPtr<ID3D11DeviceContext> ctx;
    device_->GetImmediateContext(&ctx);

    if (!stagingBgra_) {
        D3D11_TEXTURE2D_DESC d{}; bgra->GetDesc(&d);
        d.Usage = D3D11_USAGE_STAGING; d.BindFlags = 0; d.MiscFlags = 0;
        d.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
        if (FAILED(device_->CreateTexture2D(&d, nullptr, stagingBgra_.GetAddressOf())))
            { lastError_ = "cpu: create staging BGRA"; return false; }
    }
    ctx->CopyResource(stagingBgra_.Get(), bgra);
    D3D11_MAPPED_SUBRESOURCE map{};
    HRESULT hr = ctx->Map(stagingBgra_.Get(), 0, D3D11_MAP_READ, 0, &map);
    if (FAILED(hr)) {
        char b[40]; snprintf(b, sizeof(b), "cpu Map=0x%08X", (unsigned)hr);
        lastError_ = b; return false;
    }

    const int w = cfg_.width & ~1, h = cfg_.height & ~1; // NV12 needs even dimensions
    ComPtr<IMFMediaBuffer> buf;
    hr = MFCreate2DMediaBuffer((DWORD)w, (DWORD)h, MFVideoFormat_NV12.Data1, FALSE, &buf);
    if (FAILED(hr)) { ctx->Unmap(stagingBgra_.Get(), 0); lastError_ = "cpu MFCreate2DMediaBuffer"; return false; }
    ComPtr<IMF2DBuffer2> b2;
    buf.As(&b2);
    BYTE* scan0 = nullptr; LONG pitch = 0; BYTE* start = nullptr; DWORD len = 0;
    if (FAILED(b2->Lock2DSize(MF2DBuffer_LockFlags_Write, &scan0, &pitch, &start, &len))) {
        ctx->Unmap(stagingBgra_.Get(), 0); lastError_ = "cpu Lock2DSize"; return false;
    }

    const uint8_t* src = static_cast<const uint8_t*>(map.pData);
    for (int y = 0; y < h; ++y) {
        const uint8_t* row = src + (size_t)y * map.RowPitch;
        uint8_t* dst = scan0 + (size_t)y * pitch;
        for (int x = 0; x < w; ++x) {
            const int b = row[x * 4 + 0], g = row[x * 4 + 1], r = row[x * 4 + 2];
            dst[x] = (uint8_t)(((47 * r + 157 * g + 16 * b + 128) >> 8) + 16);
        }
    }
    uint8_t* uvPlane = scan0 + (size_t)pitch * h;
    for (int y = 0; y < h; y += 2) {
        const uint8_t* r0 = src + (size_t)y * map.RowPitch;
        const uint8_t* r1 = r0 + map.RowPitch;
        uint8_t* uv = uvPlane + (size_t)(y / 2) * pitch;
        for (int x = 0; x < w; x += 2) {
            const int b = (r0[x*4+0] + r0[x*4+4] + r1[x*4+0] + r1[x*4+4] + 2) >> 2;
            const int g = (r0[x*4+1] + r0[x*4+5] + r1[x*4+1] + r1[x*4+5] + 2) >> 2;
            const int r = (r0[x*4+2] + r0[x*4+6] + r1[x*4+2] + r1[x*4+6] + 2) >> 2;
            uv[x]     = (uint8_t)(((-26 * r - 87 * g + 112 * b + 128) >> 8) + 128);
            uv[x + 1] = (uint8_t)(((112 * r - 102 * g - 10 * b + 128) >> 8) + 128);
        }
    }
    b2->Unlock2D();
    ctx->Unmap(stagingBgra_.Get(), 0);
    DWORD contig = 0;
    if (SUCCEEDED(b2->GetContiguousLength(&contig))) buf->SetCurrentLength(contig);

    ComPtr<IMFSample> s;
    MFCreateSample(&s);
    s->AddBuffer(buf.Get());
    s->SetSampleTime((LONGLONG)(tsUs * 10));
    s->SetSampleDuration(sampleDuration_);
    outSample = s;
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
            HRESULT ihr = encoder_->ProcessInput(0, nv12, 0);
            if (FAILED(ihr)) {
                char b[48]; snprintf(b, sizeof(b), "async ProcessInput=0x%08X", (unsigned)ihr);
                lastError_ = b; return false;
            }
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
    encoderEvents_.Reset();
    vproc_.Reset();
    vpEnum_.Reset();
    vcontext_.Reset();
    vdevice_.Reset();
    inCopy_.Reset();
    stagingBgra_.Reset();
    useCpuConvert_ = false;
    nv12TexPool_.clear();
    nv12Idx_ = 0;
    async_ = false;
    dxgiManager_.Reset();
    MFShutdown();
}

} // namespace ssl
