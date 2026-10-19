#ifdef _WIN32
#include "WasapiEac3Output.h"
#include "Eac3BurstReader.h"
#include <windows.h>
// Instantiate SDK property keys before the audio endpoint headers declare them.
#include <initguid.h>
#include <audioclient.h>
#include <mmdeviceapi.h>
#include <functiondiscoverykeys_devpkey.h>
#include <ks.h>
#include <ksmedia.h>
#include <wrl/client.h>
#include <algorithm>
#include <cstring>
#include <stdexcept>

namespace echo {
using Microsoft::WRL::ComPtr;
namespace {
void check(HRESULT result, const char* code) {
    if (FAILED(result)) throw std::runtime_error(code);
}
std::wstring wide(const std::string& text) {
    if (text.empty()) return {};
    const int length = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, text.data(), static_cast<int>(text.size()), nullptr, 0);
    if (!length) throw std::runtime_error("eac3_hdmi_invalid_device_identity");
    std::wstring result(length, L'\0');
    MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, text.data(), static_cast<int>(text.size()), result.data(), length);
    return result;
}
std::string utf8(const wchar_t* text) {
    const int length = WideCharToMultiByte(CP_UTF8, 0, text, -1, nullptr, 0, nullptr, nullptr);
    std::string result(std::max(1, length), '\0');
    WideCharToMultiByte(CP_UTF8, 0, text, -1, result.data(), length, nullptr, nullptr);
    result.resize(std::max(0, length - 1));
    return result;
}
std::wstring endpointName(IMMDevice* device) {
    ComPtr<IPropertyStore> properties;
    check(device->OpenPropertyStore(STGM_READ, &properties), "eac3_hdmi_device_properties_failed");
    PROPVARIANT value;
    PropVariantInit(&value);
    const HRESULT result = properties->GetValue(PKEY_Device_FriendlyName, &value);
    std::wstring name = SUCCEEDED(result) && value.vt == VT_LPWSTR ? value.pwszVal : L"";
    PropVariantClear(&value);
    return name;
}
}
struct WasapiEac3Output::Impl {
    ComPtr<IAudioClient> client;
    ComPtr<IAudioRenderClient> render;
    ComPtr<IAudioClock> clock;
    uint32_t bufferFrames = 0;
    uint64_t frequency = 0;
    std::string name;
};
WasapiEac3Output::WasapiEac3Output() : impl_(std::make_unique<Impl>()) {}
WasapiEac3Output::~WasapiEac3Output() { close(); }
void WasapiEac3Output::close() {
    if (impl_->client) impl_->client->Stop();
    impl_->clock.Reset(); impl_->render.Reset(); impl_->client.Reset();
    impl_->bufferFrames = 0;
}
void WasapiEac3Output::open(const std::string& id, const std::string& name, int channels) {
    close();
    if (channels < 1 || channels > 8) throw std::runtime_error("eac3_hdmi_source_channels_unsupported");
    ComPtr<IMMDeviceEnumerator> enumerator;
    check(CoCreateInstance(__uuidof(MMDeviceEnumerator), nullptr, CLSCTX_ALL, IID_PPV_ARGS(&enumerator)), "eac3_hdmi_enumerator_failed");
    ComPtr<IMMDevice> device;
    if (!id.empty()) {
        check(enumerator->GetDevice(wide(id).c_str(), &device), "eac3_hdmi_device_unavailable");
    } else if (!name.empty()) {
        ComPtr<IMMDeviceCollection> devices;
        check(enumerator->EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE, &devices), "eac3_hdmi_enumeration_failed");
        UINT count = 0;
        check(devices->GetCount(&count), "eac3_hdmi_enumeration_failed");
        if (count > 256) throw std::runtime_error("eac3_hdmi_device_limit_exceeded");
        const auto requested = wide(name);
        for (UINT i = 0; i < count; ++i) {
            ComPtr<IMMDevice> candidate;
            check(devices->Item(i, &candidate), "eac3_hdmi_enumeration_failed");
            if (endpointName(candidate.Get()) == requested) {
                if (device) throw std::runtime_error("eac3_hdmi_device_name_ambiguous");
                device = candidate;
            }
        }
        if (!device) throw std::runtime_error("eac3_hdmi_device_unavailable");
    } else {
        check(enumerator->GetDefaultAudioEndpoint(eRender, eConsole, &device), "eac3_hdmi_device_unavailable");
    }
    ComPtr<IPropertyStore> properties;
    check(device->OpenPropertyStore(STGM_READ, &properties), "eac3_hdmi_device_properties_failed");
    PROPVARIANT form;
    PropVariantInit(&form);
    const HRESULT propertyResult = properties->GetValue(PKEY_AudioEndpoint_FormFactor, &form);
    const bool display = SUCCEEDED(propertyResult) && form.vt == VT_UI4 && form.ulVal == DigitalAudioDisplayDevice;
    PropVariantClear(&form);
    if (!display) throw std::runtime_error("eac3_hdmi_display_endpoint_required");
    impl_->name = utf8(endpointName(device.Get()).c_str());
    check(device->Activate(__uuidof(IAudioClient), CLSCTX_ALL, nullptr, reinterpret_cast<void**>(impl_->client.GetAddressOf())), "eac3_hdmi_device_activate_failed");

    WAVEFORMATEXTENSIBLE_IEC61937 format {};
    auto& wave = format.FormatExt.Format;
    wave.wFormatTag = WAVE_FORMAT_EXTENSIBLE;
    wave.nChannels = 2;
    wave.nSamplesPerSec = eac3CarrierRate;
    wave.nAvgBytesPerSec = eac3CarrierRate * 4;
    wave.nBlockAlign = 4;
    wave.wBitsPerSample = 16;
    wave.cbSize = sizeof(format) - sizeof(WAVEFORMATEX);
    format.FormatExt.Samples.wValidBitsPerSample = 16;
    format.FormatExt.dwChannelMask = channels > 6 ? KSAUDIO_SPEAKER_7POINT1_SURROUND : KSAUDIO_SPEAKER_5POINT1;
    // Receiver discovers JOC from the unchanged E-AC-3 payload. This is an
    // encoded carrier format, never a PCM fallback/closest-match negotiation.
    const GUID subtype { 0x0000000a, 0x0cea, 0x0010, {0x80, 0, 0, 0xaa, 0, 0x38, 0x9b, 0x71} };
    format.FormatExt.SubFormat = subtype;
    format.dwEncodedSamplesPerSec = eac3SourceRate;
    format.dwEncodedChannelCount = channels;
    format.dwAverageBytesPerSec = 0;
    if (impl_->client->IsFormatSupported(AUDCLNT_SHAREMODE_EXCLUSIVE, &wave, nullptr) != S_OK)
        throw std::runtime_error("eac3_hdmi_format_unsupported");
    REFERENCE_TIME period = 0;
    check(impl_->client->GetDevicePeriod(&period, nullptr), "eac3_hdmi_period_query_failed");
    const REFERENCE_TIME duration = std::max<REFERENCE_TIME>(period * 2, 640000);
    HRESULT initialized = impl_->client->Initialize(AUDCLNT_SHAREMODE_EXCLUSIVE, 0, duration, 0, &wave, nullptr);
    if (initialized == AUDCLNT_E_BUFFER_SIZE_NOT_ALIGNED) {
        UINT32 alignedFrames = 0;
        check(impl_->client->GetBufferSize(&alignedFrames), "eac3_hdmi_alignment_query_failed");
        if (!alignedFrames || alignedFrames > eac3CarrierRate)
            throw std::runtime_error("eac3_hdmi_buffer_exceeds_limit");
        const auto alignedDuration = static_cast<REFERENCE_TIME>((10000000ULL * alignedFrames + eac3CarrierRate / 2) / eac3CarrierRate);
        impl_->client.Reset();
        check(device->Activate(__uuidof(IAudioClient), CLSCTX_ALL, nullptr, reinterpret_cast<void**>(impl_->client.GetAddressOf())), "eac3_hdmi_device_activate_failed");
        initialized = impl_->client->Initialize(AUDCLNT_SHAREMODE_EXCLUSIVE, 0, alignedDuration, 0, &wave, nullptr);
    }
    check(initialized, "eac3_hdmi_exclusive_initialize_failed");
    check(impl_->client->GetBufferSize(&impl_->bufferFrames), "eac3_hdmi_buffer_query_failed");
    if (!impl_->bufferFrames || impl_->bufferFrames > eac3CarrierRate)
        throw std::runtime_error("eac3_hdmi_buffer_exceeds_limit");
    check(impl_->client->GetService(IID_PPV_ARGS(&impl_->render)), "eac3_hdmi_render_service_failed");
    check(impl_->client->GetService(IID_PPV_ARGS(&impl_->clock)), "eac3_hdmi_clock_service_failed");
    check(impl_->clock->GetFrequency(&impl_->frequency), "eac3_hdmi_clock_failed");
    if (!impl_->frequency) throw std::runtime_error("eac3_hdmi_clock_failed");
}
uint32_t WasapiEac3Output::paddingFrames() const {
    UINT32 padding = 0;
    check(impl_->client->GetCurrentPadding(&padding), "eac3_hdmi_device_lost");
    return padding;
}
uint32_t WasapiEac3Output::availableFrames() const {
    return impl_->bufferFrames - std::min(impl_->bufferFrames, paddingFrames());
}
void WasapiEac3Output::write(const uint8_t* bytes, uint32_t frames) {
    BYTE* destination = nullptr;
    check(impl_->render->GetBuffer(frames, &destination), "eac3_hdmi_render_failed");
    std::memcpy(destination, bytes, static_cast<std::size_t>(frames) * 4);
    check(impl_->render->ReleaseBuffer(frames, 0), "eac3_hdmi_render_failed");
}
void WasapiEac3Output::start() { check(impl_->client->Start(), "eac3_hdmi_start_failed"); }
void WasapiEac3Output::pause() { check(impl_->client->Stop(), "eac3_hdmi_pause_failed"); }
void WasapiEac3Output::reset() {
    pause();
    check(impl_->client->Reset(), "eac3_hdmi_reset_failed");
}
double WasapiEac3Output::elapsedSeconds() const {
    UINT64 position = 0;
    check(impl_->clock->GetPosition(&position, nullptr), "eac3_hdmi_clock_failed");
    return static_cast<double>(position) / impl_->frequency;
}
const std::string& WasapiEac3Output::deviceName() const { return impl_->name; }
}
#endif
