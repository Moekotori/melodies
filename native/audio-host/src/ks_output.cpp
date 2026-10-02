#ifdef _WIN32

#include "ks_output.h"

#include <windows.h>
#include <avrt.h>
#include <ks.h>
#include <ksmedia.h>
#include <mmreg.h>
#include <setupapi.h>

#include <algorithm>
#include <array>
#include <atomic>
#include <cmath>
#include <cstdio>
#include <cstdint>
#include <cstring>
#include <memory>
#include <string>
#include <thread>
#include <vector>

namespace {

constexpr unsigned packetCount = 4;

enum class SampleKind { Float32, Pcm32, Pcm24, Pcm16 };

struct DeviceRecord {
    std::wstring path;
    std::string name;
    bool waveRt = false;
    std::vector<ULONG> renderPins;
};

struct FormatCandidate {
    WAVEFORMATEXTENSIBLE wave{};
    SampleKind kind = SampleKind::Float32;
    const char* name = "f32";
};

struct Packet {
    KSSTREAM_HEADER header{};
    OVERLAPPED overlapped{};
    std::vector<uint8_t> bytes;
};

struct PinConnectBuffer {
    KSPIN_CONNECT connect{};
    KSDATAFORMAT_WAVEFORMATEX data{};
    uint8_t extension[sizeof(WAVEFORMATEXTENSIBLE) - sizeof(WAVEFORMATEX)]{};
};

void setError(char* error, size_t errorLen, const std::string& message, DWORD code = ERROR_SUCCESS)
{
    if (error == nullptr || errorLen == 0) return;
    const std::string full = code == ERROR_SUCCESS
        ? message
        : message + " (win32=" + std::to_string(static_cast<unsigned long>(code)) + ")";
    std::snprintf(error, errorLen, "%s", full.c_str());
    error[errorLen - 1] = '\0';
}

std::string wideToUtf8(const wchar_t* value)
{
    if (value == nullptr || value[0] == L'\0') return {};
    const int count = WideCharToMultiByte(CP_UTF8, 0, value, -1, nullptr, 0, nullptr, nullptr);
    if (count <= 1) return {};
    std::string result(static_cast<size_t>(count), '\0');
    WideCharToMultiByte(CP_UTF8, 0, value, -1, result.data(), count, nullptr, nullptr);
    result.resize(static_cast<size_t>(count - 1));
    return result;
}

bool queryPinProperty(HANDLE filter, ULONG pinId, ULONG propertyId, void* output, DWORD outputBytes)
{
    KSP_PIN property{};
    property.Property.Set = KSPROPSETID_Pin;
    property.Property.Id = propertyId;
    property.Property.Flags = KSPROPERTY_TYPE_GET;
    property.PinId = pinId;
    DWORD returned = 0;
    return DeviceIoControl(filter, IOCTL_KS_PROPERTY, &property, sizeof(property), output, outputBytes, &returned, nullptr) != FALSE;
}

bool setPinState(HANDLE pin, KSSTATE state)
{
    KSPROPERTY property{};
    property.Set = KSPROPSETID_Connection;
    property.Id = KSPROPERTY_CONNECTION_STATE;
    property.Flags = KSPROPERTY_TYPE_SET;
    DWORD returned = 0;
    return DeviceIoControl(pin, IOCTL_KS_PROPERTY, &property, sizeof(property), &state, sizeof(state), &returned, nullptr) != FALSE;
}

std::string deviceFriendlyName(HDEVINFO set, SP_DEVINFO_DATA& info)
{
    std::array<wchar_t, 512> text{};
    if (!SetupDiGetDeviceRegistryPropertyW(set, &info, SPDRP_FRIENDLYNAME, nullptr,
            reinterpret_cast<PBYTE>(text.data()), static_cast<DWORD>(text.size() * sizeof(wchar_t)), nullptr)) {
        SetupDiGetDeviceRegistryPropertyW(set, &info, SPDRP_DEVICEDESC, nullptr,
            reinterpret_cast<PBYTE>(text.data()), static_cast<DWORD>(text.size() * sizeof(wchar_t)), nullptr);
    }
    return wideToUtf8(text.data());
}

std::vector<DeviceRecord> enumerateRecords()
{
    std::vector<DeviceRecord> records;
    HDEVINFO set = SetupDiGetClassDevsW(&KSCATEGORY_AUDIO, nullptr, nullptr, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
    if (set == INVALID_HANDLE_VALUE) return records;

    for (DWORD index = 0;; ++index) {
        SP_DEVICE_INTERFACE_DATA iface{};
        iface.cbSize = sizeof(iface);
        if (!SetupDiEnumDeviceInterfaces(set, nullptr, &KSCATEGORY_AUDIO, index, &iface)) break;

        SP_DEVICE_INTERFACE_DATA renderAlias{};
        renderAlias.cbSize = sizeof(renderAlias);
        if (!SetupDiGetDeviceInterfaceAlias(set, &iface, &KSCATEGORY_RENDER, &renderAlias)
            || (renderAlias.Flags & SPINT_REMOVED) != 0) continue;

        SP_DEVICE_INTERFACE_DATA realtimeAlias{};
        realtimeAlias.cbSize = sizeof(realtimeAlias);
        const bool waveRt = SetupDiGetDeviceInterfaceAlias(set, &iface, &KSCATEGORY_REALTIME, &realtimeAlias) != FALSE
            && (realtimeAlias.Flags & SPINT_REMOVED) == 0;

        DWORD required = 0;
        SP_DEVINFO_DATA info{};
        info.cbSize = sizeof(info);
        SetupDiGetDeviceInterfaceDetailW(set, &iface, nullptr, 0, &required, &info);
        if (required < sizeof(SP_DEVICE_INTERFACE_DETAIL_DATA_W)) continue;
        std::vector<uint8_t> detailStorage(required);
        auto* detail = reinterpret_cast<SP_DEVICE_INTERFACE_DETAIL_DATA_W*>(detailStorage.data());
        detail->cbSize = sizeof(SP_DEVICE_INTERFACE_DETAIL_DATA_W);
        if (!SetupDiGetDeviceInterfaceDetailW(set, &iface, detail, required, nullptr, &info)) continue;

        HANDLE filter = CreateFileW(detail->DevicePath, GENERIC_READ | GENERIC_WRITE, 0, nullptr,
            OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OVERLAPPED, nullptr);
        if (filter == INVALID_HANDLE_VALUE) continue;

        ULONG pinCount = 0;
        std::vector<ULONG> renderPins;
        if (queryPinProperty(filter, 0, KSPROPERTY_PIN_CTYPES, &pinCount, sizeof(pinCount))) {
            for (ULONG pinId = 0; pinId < pinCount; ++pinId) {
                KSPIN_DATAFLOW flow{};
                KSPIN_COMMUNICATION communication{};
                if (queryPinProperty(filter, pinId, KSPROPERTY_PIN_DATAFLOW, &flow, sizeof(flow))
                    && queryPinProperty(filter, pinId, KSPROPERTY_PIN_COMMUNICATION, &communication, sizeof(communication))
                    && flow == KSPIN_DATAFLOW_IN
                    && (communication == KSPIN_COMMUNICATION_SINK || communication == KSPIN_COMMUNICATION_BOTH)) {
                    renderPins.push_back(pinId);
                }
            }
        }
        CloseHandle(filter);
        if (renderPins.empty()) continue;

        DeviceRecord record;
        record.path = detail->DevicePath;
        record.name = deviceFriendlyName(set, info);
        if (record.name.empty()) record.name = "WDM-KS render filter " + std::to_string(records.size());
        record.waveRt = waveRt;
        record.renderPins = std::move(renderPins);
        records.push_back(std::move(record));
    }
    SetupDiDestroyDeviceInfoList(set);
    return records;
}

FormatCandidate makeFormat(uint32_t rate, uint32_t channels, SampleKind kind)
{
    FormatCandidate candidate;
    candidate.kind = kind;
    auto& wave = candidate.wave;
    wave.Format.nChannels = static_cast<WORD>(channels);
    wave.Format.nSamplesPerSec = rate;
    wave.Format.cbSize = 0;
    switch (kind) {
        case SampleKind::Float32:
            candidate.name = "f32";
            wave.Format.wFormatTag = WAVE_FORMAT_IEEE_FLOAT;
            wave.Format.wBitsPerSample = 32;
            break;
        case SampleKind::Pcm32:
            candidate.name = "s32";
            wave.Format.wFormatTag = WAVE_FORMAT_PCM;
            wave.Format.wBitsPerSample = 32;
            break;
        case SampleKind::Pcm24:
            candidate.name = "s24";
            wave.Format.wFormatTag = WAVE_FORMAT_PCM;
            wave.Format.wBitsPerSample = 24;
            break;
        case SampleKind::Pcm16:
            candidate.name = "s16";
            wave.Format.wFormatTag = WAVE_FORMAT_PCM;
            wave.Format.wBitsPerSample = 16;
            break;
    }
    wave.Format.nBlockAlign = static_cast<WORD>((wave.Format.wBitsPerSample / 8) * channels);
    wave.Format.nAvgBytesPerSec = rate * wave.Format.nBlockAlign;
    return candidate;
}

DWORD createPin(HANDLE filter, ULONG pinId, bool waveRt, const FormatCandidate& format, HANDLE* pin)
{
    PinConnectBuffer buffer{};
    buffer.connect.Interface.Set = KSINTERFACESETID_Standard;
    buffer.connect.Interface.Id = waveRt ? KSINTERFACE_STANDARD_LOOPED_STREAMING : KSINTERFACE_STANDARD_STREAMING;
    buffer.connect.Medium.Set = KSMEDIUMSETID_Standard;
    buffer.connect.Medium.Id = KSMEDIUM_TYPE_ANYINSTANCE;
    buffer.connect.PinId = pinId;
    buffer.connect.Priority.PriorityClass = KSPRIORITY_NORMAL;
    buffer.connect.Priority.PrioritySubClass = 1;
    buffer.data.DataFormat.FormatSize = sizeof(KSDATAFORMAT_WAVEFORMATEX);
    buffer.data.DataFormat.SampleSize = format.wave.Format.nBlockAlign;
    buffer.data.DataFormat.MajorFormat = KSDATAFORMAT_TYPE_AUDIO;
    buffer.data.DataFormat.SubFormat = format.kind == SampleKind::Float32
        ? KSDATAFORMAT_SUBTYPE_IEEE_FLOAT : KSDATAFORMAT_SUBTYPE_PCM;
    buffer.data.DataFormat.Specifier = KSDATAFORMAT_SPECIFIER_WAVEFORMATEX;
    buffer.data.WaveFormatEx = format.wave.Format;
    return KsCreatePin(filter, &buffer.connect, GENERIC_READ | GENERIC_WRITE, pin);
}

float clamped(float value) { return std::max(-1.0f, std::min(1.0f, value)); }

void convertSamples(const float* source, uint8_t* target, size_t samples, SampleKind kind)
{
    if (kind == SampleKind::Float32) {
        std::memcpy(target, source, samples * sizeof(float));
        return;
    }
    for (size_t i = 0; i < samples; ++i) {
        const float value = clamped(source[i]);
        if (kind == SampleKind::Pcm32) {
            const int32_t sample = static_cast<int32_t>(std::llround(static_cast<double>(value) * 2147483647.0));
            std::memcpy(target + i * 4, &sample, 4);
        } else if (kind == SampleKind::Pcm24) {
            const int32_t sample = static_cast<int32_t>(std::lround(value * 8388607.0f));
            target[i * 3] = static_cast<uint8_t>(sample & 0xff);
            target[i * 3 + 1] = static_cast<uint8_t>((sample >> 8) & 0xff);
            target[i * 3 + 2] = static_cast<uint8_t>((sample >> 16) & 0xff);
        } else {
            const int16_t sample = static_cast<int16_t>(std::lround(value * 32767.0f));
            std::memcpy(target + i * 2, &sample, 2);
        }
    }
}

bool registerWaveRtEvent(HANDLE pin, HANDLE event, ULONG propertyId)
{
    KSRTAUDIO_NOTIFICATION_EVENT_PROPERTY property{};
    property.Property.Set = KSPROPSETID_RtAudio;
    property.Property.Id = propertyId;
    property.Property.Flags = KSPROPERTY_TYPE_SET;
    property.NotificationEvent = event;
    DWORD returned = 0;
    return DeviceIoControl(pin, IOCTL_KS_PROPERTY, &property, sizeof(property), &property, sizeof(property), &returned, nullptr) != FALSE;
}

} // namespace

struct ks_runtime {
    HANDLE filter = INVALID_HANDLE_VALUE;
    HANDLE pin = INVALID_HANDLE_VALUE;
    HANDLE stopEvent = nullptr;
    HANDLE waveRtEvent = nullptr;
    HANDLE thread = nullptr;
    bool waveRt = false;
    bool waveRtNotification = false;
    void* waveRtBuffer = nullptr;
    DWORD waveRtBufferBytes = 0;
    volatile ULONG* positionRegister = nullptr;
    std::array<Packet, packetCount> packets;
    std::vector<float> scratch;
    uint32_t sampleRate = 0;
    uint32_t channels = 0;
    uint32_t framesPerPacket = 0;
    uint32_t bytesPerFrame = 0;
    SampleKind sampleKind = SampleKind::Float32;
    ks_render_callback callback = nullptr;
    void* userData = nullptr;
};

namespace {

void fillFrames(ks_runtime* runtime, uint8_t* target, uint32_t frames)
{
    const size_t sampleCount = static_cast<size_t>(frames) * runtime->channels;
    if (runtime->scratch.size() < sampleCount) runtime->scratch.resize(sampleCount);
    std::fill(runtime->scratch.begin(), runtime->scratch.begin() + static_cast<std::ptrdiff_t>(sampleCount), 0.0f);
    runtime->callback(runtime->userData, runtime->scratch.data(), frames, runtime->channels);
    convertSamples(runtime->scratch.data(), target, sampleCount, runtime->sampleKind);
}

bool submitPacket(ks_runtime* runtime, Packet& packet)
{
    ResetEvent(packet.overlapped.hEvent);
    packet.header.DataUsed = static_cast<ULONG>(packet.bytes.size());
    DWORD returned = 0;
    const BOOL ok = DeviceIoControl(runtime->pin, IOCTL_KS_WRITE_STREAM, nullptr, 0,
        &packet.header, packet.header.Size, &returned, &packet.overlapped);
    return ok != FALSE || GetLastError() == ERROR_IO_PENDING;
}

DWORD WINAPI cyclicThread(void* context)
{
    auto* runtime = static_cast<ks_runtime*>(context);
    DWORD taskIndex = 0;
    HANDLE mmcss = AvSetMmThreadCharacteristicsW(L"Pro Audio", &taskIndex);
    if (mmcss != nullptr) AvSetMmThreadPriority(mmcss, AVRT_PRIORITY_CRITICAL);

    std::array<HANDLE, packetCount + 1> waits{};
    waits[0] = runtime->stopEvent;
    for (unsigned i = 0; i < packetCount; ++i) {
        waits[i + 1] = runtime->packets[i].overlapped.hEvent;
        fillFrames(runtime, runtime->packets[i].bytes.data(), runtime->framesPerPacket);
        if (!submitPacket(runtime, runtime->packets[i])) SetEvent(runtime->stopEvent);
    }
    setPinState(runtime->pin, KSSTATE_RUN);

    while (WaitForSingleObject(runtime->stopEvent, 0) != WAIT_OBJECT_0) {
        const DWORD wait = WaitForMultipleObjects(static_cast<DWORD>(waits.size()), waits.data(), FALSE, 1000);
        if (wait == WAIT_OBJECT_0) break;
        if (wait <= WAIT_OBJECT_0 || wait >= WAIT_OBJECT_0 + waits.size()) continue;
        const unsigned index = wait - WAIT_OBJECT_0 - 1;
        DWORD transferred = 0;
        if (!GetOverlappedResult(runtime->pin, &runtime->packets[index].overlapped, &transferred, FALSE)
            && GetLastError() != ERROR_OPERATION_ABORTED) break;
        fillFrames(runtime, runtime->packets[index].bytes.data(), runtime->framesPerPacket);
        if (!submitPacket(runtime, runtime->packets[index])) break;
    }

    if (mmcss != nullptr) AvRevertMmThreadCharacteristics(mmcss);
    return 0;
}

bool queryWaveRtPosition(ks_runtime* runtime, ULONG* position)
{
    if (runtime->positionRegister != nullptr) {
        *position = *runtime->positionRegister;
        return true;
    }
    KSPROPERTY property{};
    property.Set = KSPROPSETID_Audio;
    property.Id = KSPROPERTY_AUDIO_POSITION;
    property.Flags = KSPROPERTY_TYPE_GET;
    KSAUDIO_POSITION value{};
    DWORD returned = 0;
    if (!DeviceIoControl(runtime->pin, IOCTL_KS_PROPERTY, &property, sizeof(property), &value, sizeof(value), &returned, nullptr)) return false;
    *position = static_cast<ULONG>(value.PlayOffset);
    return true;
}

DWORD WINAPI waveRtThread(void* context)
{
    auto* runtime = static_cast<ks_runtime*>(context);
    DWORD taskIndex = 0;
    HANDLE mmcss = AvSetMmThreadCharacteristicsW(L"Pro Audio", &taskIndex);
    if (mmcss != nullptr) AvSetMmThreadPriority(mmcss, AVRT_PRIORITY_CRITICAL);
    const DWORD halfBytes = runtime->waveRtBufferBytes / 2;
    const uint32_t halfFrames = halfBytes / runtime->bytesPerFrame;
    auto* bytes = static_cast<uint8_t*>(runtime->waveRtBuffer);
    fillFrames(runtime, bytes, halfFrames);
    fillFrames(runtime, bytes + halfBytes, halfFrames);
    setPinState(runtime->pin, KSSTATE_RUN);
    ULONG lastHalf = 1;

    while (WaitForSingleObject(runtime->stopEvent, 0) != WAIT_OBJECT_0) {
        if (runtime->waveRtNotification) {
            HANDLE waits[] { runtime->stopEvent, runtime->waveRtEvent };
            if (WaitForMultipleObjects(2, waits, FALSE, 1000) == WAIT_OBJECT_0) break;
        } else {
            const DWORD waitMs = std::max<DWORD>(1, static_cast<DWORD>((500ull * halfFrames) / runtime->sampleRate));
            if (WaitForSingleObject(runtime->stopEvent, waitMs) == WAIT_OBJECT_0) break;
        }
        ULONG position = 0;
        if (!queryWaveRtPosition(runtime, &position)) continue;
        const ULONG playingHalf = (position % runtime->waveRtBufferBytes) >= halfBytes ? 1u : 0u;
        if (playingHalf == lastHalf) continue;
        fillFrames(runtime, bytes + (playingHalf == 0 ? halfBytes : 0), halfFrames);
        MemoryBarrier();
        lastHalf = playingHalf;
    }
    if (mmcss != nullptr) AvRevertMmThreadCharacteristics(mmcss);
    return 0;
}

bool initializeWaveRt(ks_runtime* runtime, uint32_t requestedFrames)
{
    DWORD requestedBytes = std::max<DWORD>(128, requestedFrames * runtime->bytesPerFrame * 2);
    requestedBytes = (requestedBytes + 127u) & ~127u;
    KSRTAUDIO_BUFFER_PROPERTY_WITH_NOTIFICATION input{};
    input.Property.Set = KSPROPSETID_RtAudio;
    input.Property.Id = KSPROPERTY_RTAUDIO_BUFFER_WITH_NOTIFICATION;
    input.Property.Flags = KSPROPERTY_TYPE_GET;
    input.RequestedBufferSize = requestedBytes;
    input.NotificationCount = 2;
    KSRTAUDIO_BUFFER output{};
    DWORD returned = 0;
    if (DeviceIoControl(runtime->pin, IOCTL_KS_PROPERTY, &input, sizeof(input), &output, sizeof(output), &returned, nullptr)) {
        runtime->waveRtNotification = true;
    } else {
        KSRTAUDIO_BUFFER_PROPERTY polled{};
        polled.Property.Set = KSPROPSETID_RtAudio;
        polled.Property.Id = KSPROPERTY_RTAUDIO_BUFFER;
        polled.Property.Flags = KSPROPERTY_TYPE_GET;
        polled.RequestedBufferSize = requestedBytes;
        if (!DeviceIoControl(runtime->pin, IOCTL_KS_PROPERTY, &polled, sizeof(polled), &output, sizeof(output), &returned, nullptr)) return false;
    }
    if (output.BufferAddress == nullptr || output.ActualBufferSize < runtime->bytesPerFrame * 2) return false;
    runtime->waveRtBuffer = output.BufferAddress;
    runtime->waveRtBufferBytes = output.ActualBufferSize - (output.ActualBufferSize % (runtime->bytesPerFrame * 2));
    runtime->waveRtEvent = CreateEventW(nullptr, FALSE, FALSE, nullptr);
    if (runtime->waveRtNotification && (runtime->waveRtEvent == nullptr
        || !registerWaveRtEvent(runtime->pin, runtime->waveRtEvent, KSPROPERTY_RTAUDIO_REGISTER_NOTIFICATION_EVENT))) return false;

    KSRTAUDIO_HWREGISTER_PROPERTY positionInput{};
    positionInput.Property.Set = KSPROPSETID_RtAudio;
    positionInput.Property.Id = KSPROPERTY_RTAUDIO_POSITIONREGISTER;
    positionInput.Property.Flags = KSPROPERTY_TYPE_SET;
    KSRTAUDIO_HWREGISTER positionOutput{};
    if (DeviceIoControl(runtime->pin, IOCTL_KS_PROPERTY, &positionInput, sizeof(positionInput),
            &positionOutput, sizeof(positionOutput), &returned, nullptr)) {
        runtime->positionRegister = static_cast<volatile ULONG*>(positionOutput.Register);
    }
    return true;
}

} // namespace

int ks_output_list_devices(ks_device_info** outDevices, uint32_t* outCount)
{
    if (outDevices == nullptr || outCount == nullptr) return 1;
    *outDevices = nullptr;
    *outCount = 0;
    const auto records = enumerateRecords();
    if (records.empty()) return 0;
    auto* devices = new (std::nothrow) ks_device_info[records.size()]{};
    if (devices == nullptr) return 1;
    for (size_t i = 0; i < records.size(); ++i) {
        wcsncpy_s(devices[i].id, records[i].path.c_str(), _TRUNCATE);
        std::snprintf(devices[i].name, sizeof(devices[i].name), "%s", records[i].name.c_str());
        devices[i].renderPinCount = static_cast<uint32_t>(records[i].renderPins.size());
    }
    *outDevices = devices;
    *outCount = static_cast<uint32_t>(records.size());
    return 0;
}

void ks_output_free_devices(ks_device_info* devices) { delete[] devices; }

int ks_output_start(const char* targetDeviceName, int targetDeviceIndex, uint32_t sampleRate, uint32_t channels,
    uint32_t requestedBufferFrames, ks_render_callback callback, void* userData,
    ks_runtime** outRuntime, ks_ready_info* outInfo, char* error, size_t errorLen)
{
    if (outRuntime == nullptr || callback == nullptr || channels != 2 || sampleRate == 0) {
        setError(error, errorLen, "WDM-KS requires a runtime output, render callback, and stereo PCM format");
        return 1;
    }
    *outRuntime = nullptr;
    const auto devices = enumerateRecords();
    if (devices.empty()) { setError(error, errorLen, "WDM-KS render device enumeration returned no devices"); return 1; }
    int selected = targetDeviceIndex;
    if (selected < 0 && targetDeviceName != nullptr && targetDeviceName[0] != '\0') {
        for (size_t i = 0; i < devices.size(); ++i) if (_stricmp(devices[i].name.c_str(), targetDeviceName) == 0) { selected = static_cast<int>(i); break; }
    }
    if (selected < 0) selected = 0;
    if (selected >= static_cast<int>(devices.size())) { setError(error, errorLen, "WDM-KS device index not found"); return 1; }
    const auto& device = devices[static_cast<size_t>(selected)];

    auto runtime = std::make_unique<ks_runtime>();
    runtime->filter = CreateFileW(device.path.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr,
        OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OVERLAPPED, nullptr);
    if (runtime->filter == INVALID_HANDLE_VALUE) { setError(error, errorLen, "WDM-KS filter open failed", GetLastError()); return 1; }

    FormatCandidate selectedFormat;
    DWORD lastCreateError = ERROR_INVALID_PARAMETER;
    for (ULONG pinId : device.renderPins) {
        for (const SampleKind kind : { SampleKind::Float32, SampleKind::Pcm32, SampleKind::Pcm24, SampleKind::Pcm16 }) {
            const auto candidate = makeFormat(sampleRate, channels, kind);
            HANDLE pin = INVALID_HANDLE_VALUE;
            lastCreateError = createPin(runtime->filter, pinId, device.waveRt, candidate, &pin);
            if (lastCreateError == ERROR_SUCCESS) {
                runtime->pin = pin;
                selectedFormat = candidate;
                break;
            }
        }
        if (runtime->pin != INVALID_HANDLE_VALUE) break;
    }
    if (runtime->pin == INVALID_HANDLE_VALUE) {
        CloseHandle(runtime->filter);
        runtime->filter = INVALID_HANDLE_VALUE;
        setError(error, errorLen, "WDM-KS format unsupported or render pin unavailable", lastCreateError);
        return 1;
    }

    runtime->sampleRate = sampleRate;
    runtime->channels = channels;
    runtime->sampleKind = selectedFormat.kind;
    runtime->bytesPerFrame = selectedFormat.wave.Format.nBlockAlign;
    runtime->framesPerPacket = std::max<uint32_t>(128, requestedBufferFrames == 0 ? 512 : requestedBufferFrames);
    runtime->callback = callback;
    runtime->userData = userData;
    runtime->stopEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (runtime->stopEvent == nullptr || !setPinState(runtime->pin, KSSTATE_ACQUIRE) || !setPinState(runtime->pin, KSSTATE_PAUSE)) {
        setError(error, errorLen, "WDM-KS pin prepare failed", GetLastError());
        ks_output_stop(runtime.release());
        return 1;
    }

    if (device.waveRt)
    {
        if (!initializeWaveRt(runtime.get(), runtime->framesPerPacket))
        {
            setError(error, errorLen, "failed to initialize the WaveRT buffer");
            ks_output_stop(runtime.release());
            return 1;
        }
        runtime->waveRt = true;
    }
    if (!runtime->waveRt) {
        for (auto& packet : runtime->packets) {
            packet.bytes.resize(static_cast<size_t>(runtime->framesPerPacket) * runtime->bytesPerFrame);
            packet.overlapped.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
            packet.header.Size = sizeof(KSSTREAM_HEADER);
            packet.header.Data = packet.bytes.data();
            packet.header.FrameExtent = static_cast<ULONG>(packet.bytes.size());
            packet.header.DataUsed = static_cast<ULONG>(packet.bytes.size());
            packet.header.PresentationTime.Numerator = 1;
            packet.header.PresentationTime.Denominator = 1;
            if (packet.overlapped.hEvent == nullptr) {
                setError(error, errorLen, "WDM-KS stream event creation failed", GetLastError());
                ks_output_stop(runtime.release());
                return 1;
            }
        }
    }
    runtime->thread = CreateThread(nullptr, 0, runtime->waveRt ? waveRtThread : cyclicThread, runtime.get(), 0, nullptr);
    if (runtime->thread == nullptr) {
        setError(error, errorLen, "WDM-KS render thread creation failed", GetLastError());
        ks_output_stop(runtime.release());
        return 1;
    }

    if (outInfo != nullptr) {
        outInfo->sampleRate = sampleRate;
        outInfo->channels = channels;
        outInfo->bufferFrameCount = runtime->waveRt
            ? runtime->waveRtBufferBytes / runtime->bytesPerFrame
            : runtime->framesPerPacket * packetCount;
        std::snprintf(outInfo->format, sizeof(outInfo->format), "%s", selectedFormat.name);
        std::snprintf(outInfo->deviceName, sizeof(outInfo->deviceName), "%s", device.name.c_str());
    }
    *outRuntime = runtime.release();
    return 0;
}

void ks_output_stop(ks_runtime* runtime)
{
    if (runtime == nullptr) return;
    if (runtime->stopEvent != nullptr) SetEvent(runtime->stopEvent);
    if (runtime->pin != INVALID_HANDLE_VALUE) CancelIoEx(runtime->pin, nullptr);
    if (runtime->thread != nullptr) {
        WaitForSingleObject(runtime->thread, 3000);
        CloseHandle(runtime->thread);
    }
    if (runtime->pin != INVALID_HANDLE_VALUE) {
        setPinState(runtime->pin, KSSTATE_PAUSE);
        setPinState(runtime->pin, KSSTATE_STOP);
        if (runtime->waveRtNotification && runtime->waveRtEvent != nullptr)
            registerWaveRtEvent(runtime->pin, runtime->waveRtEvent, KSPROPERTY_RTAUDIO_UNREGISTER_NOTIFICATION_EVENT);
        CloseHandle(runtime->pin);
    }
    for (auto& packet : runtime->packets) if (packet.overlapped.hEvent != nullptr) CloseHandle(packet.overlapped.hEvent);
    if (runtime->waveRtEvent != nullptr) CloseHandle(runtime->waveRtEvent);
    if (runtime->stopEvent != nullptr) CloseHandle(runtime->stopEvent);
    if (runtime->filter != INVALID_HANDLE_VALUE) CloseHandle(runtime->filter);
    delete runtime;
}

#endif
