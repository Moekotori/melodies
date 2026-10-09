#define MINIAUDIO_IMPLEMENTATION
#include "HostCommon.h"
#include "NativeDaemonBootstrap.h"
#include "NativePcmChannelPolicy.h"
#include "MqaPassthroughPolicy.h"
#include "ProcessMemoryTelemetry.h"
#include "RuntimeOutputTransitionCoordinator.h"
#include "RpcOutputWriter.h"
#include "ListenTogetherRpc.h"
#include "WindowsCddaReader.h"
#include <unordered_map>
#if defined(__APPLE__)
#include "MacCoreAudioDeviceInfo.h"
#include "MacCoreAudioDopOutput.h"
#include "MacCoreAudioDeviceWatcher.h"
#include "MacCoreAudioHogMode.h"
#endif
#if defined(__linux__)
#include <alsa/asoundlib.h>
#include "LinuxAlsaNativeDsdOutput.h"
#include "LinuxAlsaDevicePolicy.h"
#include "LinuxPipeWireOutput.h"
#endif

namespace
{
#if defined(_WIN32)
constexpr bool hasWasapiExclusiveOutput = true;
constexpr bool hasAsioOutput = true;
constexpr bool hasKernelStreamingOutput = true;
constexpr bool hasCoreAudioExclusiveOutput = false;
#elif defined(__APPLE__)
constexpr bool hasWasapiExclusiveOutput = false;
constexpr bool hasAsioOutput = false;
constexpr bool hasKernelStreamingOutput = false;
constexpr bool hasCoreAudioExclusiveOutput = true;
#else
constexpr bool hasWasapiExclusiveOutput = false;
constexpr bool hasAsioOutput = false;
constexpr bool hasKernelStreamingOutput = false;
constexpr bool hasCoreAudioExclusiveOutput = false;
#endif

#ifdef _WIN32
void logWindowsError(const std::string& action)
{
    logLine(action + " failed: win32=" + std::to_string(static_cast<unsigned long>(GetLastError())));
}

void configureProcessPriority()
{
    if (! SetPriorityClass(GetCurrentProcess(), ABOVE_NORMAL_PRIORITY_CLASS))
        logWindowsError("SetPriorityClass(ABOVE_NORMAL_PRIORITY_CLASS)");
}

class ScopedTimerResolution final
{
public:
    ScopedTimerResolution()
        : active(timeBeginPeriod(1) == TIMERR_NOERROR)
    {
        if (! active)
            logLine("timeBeginPeriod(1) failed");
    }

    ~ScopedTimerResolution()
    {
        if (active)
            timeEndPeriod(1);
    }

private:
    bool active = false;
};

class ScopedMmcssRegistration final
{
public:
    ScopedMmcssRegistration(const wchar_t* taskName, AVRT_PRIORITY priority)
    {
        DWORD taskIndex = 0;
        handle = AvSetMmThreadCharacteristicsW(taskName, &taskIndex);
        if (handle == nullptr)
        {
            logWindowsError("AvSetMmThreadCharacteristicsW");
            return;
        }

        if (! AvSetMmThreadPriority(handle, priority))
            logWindowsError("AvSetMmThreadPriority");
    }

    ~ScopedMmcssRegistration()
    {
        if (handle != nullptr)
            AvRevertMmThreadCharacteristics(handle);
    }

private:
    HANDLE handle = nullptr;
};

void configureThreadPriority(const wchar_t* taskName, AVRT_PRIORITY priority)
{
    thread_local std::unique_ptr<ScopedMmcssRegistration> registration;
    if (registration == nullptr)
        registration = std::make_unique<ScopedMmcssRegistration>(taskName, priority);
}

void configureAudioCallbackThread()
{
    configureThreadPriority(L"Pro Audio", AVRT_PRIORITY_CRITICAL);
}
#else
class ScopedTimerResolution final {};

void configureProcessPriority() {}
void configureAudioCallbackThread() {}
#endif

#ifdef _WIN32
std::string wideToUtf8(const wchar_t* value)
{
    if (value == nullptr || value[0] == L'\0')
        return {};
    const int required = WideCharToMultiByte(CP_UTF8, 0, value, -1, nullptr, 0, nullptr, nullptr);
    if (required <= 1)
        return {};
    std::string result(static_cast<size_t>(required), '\0');
    WideCharToMultiByte(CP_UTF8, 0, value, -1, result.data(), required, nullptr, nullptr);
    result.resize(static_cast<size_t>(required - 1));
    return result;
}
#endif

double parseDouble(std::string_view value, double fallback)
{
    if (value.empty())
        return fallback;

    try
    {
        return std::stod(std::string(value));
    }
    catch (...)
    {
        return fallback;
    }
}

std::vector<std::string> getCommandLineArgs(int argc, char* argv[])
{
#ifdef _WIN32
    int wideArgc = 0;
    LPWSTR* wideArgv = CommandLineToArgvW(GetCommandLineW(), &wideArgc);
    std::vector<std::string> wideArgs;

    if (wideArgv != nullptr)
    {
        wideArgs.reserve(static_cast<size_t>(wideArgc));

        for (int i = 0; i < wideArgc; ++i)
            wideArgs.emplace_back(wideToUtf8(wideArgv[i]));

        LocalFree(wideArgv);
        return wideArgs;
    }
#endif

    std::vector<std::string> args;
    args.reserve(static_cast<size_t>(std::max(argc, 0)));

    for (int i = 0; i < argc; ++i)
        args.emplace_back(argv[i] != nullptr ? argv[i] : "");

    return args;
}

Options parseOptions(const std::vector<std::string>& args)
{
    Options options;

    for (size_t i = 1; i < args.size(); ++i)
    {
        const auto arg = args[i];

        if (arg == "-list")
        {
            options.list = true;
        }
        else if (arg == "-exclusive")
        {
            options.exclusive = true;
        }
        else if (arg == "-asio")
        {
            options.asio = true;
        }
        else if (arg == "-ks")
        {
            options.kernelStreaming = true;
        }
        else if (arg == "-dop-output")
        {
            options.dopOutput = true;
        }
        else if (arg == "-asio-native-dsd-output")
        {
            options.asioNativeDsdOutput = true;
        }
        else if (arg == "--no-stdin")
        {
            options.noStdin = true;
        }
        else if (arg == "--defer-device-open")
        {
            options.deviceOpenDeferred = true;
        }
        else if (arg == "--defer-channel-init")
        {
            options.channelInitializationDeferred = true;
        }
        else if (arg == "--allow-pro-dsp")
        {
            options.proDspAllowed = true;
        }
        else if (arg == "-decode-pcm" && i + 1 < args.size())
        {
            options.decodePcm = true;
            options.decodeFile = args[++i];
        }
        else if (arg == "--cdda-toc" && i + 1 < args.size())
        {
            options.cddaToc = true;
            options.cddaDrive = args[++i];
        }
        else if (arg == "--cdda-read" && i + 1 < args.size())
        {
            options.cddaRead = true;
            options.cddaDrive = args[++i];
        }
        else if (arg == "--cdda-eject" && i + 1 < args.size())
        {
            options.cddaEject = true;
            options.cddaDrive = args[++i];
        }
        else if (arg == "--cdda-last-track" && i + 1 < args.size())
            options.cddaLastTrack = parseInt(args[++i], 0);
        else if (arg == "--cdda-expected-start" && i + 1 < args.size())
            options.cddaExpectedStartLba = parseInt(args[++i], -1);
        else if (arg == "--cdda-expected-end" && i + 1 < args.size())
            options.cddaExpectedEndLba = parseInt(args[++i], -1);
        else if (arg == "--cdda-capabilities")
        {
            options.cddaCapabilities = true;
        }
        else if (arg == "--cdda-status" && i + 1 < args.size())
        {
            options.cddaStatus = true;
            options.cddaDrive = args[++i];
        }
        else if (arg == "--probe-asio-sdm-families")
        {
            options.asioSdmFamilyProbe = true;
        }
        else if (arg == "--build-capabilities")
        {
            options.buildCapabilities = true;
        }
        else if (arg == "--track" && i + 1 < args.size())
        {
            options.cddaTrack = parseInt(args[++i], 0);
        }
        else if (arg == "-ss" && i + 1 < args.size())
        {
            options.decodeStartSeconds = std::max(0.0, parseDouble(args[++i], options.decodeStartSeconds));
        }
        else if (arg == "-sr" && i + 1 < args.size())
        {
            options.sampleRate = std::max(1, parseInt(args[++i], options.sampleRate));
        }
        else if (arg == "-ch" && i + 1 < args.size())
        {
            options.channels = std::max(1, std::min(8, parseInt(args[++i], options.channels)));
        }
        else if (arg == "-device-index" && i + 1 < args.size())
        {
            options.deviceIndex = parseInt(args[++i], -1);
        }
        else if (arg == "-device" && i + 1 < args.size())
        {
            options.deviceName = args[++i];
            options.deviceId = options.deviceName;
        }
        else if ((arg == "-buffer" || arg == "-buffer-size") && i + 1 < args.size())
        {
            options.bufferSize = std::max(0, parseInt(args[++i], options.bufferSize));
        }
        else if (arg == "-asio-output-channel-start" && i + 1 < args.size())
        {
            options.asioOutputChannelStart = std::max(0, parseInt(args[++i], options.asioOutputChannelStart));
        }
        else if (arg == "-fifo-ms" && i + 1 < args.size())
        {
            options.fifoCapacityMs = std::max(0, parseInt(args[++i], options.fifoCapacityMs));
        }
        else if (arg == "-prebuffer-ms" && i + 1 < args.size())
        {
            options.startupPrebufferMsSpecified = true;
            options.startupPrebufferMs = std::max(0, parseInt(args[++i], options.startupPrebufferMs));
        }
        else if (arg == "-prebuffer-timeout-ms" && i + 1 < args.size())
        {
            options.startupPrebufferTimeoutMsSpecified = true;
            options.startupPrebufferTimeoutMs = std::max(0, parseInt(args[++i], options.startupPrebufferTimeoutMs));
        }
        else if (arg == "-eq-port" && i + 1 < args.size())
        {
            options.eqControlPort = std::max(0, parseInt(args[++i], options.eqControlPort));
        }
        else if (arg == "--rpc-stdin-fd" && i + 1 < args.size())
        {
            options.rpcStdinFd = std::max(0, parseInt(args[++i], options.rpcStdinFd));
        }
        else if (arg == "--rpc-stdout-fd" && i + 1 < args.size())
        {
            options.rpcStdoutFd = std::max(0, parseInt(args[++i], options.rpcStdoutFd));
        }
        else if (arg == "--pcm-input-fd" && i + 1 < args.size())
        {
            options.pcmInputFd = std::max(0, parseInt(args[++i], options.pcmInputFd));
        }
        else if (arg == "--listen-together-output-fd" && i + 1 < args.size())
        {
            options.listenTogetherOutputFd = std::max(0, parseInt(args[++i], options.listenTogetherOutputFd));
        }
        else if (arg == "--listen-together-input-fd" && i + 1 < args.size())
        {
            options.listenTogetherInputFd = std::max(0, parseInt(args[++i], options.listenTogetherInputFd));
        }
        else if (arg == "-vol" && i + 1 < args.size())
        {
            options.volume = std::max(0.0, std::min(1.0, parseDouble(args[++i], options.volume)));
        }
        else if (arg == "-shared-backend" && i + 1 < args.size())
        {
            auto value = args[++i];
            std::transform(value.begin(), value.end(), value.begin(), [] (unsigned char ch) { return static_cast<char>(std::tolower(ch)); });
            if (value == "auto" || value == "windows" || value == "directsound" || value == "alsa" || value == "miniaudio" || value == "pipewire")
                options.sharedBackend = value;
        }
    }

    return options;
}

void writeErrorEvent(const std::string& message, const std::string& reason = "runtime_error")
{
    writeJsonLine(
        "{\"event\":\"error\",\"reason\":\"" + jsonEscape(reason)
        + "\",\"message\":\"" + jsonEscape(message) + "\"}");
}

int getDeviceBufferSize(const Options& options)
{
    if (options.bufferSize > 0)
        return options.bufferSize;

    return 256;
}

std::vector<int> buildBufferSizeAttempts(int requested)
{
    static constexpr int supportedSizes[] { 64, 128, 256, 512, 1024, 2048, 4096, 8192 };
    requested = std::max(1, requested);
    std::vector<int> attempts;
    attempts.push_back(requested);
    for (const int candidate : supportedSizes)
        if (candidate > requested)
            attempts.push_back(candidate);
    return attempts;
}

std::vector<int> buildBufferSizeAttempts(const Options& options)
{
    return buildBufferSizeAttempts(getDeviceBufferSize(options));
}

std::vector<int> buildPreferredBufferSizeAttempts(int requested, int preferred)
{
    auto attempts = buildBufferSizeAttempts(requested);
    if (preferred <= 0 || preferred == requested)
        return attempts;

    attempts.erase(std::remove(attempts.begin(), attempts.end(), preferred), attempts.end());
    attempts.insert(attempts.begin(), preferred);
    return attempts;
}

bool isDisabledSharedBackend(const Options& options)
{
#ifdef _WIN32
    return options.sharedBackend == "alsa" || options.sharedBackend == "pipewire";
#elif defined(__linux__)
    return options.sharedBackend == "windows" || options.sharedBackend == "directsound";
#else
    return options.sharedBackend == "windows" || options.sharedBackend == "directsound" || options.sharedBackend == "alsa" || options.sharedBackend == "pipewire";
#endif
}

int waitForInitialPcm(PcmRingAudioSource& source, int targetFrames, int timeoutMs)
{
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::milliseconds(std::max(0, timeoutMs));
    while (source.getReadyFrames() < targetFrames && std::chrono::steady_clock::now() < deadline)
        std::this_thread::sleep_for(std::chrono::milliseconds(2));
    return source.getReadyFrames();
}

void pushPcmPayload(PcmRingAudioSource& source, int channels, std::vector<char>& pending, const std::vector<char>& payload)
{
    if (channels <= 0 || payload.empty())
        return;
    pending.insert(pending.end(), payload.begin(), payload.end());
    const size_t frameBytes = static_cast<size_t>(channels) * sizeof(float);
    const size_t completeBytes = (pending.size() / frameBytes) * frameBytes;
    if (completeBytes == 0)
        return;
    std::vector<float> samples(completeBytes / sizeof(float));
    std::memcpy(samples.data(), pending.data(), completeBytes);
    source.push(samples.data(), static_cast<int>(completeBytes / frameBytes));
    pending.erase(pending.begin(), pending.begin() + static_cast<std::ptrdiff_t>(completeBytes));
}

int framesForMilliseconds(int sampleRate, int milliseconds)
{
    if (sampleRate <= 0 || milliseconds <= 0)
        return 0;

    return std::max(1, static_cast<int>(std::round((static_cast<double>(sampleRate) * milliseconds) / 1000.0)));
}

int getFifoCapacityFrames(const Options& options, int sampleRate)
{
    const int requestedFrames = framesForMilliseconds(sampleRate, options.fifoCapacityMs);

    if (requestedFrames > 0)
        return std::max(requestedFrames, getDeviceBufferSize(options) * 2);

    if (options.exclusive && sampleRate >= 176400)
        return framesForMilliseconds(sampleRate, 750);

    return std::max(sampleRate / 5, 4096);
}

int getStartupPrebufferFrames(const Options& options, int sampleRate)
{
    if (options.startupPrebufferMsSpecified)
        return framesForMilliseconds(sampleRate, options.startupPrebufferMs);

    const int requestedFrames = framesForMilliseconds(sampleRate, options.startupPrebufferMs);

    if (requestedFrames > 0)
        return requestedFrames;

    if (options.exclusive && sampleRate >= 176400)
        return framesForMilliseconds(sampleRate, 180);

    if (options.exclusive || options.asio)
        return std::max(1, std::min(sampleRate / 50, 4096));

    return 0;
}

int nativeDsdByteFramesForBitFrames(int bitFrames)
{
    if (bitFrames <= 0)
        return 0;

    return std::max(1, static_cast<int>(std::ceil(static_cast<double>(bitFrames) / 8.0)));
}

int getNativeDsdFifoCapacityByteFrames(const Options& options, int nativeSampleRate)
{
    return nativeDsdByteFramesForBitFrames(getFifoCapacityFrames(options, nativeSampleRate));
}

int getNativeDsdStartupPrebufferByteFrames(const Options& options, int nativeSampleRate)
{
    if (! options.startupPrebufferMsSpecified && options.startupPrebufferMs <= 0)
        return nativeDsdByteFramesForBitFrames(framesForMilliseconds(nativeSampleRate, 20));

    return nativeDsdByteFramesForBitFrames(getStartupPrebufferFrames(options, nativeSampleRate));
}

constexpr int kDeferredDaemonFifoMs = 8000;
constexpr int kDeferredDaemonNativeDsdFifoMs = 300;
constexpr int kDeferredDaemonRingPlaceholderFrames = 1;

int getDeferredDaemonPcmFifoCapacityFrames(int sampleRate)
{
    return framesForMilliseconds(sampleRate, kDeferredDaemonFifoMs);
}

int getDeferredDaemonNativeDsdFifoCapacityByteFrames(int nativeSampleRate)
{
    return nativeDsdByteFramesForBitFrames(
        framesForMilliseconds(nativeSampleRate, kDeferredDaemonNativeDsdFifoMs));
}

void applyDeferredDaemonRingCapacities(
    PcmRingAudioSource& source,
    DopRingSource& dopSource,
    NativeDsdRingSource& nativeDsdSource,
    int sampleRate,
    bool dopOutput,
    bool nativeDsdOutput,
    int& fifoCapacityFrames,
    int& nativeDsdFifoCapacityByteFrames)
{
    const int nextPcmFrames = nativeDsdOutput
        ? kDeferredDaemonRingPlaceholderFrames
        : std::max(1, getDeferredDaemonPcmFifoCapacityFrames(sampleRate));
    if (source.getCapacityFrames() != nextPcmFrames)
        source.reconfigureCapacity(nextPcmFrames);
    fifoCapacityFrames = nextPcmFrames;

    const int nextDopFrames = dopOutput && ! nativeDsdOutput
        ? nextPcmFrames
        : kDeferredDaemonRingPlaceholderFrames;
    if (dopSource.getCapacityFrames() != nextDopFrames)
        dopSource.reconfigureCapacity(nextDopFrames);

    const int nextDsdByteFrames = nativeDsdOutput
        ? std::max(1, getDeferredDaemonNativeDsdFifoCapacityByteFrames(sampleRate))
        : kDeferredDaemonRingPlaceholderFrames;
    if (nativeDsdSource.getCapacityByteFrames() != nextDsdByteFrames)
        nativeDsdSource.reconfigureCapacity(nextDsdByteFrames);
    nativeDsdFifoCapacityByteFrames = nextDsdByteFrames;
}

int getStartupPrebufferTimeoutMs(const Options& options)
{
    if (options.startupPrebufferTimeoutMsSpecified)
        return options.startupPrebufferTimeoutMs;

    if (options.startupPrebufferTimeoutMs > 0)
        return options.startupPrebufferTimeoutMs;

    return 300;
}

#ifdef _WIN32
const PROPERTYKEY echoPkeyDeviceFriendlyName = {
    { 0xa45c254e, 0xdf1c, 0x4efd, { 0x80, 0x20, 0x67, 0xd1, 0x46, 0xa8, 0x50, 0xe0 } },
    14
};

const PROPERTYKEY echoPkeyDeviceInstanceId = {
    { 0x78c34fc8, 0x104a, 0x4aca, { 0x9e, 0xa4, 0x52, 0x4d, 0x52, 0x99, 0x6e, 0x57 } },
    256
};

const PROPERTYKEY echoPkeyAudioEndpointFormFactor = {
    { 0x1da5d803, 0xd492, 0x4edd, { 0x8c, 0x23, 0xe0, 0xc0, 0xff, 0xee, 0x7f, 0x0e } },
    0
};

const DEVPROPKEY echoDevpkeyDeviceBusTypeGuid = {
    { 0xa45c254e, 0xdf1c, 0x4efd, { 0x80, 0x20, 0x67, 0xd1, 0x46, 0xa8, 0x50, 0xe0 } },
    21
};

const DEVPROPKEY echoDevpkeyDeviceEnumeratorName = {
    { 0xa45c254e, 0xdf1c, 0x4efd, { 0x80, 0x20, 0x67, 0xd1, 0x46, 0xa8, 0x50, 0xe0 } },
    24
};

const DEVPROPKEY echoDevpkeyDeviceParent = {
    { 0x4340a6c5, 0x93fa, 0x4706, { 0x97, 0x2c, 0x7b, 0x64, 0x80, 0x08, 0xa5, 0xa7 } },
    8
};

const GUID echoBluetoothBusTypeGuid = {
    0xe0cbf06c, 0xcd8b, 0x4647, { 0xbb, 0x8a, 0x26, 0x3b, 0x43, 0xf0, 0xf9, 0x74 }
};

class ScopedComInitializer final
{
public:
    ScopedComInitializer()
    {
        result = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        ownsInitialisation = SUCCEEDED(result);
    }

    ~ScopedComInitializer()
    {
        if (ownsInitialisation)
            CoUninitialize();
    }

    bool canUseCom() const
    {
        return SUCCEEDED(result) || result == RPC_E_CHANGED_MODE;
    }

private:
    HRESULT result = E_FAIL;
    bool ownsInitialisation = false;
};

struct CoreAudioEndpoint
{
    std::string id;
    std::string name;
    int mixSampleRate = 0;
    bool isDefault = false;
    std::string connectionType = "unknown";
    std::string formFactor = "unknown";
};

std::string getEndpointId(IMMDevice* device)
{
    if (device == nullptr)
        return {};

    LPWSTR rawId = nullptr;
    if (FAILED(device->GetId(&rawId)) || rawId == nullptr)
    {
        if (rawId != nullptr)
            CoTaskMemFree(rawId);
        return {};
    }

    std::string id = wideToUtf8(rawId);
    CoTaskMemFree(rawId);
    return id;
}

std::string getEndpointFriendlyName(IMMDevice* device)
{
    if (device == nullptr)
        return {};

    Microsoft::WRL::ComPtr<IPropertyStore> properties;
    if (FAILED(device->OpenPropertyStore(STGM_READ, properties.GetAddressOf())))
        return {};

    PROPVARIANT value;
    PropVariantInit(&value);

    std::string name;
    if (SUCCEEDED(properties->GetValue(echoPkeyDeviceFriendlyName, &value)) && value.vt == VT_LPWSTR && value.pwszVal != nullptr)
        name = wideToUtf8(value.pwszVal);

    PropVariantClear(&value);
    return name;
}

std::wstring getEndpointStringProperty(IMMDevice* device, const PROPERTYKEY& key)
{
    if (device == nullptr)
        return {};

    Microsoft::WRL::ComPtr<IPropertyStore> properties;
    if (FAILED(device->OpenPropertyStore(STGM_READ, properties.GetAddressOf())))
        return {};

    PROPVARIANT value;
    PropVariantInit(&value);
    std::wstring result;
    if (SUCCEEDED(properties->GetValue(key, &value)) && value.vt == VT_LPWSTR && value.pwszVal != nullptr)
        result = value.pwszVal;
    PropVariantClear(&value);
    return result;
}

std::string getEndpointFormFactor(IMMDevice* device)
{
    if (device == nullptr)
        return "unknown";

    Microsoft::WRL::ComPtr<IPropertyStore> properties;
    if (FAILED(device->OpenPropertyStore(STGM_READ, properties.GetAddressOf())))
        return "unknown";

    PROPVARIANT value;
    PropVariantInit(&value);
    unsigned long formFactor = 10;
    if (SUCCEEDED(properties->GetValue(echoPkeyAudioEndpointFormFactor, &value)))
    {
        if (value.vt == VT_UI4)
            formFactor = value.ulVal;
        else if (value.vt == VT_I4 && value.lVal >= 0)
            formFactor = static_cast<unsigned long>(value.lVal);
    }
    PropVariantClear(&value);

    switch (formFactor)
    {
        case 1: return "speakers";
        case 3: return "headphones";
        case 5: return "headset";
        case 7:
        case 8: return "digital";
        case 9: return "display";
        default: return "unknown";
    }
}

bool getDeviceNodeStringProperty(DEVINST device, const DEVPROPKEY& key, std::wstring& result)
{
    wchar_t buffer[512]{};
    DEVPROPTYPE propertyType = 0;
    ULONG bufferSize = sizeof(buffer);
    if (CM_Get_DevNode_PropertyW(device, &key, &propertyType, reinterpret_cast<PBYTE>(buffer), &bufferSize, 0) != CR_SUCCESS
        || propertyType != DEVPROP_TYPE_STRING)
        return false;

    result = buffer;
    return ! result.empty();
}

bool getDeviceNodeBusType(DEVINST device, GUID& result)
{
    DEVPROPTYPE propertyType = 0;
    ULONG bufferSize = sizeof(result);
    return CM_Get_DevNode_PropertyW(
        device,
        &echoDevpkeyDeviceBusTypeGuid,
        &propertyType,
        reinterpret_cast<PBYTE>(&result),
        &bufferSize,
        0) == CR_SUCCESS
        && propertyType == DEVPROP_TYPE_GUID;
}

bool isBluetoothDeviceInstance(const std::wstring& instanceId)
{
    if (instanceId.empty())
        return false;

    std::wstring mutableInstanceId = instanceId;
    DEVINST device = 0;
    if (CM_Locate_DevNodeW(&device, mutableInstanceId.data(), CM_LOCATE_DEVNODE_NORMAL) != CR_SUCCESS)
        return false;

    for (int depth = 0; depth < 16; ++depth)
    {
        wchar_t currentId[MAX_DEVICE_ID_LEN]{};
        if (CM_Get_Device_IDW(device, currentId, MAX_DEVICE_ID_LEN, 0) == CR_SUCCESS)
        {
            const auto currentIdUtf8 = wideToUtf8(currentId);
            if (containsIgnoreCase(currentIdUtf8, "BTHENUM\\")
                || containsIgnoreCase(currentIdUtf8, "BTHLEDEVICE\\")
                || containsIgnoreCase(currentIdUtf8, "BLUETOOTH"))
                return true;
        }

        std::wstring enumeratorName;
        if (getDeviceNodeStringProperty(device, echoDevpkeyDeviceEnumeratorName, enumeratorName))
        {
            const auto enumeratorNameUtf8 = wideToUtf8(enumeratorName.c_str());
            if (containsIgnoreCase(enumeratorNameUtf8, "BTH") || containsIgnoreCase(enumeratorNameUtf8, "BLUETOOTH"))
                return true;
        }

        std::wstring parentInstanceId;
        if (getDeviceNodeStringProperty(device, echoDevpkeyDeviceParent, parentInstanceId))
        {
            const auto parentInstanceIdUtf8 = wideToUtf8(parentInstanceId.c_str());
            if (containsIgnoreCase(parentInstanceIdUtf8, "BTHENUM\\")
                || containsIgnoreCase(parentInstanceIdUtf8, "BTHHFENUM\\")
                || containsIgnoreCase(parentInstanceIdUtf8, "BTHLEDEVICE\\")
                || containsIgnoreCase(parentInstanceIdUtf8, "BLUETOOTH"))
                return true;
        }

        GUID busType{};
        if (getDeviceNodeBusType(device, busType) && IsEqualGUID(busType, echoBluetoothBusTypeGuid))
            return true;

        DEVINST parent = 0;
        if (CM_Get_Parent(&parent, device, 0) != CR_SUCCESS)
            break;
        device = parent;
    }

    return false;
}

std::string getEndpointConnectionType(
    IMMDevice* device,
    const std::string& endpointId,
    const std::string& friendlyName)
{
    auto instanceId = getEndpointStringProperty(device, echoPkeyDeviceInstanceId);
    if (instanceId.empty() && ! endpointId.empty())
    {
        const std::string pnpInstanceId = "SWD\\MMDEVAPI\\" + endpointId;
        instanceId.assign(pnpInstanceId.begin(), pnpInstanceId.end());
    }
    if (isBluetoothDeviceInstance(instanceId)
        || containsIgnoreCase(friendlyName, "bluetooth")
        || friendlyName.find("蓝牙") != std::string::npos)
        return "bluetooth";
    return "unknown";
}

int getEndpointMixSampleRate(IMMDevice* device)
{
    if (device == nullptr)
        return 0;

    Microsoft::WRL::ComPtr<IAudioClient> audioClient;
    if (FAILED(device->Activate(__uuidof(IAudioClient), CLSCTX_ALL, nullptr, reinterpret_cast<void**>(audioClient.GetAddressOf()))))
        return 0;

    WAVEFORMATEX* mixFormat = nullptr;
    if (FAILED(audioClient->GetMixFormat(&mixFormat)) || mixFormat == nullptr)
    {
        if (mixFormat != nullptr)
            CoTaskMemFree(mixFormat);
        return 0;
    }

    const int sampleRate = mixFormat->nSamplesPerSec > 0
        ? static_cast<int>(mixFormat->nSamplesPerSec)
        : 0;
    CoTaskMemFree(mixFormat);
    return sampleRate;
}

std::string getDefaultEndpointId(IMMDeviceEnumerator& enumerator)
{
    Microsoft::WRL::ComPtr<IMMDevice> defaultDevice;

    if (SUCCEEDED(enumerator.GetDefaultAudioEndpoint(eRender, eMultimedia, defaultDevice.GetAddressOf())))
        return getEndpointId(defaultDevice.Get());

    defaultDevice.Reset();
    if (SUCCEEDED(enumerator.GetDefaultAudioEndpoint(eRender, eConsole, defaultDevice.GetAddressOf())))
        return getEndpointId(defaultDevice.Get());

    return {};
}

std::vector<CoreAudioEndpoint> enumerateCoreAudioRenderEndpoints()
{
    ScopedComInitializer com;
    if (! com.canUseCom())
        return {};

    Microsoft::WRL::ComPtr<IMMDeviceEnumerator> enumerator;
    if (FAILED(CoCreateInstance(
            __uuidof(MMDeviceEnumerator),
            nullptr,
            CLSCTX_ALL,
            IID_PPV_ARGS(enumerator.GetAddressOf()))))
        return {};

    const auto defaultId = getDefaultEndpointId(*enumerator.Get());

    Microsoft::WRL::ComPtr<IMMDeviceCollection> collection;
    if (FAILED(enumerator->EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE, collection.GetAddressOf())))
        return {};

    UINT count = 0;
    if (FAILED(collection->GetCount(&count)))
        return {};

    std::vector<CoreAudioEndpoint> endpoints;
    endpoints.reserve(count);

    for (UINT i = 0; i < count; ++i)
    {
        Microsoft::WRL::ComPtr<IMMDevice> endpoint;
        if (FAILED(collection->Item(i, endpoint.GetAddressOf())))
            continue;

        CoreAudioEndpoint descriptor;
        descriptor.id = getEndpointId(endpoint.Get());
        descriptor.name = getEndpointFriendlyName(endpoint.Get());
        descriptor.mixSampleRate = getEndpointMixSampleRate(endpoint.Get());
        descriptor.isDefault = ! defaultId.empty() && descriptor.id == defaultId;
        descriptor.connectionType = getEndpointConnectionType(endpoint.Get(), descriptor.id, descriptor.name);
        descriptor.formFactor = getEndpointFormFactor(endpoint.Get());
        endpoints.push_back(std::move(descriptor));
    }

    return endpoints;
}

bool isCoreAudioEndpointNameMatch(const std::string& endpointName, const std::string& deviceName)
{
    return ! endpointName.empty()
        && ! deviceName.empty()
        && (endpointName == deviceName
            || containsIgnoreCase(endpointName, deviceName)
            || containsIgnoreCase(deviceName, endpointName));
}

const CoreAudioEndpoint* findCoreAudioEndpoint(
    const std::vector<CoreAudioEndpoint>& endpoints,
    const DeviceDescriptor& device)
{
    auto byId = std::find_if(endpoints.begin(), endpoints.end(), [&] (const CoreAudioEndpoint& endpoint)
    {
        return ! endpoint.id.empty()
            && device.stableId.size() >= endpoint.id.size()
            && device.stableId.ends_with(endpoint.id);
    });

    if (byId != endpoints.end())
        return &*byId;

    auto exact = std::find_if(endpoints.begin(), endpoints.end(), [&] (const CoreAudioEndpoint& endpoint)
    {
        return endpoint.name == device.name;
    });

    if (exact != endpoints.end())
        return &*exact;

    auto loose = std::find_if(endpoints.begin(), endpoints.end(), [&] (const CoreAudioEndpoint& endpoint)
    {
        return isCoreAudioEndpointNameMatch(endpoint.name, device.name);
    });

    return loose != endpoints.end() ? &*loose : nullptr;
}

int getFallbackSharedSampleRate()
{
    return 48000;
}

void applyCoreAudioDeviceMetadata(std::vector<DeviceDescriptor>& devices, bool applySharedSampleRates)
{
    const auto endpoints = enumerateCoreAudioRenderEndpoints();
    const auto defaultEndpoint = std::find_if(endpoints.begin(), endpoints.end(), [] (const CoreAudioEndpoint& endpoint)
    {
        return endpoint.isDefault && endpoint.mixSampleRate > 0;
    });

    for (auto& device : devices)
    {
        int sampleRate = 0;

        if (const auto* endpoint = findCoreAudioEndpoint(endpoints, device))
        {
            sampleRate = endpoint->mixSampleRate;
            device.isDefault = device.isDefault || endpoint->isDefault;
            device.connectionType = endpoint->connectionType;
            device.formFactor = endpoint->formFactor;
        }

        if (! applySharedSampleRates)
            continue;

        if (sampleRate <= 0 && device.isDefault && defaultEndpoint != endpoints.end())
            sampleRate = defaultEndpoint->mixSampleRate;

        if (sampleRate <= 0)
            sampleRate = getFallbackSharedSampleRate();

        device.sampleRate = sampleRate;
        device.sharedSampleRate = sampleRate;
    }
}

#else
int getFallbackSharedSampleRate()
{
    return 48000;
}

void applyCoreAudioDeviceMetadata(std::vector<DeviceDescriptor>& devices, bool applySharedSampleRates)
{
    for (auto& device : devices)
    {
        if (! applySharedSampleRates)
            continue;
        if (device.sharedSampleRate <= 0)
            device.sharedSampleRate = getFallbackSharedSampleRate();

        if (device.sampleRate <= 0)
            device.sampleRate = device.sharedSampleRate;
    }
}
#endif


struct MiniaudioContextScope
{
    ma_context context{};
    bool initialized = false;

    ~MiniaudioContextScope()
    {
        if (initialized)
            ma_context_uninit(&context);
    }
};

bool initMiniaudioOutputContext(
    ma_context& context,
    std::string_view sharedBackend,
    std::string& error,
    bool exclusive = false)
{
#ifdef _WIN32
    (void)exclusive;
    const ma_backend backends[] = {
        sharedBackend == "directsound" ? ma_backend_dsound : ma_backend_wasapi
    };
    const ma_result result = ma_context_init(backends, 1, nullptr, &context);
#elif defined(__linux__)
    ma_result result;
    if (exclusive || sharedBackend == "alsa")
    {
        ma_context_config config = ma_context_config_init();
        const ma_backend backends[] = { ma_backend_alsa };
        result = ma_context_init(backends, 1, &config, &context);
    }
    else
    {
        result = ma_context_init(nullptr, 0, nullptr, &context);
    }
#else
    (void)exclusive;
    const ma_result result = ma_context_init(nullptr, 0, nullptr, &context);
#endif
    if (result != MA_SUCCESS)
    {
        error = std::string("miniaudio shared context init failed: ") + ma_result_description(result);
        return false;
    }
    return true;
}

bool initMiniaudioOutputContext(
    MiniaudioContextScope& scope,
    std::string_view sharedBackend,
    std::string& error,
    bool exclusive = false)
{
    if (! initMiniaudioOutputContext(scope.context, sharedBackend, error, exclusive))
        return false;
    scope.initialized = true;
    return true;
}

int pickMiniaudioDeviceSampleRate(const ma_device_info& info)
{
#if defined(__APPLE__)
    const int nominalSampleRate = getMacCoreAudioNominalSampleRateForUid(info.id.coreaudio);
    if (nominalSampleRate > 0)
        return nominalSampleRate;
#endif
    for (ma_uint32 i = 0; i < info.nativeDataFormatCount; ++i)
        if (info.nativeDataFormats[i].sampleRate == 48000)
            return 48000;
    for (ma_uint32 i = 0; i < info.nativeDataFormatCount; ++i)
        if (info.nativeDataFormats[i].sampleRate > 0)
            return static_cast<int>(info.nativeDataFormats[i].sampleRate);
    return getFallbackSharedSampleRate();
}

int pickMiniaudioExclusiveSampleRate(const ma_device_info& info)
{
    int highest = 0;
    for (ma_uint32 i = 0; i < info.nativeDataFormatCount; ++i)
        if (info.nativeDataFormats[i].sampleRate > static_cast<ma_uint32>(highest))
            highest = static_cast<int>(info.nativeDataFormats[i].sampleRate);
#if defined(__linux__)
    if (info.id.alsa[0] != '\0')
    {
        std::string pcmName(info.id.alsa);
        if (pcmName.front() == ':')
            pcmName.insert(0, "hw");
        snd_pcm_t* pcm = nullptr;
        if (snd_pcm_open(&pcm, pcmName.c_str(), SND_PCM_STREAM_PLAYBACK, SND_PCM_NONBLOCK) >= 0)
        {
            snd_pcm_hw_params_t* params = nullptr;
            snd_pcm_hw_params_alloca(&params);
            unsigned int maxRate = 0;
            if (snd_pcm_hw_params_any(pcm, params) == 0)
                snd_pcm_hw_params_get_rate_max(params, &maxRate, nullptr);
            snd_pcm_close(pcm);
            if (static_cast<int>(maxRate) > highest)
                highest = static_cast<int>(maxRate);
        }
    }
#endif
    return highest > 0 ? highest : pickMiniaudioDeviceSampleRate(info);
}

bool shouldListMiniaudioExclusiveDevice(const ma_device_info& info)
{
#if defined(__linux__)
    return isLinuxAlsaPlaybackDevice(info.id.alsa);
#else
    (void)info;
    return true;
#endif
}

int pickMiniaudioDeviceChannels(const ma_device_info& info)
{
    for (ma_uint32 i = 0; i < info.nativeDataFormatCount; ++i)
        if (info.nativeDataFormats[i].channels > 0)
            return static_cast<int>(info.nativeDataFormats[i].channels);
    return 0;
}

std::string getMiniaudioStableId(int index, bool exclusive = false)
{
    return std::string(exclusive ? "exclusive:" : "shared:") + std::to_string(index);
}

std::string getMiniaudioStableId(const ma_device_info& info, int index, bool exclusive = false)
{
#if defined(__linux__)
    if (info.id.alsa[0] != '\0')
        return std::string(exclusive ? "exclusive:" : "shared:") + info.id.alsa;
#endif
    (void)info;
    return getMiniaudioStableId(index, exclusive);
}

std::string normalizeMiniaudioSelection(std::string_view value)
{
    std::string normalized;
    for (const unsigned char ch : std::string(value))
        if (! std::isspace(ch))
            normalized.push_back(static_cast<char>(std::tolower(ch)));
    return normalized;
}

bool isMiniaudioSelectionMatch(const ma_device_info& info, int index, std::string_view requested, bool exclusive = false)
{
    if (requested.empty())
        return false;
    const std::string requestedText(requested);
    if (requestedText == getMiniaudioStableId(info, index, exclusive)
        || requestedText == getMiniaudioStableId(index, exclusive)
        || requestedText == getMiniaudioStableId(index, ! exclusive))
        return true;
#if defined(__linux__)
    if (info.id.alsa[0] != '\0')
    {
        const std::string alsaId(info.id.alsa);
        if (requestedText == alsaId
            || requestedText == "shared:" + alsaId
            || requestedText == "exclusive:" + alsaId)
            return true;
    }
#endif
    const std::string candidateName(info.name);
    if (candidateName == requestedText || containsIgnoreCase(candidateName, requestedText))
        return true;
    const auto normalizedRequested = normalizeMiniaudioSelection(requested);
    const auto normalizedName = normalizeMiniaudioSelection(info.name);
    return ! normalizedRequested.empty()
        && ! normalizedName.empty()
        && (normalizedName == normalizedRequested
            || normalizedName.find(normalizedRequested) != std::string::npos
            || normalizedRequested.find(normalizedName) != std::string::npos);
}

std::vector<DeviceDescriptor> enumerateMiniaudioSharedDevices(
    std::string_view sharedBackend,
    std::string* error = nullptr,
    bool exclusive = false)
{
    std::vector<DeviceDescriptor> devices;
    MiniaudioContextScope scope;
    std::string contextError;
    if (! initMiniaudioOutputContext(scope, sharedBackend, contextError, exclusive))
    {
        if (error != nullptr)
            *error = contextError;
        return devices;
    }

    ma_device_info* playbackDevices = nullptr;
    ma_uint32 playbackDeviceCount = 0;
    const ma_result result = ma_context_get_devices(&scope.context, &playbackDevices, &playbackDeviceCount, nullptr, nullptr);
    if (result != MA_SUCCESS)
    {
        if (error != nullptr)
            *error = std::string("miniaudio shared device enumeration failed: ") + ma_result_description(result);
        return devices;
    }

    devices.reserve(playbackDeviceCount);
    for (ma_uint32 i = 0; i < playbackDeviceCount; ++i)
    {
        if (exclusive && ! shouldListMiniaudioExclusiveDevice(playbackDevices[i]))
            continue;
        const int index = static_cast<int>(devices.size());
        const int sharedSampleRate = pickMiniaudioDeviceSampleRate(playbackDevices[i]);
        const int sampleRate = exclusive
            ? pickMiniaudioExclusiveSampleRate(playbackDevices[i])
            : sharedSampleRate;
        DeviceDescriptor descriptor;
        descriptor.index = index;
        descriptor.typeName = exclusive
            ? "ALSA exclusive"
            : sharedBackend == "alsa" ? "ALSA" : "miniaudio-shared";
        descriptor.name = playbackDevices[i].name;
        descriptor.sampleRate = sampleRate;
        descriptor.sharedSampleRate = sharedSampleRate;
        descriptor.isDefault = playbackDevices[i].isDefault != 0;
        descriptor.isAsio = false;
        descriptor.asioOutputChannels = pickMiniaudioDeviceChannels(playbackDevices[i]);
        descriptor.stableId = getMiniaudioStableId(playbackDevices[i], index, exclusive);
        if (containsIgnoreCase(descriptor.name, "usb"))
            descriptor.formFactor = "digital";
#if defined(__linux__)
        if (exclusive)
            descriptor.nativeDsdSampleRates = probeLinuxAlsaNativeDsdBitRates(playbackDevices[i].id.alsa);
#endif
        devices.push_back(descriptor);
    }

    applyCoreAudioDeviceMetadata(devices, ! exclusive);
    return devices;
}

#if defined(__linux__)
std::vector<DeviceDescriptor> enumeratePipeWireSharedDevices(std::string* error = nullptr)
{
    std::vector<DeviceDescriptor> devices;
    std::string pipewireError;
    const auto sinks = enumerateLinuxPipeWireSinks(&pipewireError);
    if (! pipewireError.empty() && error != nullptr)
        *error = pipewireError;
    devices.reserve(sinks.size());
    for (size_t i = 0; i < sinks.size(); ++i)
    {
        DeviceDescriptor descriptor;
        descriptor.index = static_cast<int>(i);
        descriptor.typeName = "PipeWire";
        descriptor.name = sinks[i].description.empty() ? sinks[i].name : sinks[i].description;
        descriptor.sampleRate = sinks[i].sampleRate > 0 ? sinks[i].sampleRate : 48000;
        descriptor.sharedSampleRate = descriptor.sampleRate;
        descriptor.isDefault = sinks[i].isDefault;
        descriptor.stableId = sinks[i].name.empty()
            ? "shared:pw:" + std::to_string(sinks[i].nodeId)
            : "shared:pw:" + sinks[i].name;
        if (containsIgnoreCase(descriptor.name, "usb") || containsIgnoreCase(sinks[i].name, "usb"))
            descriptor.formFactor = "digital";
        devices.push_back(std::move(descriptor));
    }
    return devices;
}
#endif

#ifdef _WIN32
std::vector<DeviceDescriptor> enumerateWasapiSharedDevices(std::string* error = nullptr)
{
    wasapi_shared_device_info* rawDevices = nullptr;
    uint32_t count = 0;
    std::vector<DeviceDescriptor> devices;
    if (wasapi_shared_list_devices(&rawDevices, &count) != 0)
    {
        if (error != nullptr)
            *error = "WASAPI shared device enumeration failed";
        return devices;
    }

    devices.reserve(count);
    for (uint32_t i = 0; i < count; ++i)
    {
        DeviceDescriptor descriptor;
        descriptor.index = static_cast<int>(i);
        descriptor.typeName = "Windows Audio";
        descriptor.name = rawDevices[i].name;
        descriptor.sampleRate = static_cast<int>(rawDevices[i].sharedSampleRate);
        descriptor.sharedSampleRate = static_cast<int>(rawDevices[i].sharedSampleRate);
        descriptor.isDefault = rawDevices[i].isDefault != 0;
        descriptor.isAsio = false;
        descriptor.asioOutputChannels = static_cast<int>(rawDevices[i].channels);
        const auto deviceId = wideToUtf8(rawDevices[i].id);
        descriptor.stableId = deviceId.empty() ? "shared:" + std::to_string(i) : "shared:" + deviceId;
        devices.push_back(std::move(descriptor));
    }
    wasapi_shared_free_devices(rawDevices);
    applyCoreAudioDeviceMetadata(devices, true);
    return devices;
}
#endif

enum class NativePcmOutputBackendKind
{
    Miniaudio,
    PipeWire,
    WasapiShared,
    WasapiExclusive,
    Asio,
    KernelStreaming,
};

enum class SpecializedHostRunner
{
    None,
    WasapiExclusivePcm,
    WasapiExclusiveDop,
    AsioPcm,
    AsioDop,
    AsioNativeDsd,
};

bool shouldTryMiniaudioSharedOutput(const Options& options)
{
    if (options.asio || options.kernelStreaming || options.exclusive || options.dopOutput || options.asioNativeDsdOutput)
        return false;
#ifdef _WIN32
    return options.sharedBackend == "miniaudio" || options.sharedBackend == "directsound";
#elif defined(__linux__)
    if (options.sharedBackend == "alsa" || options.sharedBackend == "miniaudio")
        return true;
    if (options.sharedBackend == "pipewire")
        return false;
    return ! linuxPipeWireRuntimeAvailable();
#else
    return options.sharedBackend != "windows" && options.sharedBackend != "directsound";
#endif
}

SpecializedHostRunner selectSpecializedHostRunner(const Options& options)
{
    if (options.asioNativeDsdOutput)
        return SpecializedHostRunner::AsioNativeDsd;
    if (options.asio && options.dopOutput)
        return SpecializedHostRunner::AsioDop;
    if (options.asio)
        return SpecializedHostRunner::AsioPcm;
    if (options.exclusive && options.dopOutput)
        return SpecializedHostRunner::WasapiExclusiveDop;
    if (options.exclusive)
        return SpecializedHostRunner::WasapiExclusivePcm;
    return SpecializedHostRunner::None;
}

NativePcmOutputBackendKind selectNativePcmOutputBackend(const Options& options)
{
    if (options.kernelStreaming)
        return NativePcmOutputBackendKind::KernelStreaming;
    if (options.asio)
        return NativePcmOutputBackendKind::Asio;
    if (options.exclusive)
#if defined(__APPLE__) || defined(__linux__)
        return NativePcmOutputBackendKind::Miniaudio;
#else
        return NativePcmOutputBackendKind::WasapiExclusive;
#endif
    if (shouldTryMiniaudioSharedOutput(options))
        return NativePcmOutputBackendKind::Miniaudio;
#ifdef _WIN32
    return NativePcmOutputBackendKind::WasapiShared;
#elif defined(__linux__)
    return NativePcmOutputBackendKind::PipeWire;
#else
    return NativePcmOutputBackendKind::Miniaudio;
#endif
}

std::vector<DeviceDescriptor> enumerateDevices(
    DeviceListMode mode,
    std::string_view sharedBackend = "auto")
{
    if (mode == DeviceListMode::Shared)
    {
#ifdef _WIN32
        if (sharedBackend != "miniaudio" && sharedBackend != "directsound" && sharedBackend != "alsa")
        {
            std::string wasapiError;
            auto devices = enumerateWasapiSharedDevices(&wasapiError);
            if (devices.empty() && ! wasapiError.empty())
                logLine(wasapiError);
            return devices;
        }
#endif
#if defined(__linux__)
        if (sharedBackend != "alsa" && sharedBackend != "miniaudio")
        {
            std::string pipewireError;
            auto devices = enumeratePipeWireSharedDevices(&pipewireError);
            if (! devices.empty())
                return devices;
            if (sharedBackend == "pipewire")
            {
                if (! pipewireError.empty())
                    logLine(pipewireError);
                return devices;
            }
            if (! pipewireError.empty())
                logLine(pipewireError);
        }
#endif
        std::string miniaudioError;
        auto devices = enumerateMiniaudioSharedDevices(sharedBackend, &miniaudioError);
        if (devices.empty() && ! miniaudioError.empty())
            logLine(miniaudioError);
        return devices;
    }

#ifdef _WIN32
    if (mode == DeviceListMode::KernelStreaming)
    {
        ks_device_info* rawDevices = nullptr;
        uint32_t count = 0;
        std::vector<DeviceDescriptor> devices;
        if (ks_output_list_devices(&rawDevices, &count) != 0)
            return devices;
        devices.reserve(count);
        for (uint32_t i = 0; i < count; ++i)
        {
            DeviceDescriptor descriptor;
            descriptor.index = static_cast<int>(i);
            descriptor.typeName = "WDM-KS";
            descriptor.name = rawDevices[i].name;
            descriptor.isDefault = i == 0;
            const auto deviceId = wideToUtf8(rawDevices[i].id);
            descriptor.stableId = deviceId.empty() ? "ks:" + std::to_string(i) : "ks:" + deviceId;
            devices.push_back(std::move(descriptor));
        }
        ks_output_free_devices(rawDevices);
        return devices;
    }
    if (mode == DeviceListMode::Asio)
    {
        asio_device_info* rawDevices = nullptr;
        uint32_t count = 0;
        std::vector<DeviceDescriptor> devices;
        if (asio_list_devices(&rawDevices, &count) != 0)
            return devices;
        devices.reserve(count);
        for (uint32_t i = 0; i < count; ++i)
        {
            DeviceDescriptor descriptor;
            descriptor.index = static_cast<int>(i);
            descriptor.typeName = "ASIO";
            descriptor.name = rawDevices[i].name;
            descriptor.isDefault = rawDevices[i].isDefault != 0;
            descriptor.isAsio = true;
            descriptor.asioOutputChannels = static_cast<int>(rawDevices[i].outputChannels);
            descriptor.asioOutputChannelNames = rawDevices[i].outputChannelNames;
            descriptor.stableId = "asio:" + descriptor.name;
            devices.push_back(std::move(descriptor));
        }
        asio_free_devices(rawDevices);
        return devices;
    }
    if (mode == DeviceListMode::Exclusive)
    {
        wasapi_exclusive_device_info* rawDevices = nullptr;
        uint32_t count = 0;
        std::vector<DeviceDescriptor> devices;
        if (wasapi_exclusive_list_devices(&rawDevices, &count) != 0)
            return devices;
        devices.reserve(count);
        for (uint32_t i = 0; i < count; ++i)
        {
            DeviceDescriptor descriptor;
            descriptor.index = static_cast<int>(i);
            descriptor.typeName = "WASAPI exclusive";
            descriptor.name = rawDevices[i].name;
            descriptor.sampleRate = static_cast<int>(rawDevices[i].highestSampleRate);
            descriptor.sharedSampleRate = static_cast<int>(rawDevices[i].sharedSampleRate);
            descriptor.isDefault = rawDevices[i].isDefault != 0;
            const auto deviceId = wideToUtf8(rawDevices[i].id);
            descriptor.stableId = deviceId.empty() ? "exclusive:" + std::to_string(i) : "exclusive:" + deviceId;
            devices.push_back(std::move(descriptor));
        }
        wasapi_exclusive_free_devices(rawDevices);
        applyCoreAudioDeviceMetadata(devices, false);
        return devices;
    }
#endif
#if defined(__APPLE__)
    if (mode == DeviceListMode::Exclusive)
        return enumerateMiniaudioSharedDevices(sharedBackend);
#endif
#if defined(__linux__)
    if (mode == DeviceListMode::Exclusive)
        return enumerateMiniaudioSharedDevices("alsa", nullptr, true);
#endif
    return {};
}

int listDevices(const Options& options)
{
    const auto mode = options.kernelStreaming
        ? DeviceListMode::KernelStreaming
        : options.asio ? DeviceListMode::Asio
        : options.exclusive ? DeviceListMode::Exclusive : DeviceListMode::Shared;
    const auto devices = enumerateDevices(mode, options.sharedBackend);

    for (const auto& device : devices)
    {
        std::cout
            << device.index << "\t"
            << device.name << "\t"
            << device.sampleRate << "\t"
            << (device.isDefault ? 1 : 0) << "\t"
            << device.sharedSampleRate << "\t"
            << device.connectionType << "\t"
            << device.formFactor << "\t"
            << device.stableId;
        if (! device.nativeDsdSampleRates.empty())
        {
            std::cout << "\t";
            for (size_t i = 0; i < device.nativeDsdSampleRates.size(); ++i)
            {
                if (i > 0)
                    std::cout << ",";
                std::cout << device.nativeDsdSampleRates[i];
            }
        }
        std::cout << std::endl;
    }

    return 0;
}

#ifdef _WIN32
int probeAsioSdmFamilies(const Options& options)
{
    // Probe the actual DoP/native DSD transport rates used by every target.
    // Report per-rate evidence; support for one target must never be inferred
    // from another target in the same family.
    constexpr uint32_t rates[] {
        176'400, 352'800, 705'600, 1'411'200,
        192'000, 384'000, 768'000, 1'536'000,
        2'822'400, 5'644'800, 11'289'600, 22'579'200,
        3'072'000, 6'144'000, 12'288'000, 24'576'000,
    };
    uint8_t supported[std::size(rates)] {};
    char error[512] {};
    constexpr uint32_t ratesPerFormat = 8;
    if (asio_probe_sample_rates(
            options.deviceName.c_str(), options.deviceIndex,
            rates, ratesPerFormat, 0, supported,
            error, sizeof(error)) != 0)
        throw std::runtime_error(error[0] != '\0' ? error : "ASIO SDM family probe failed");

    char nativeDsdError[512] {};
    if (asio_probe_sample_rates(
            options.deviceName.c_str(), options.deviceIndex,
            rates + ratesPerFormat, ratesPerFormat, 1, supported + ratesPerFormat,
            nativeDsdError, sizeof(nativeDsdError)) != 0)
    {
        // Native DSD is optional. Returning an explicitly empty array keeps
        // planning fail-closed while preserving independently proven DoP rates.
        std::fill(supported + ratesPerFormat, supported + std::size(rates), 0);
    }

    auto writeSupportedRates = [&] (std::size_t offset, std::size_t count)
    {
        std::string json = "[";
        bool first = true;
        for (std::size_t index = 0; index < count; ++index)
        {
            if (supported[offset + index] == 0)
                continue;
            if (!first)
                json += ",";
            json += std::to_string(rates[offset + index]);
            first = false;
        }
        return json + "]";
    };
    std::cout << "{\"schemaVersion\":1,\"device\":\"" << jsonEscape(options.deviceName)
              << "\",\"dopTransportSampleRates\":" << writeSupportedRates(0, 8)
              << ",\"nativeDsdSampleRates\":" << writeSupportedRates(8, 8)
              << "}\n";
    return 0;
}
#endif

constexpr size_t maxAutomixPcmPayloadBytes = 64 * 1024;
constexpr size_t maxPendingAutomixPcmBytes = 1024 * 1024;

void pushAutomixNextPcmPayload(PcmRingAudioSource& source, int channels, std::vector<char>& pending, const std::vector<char>& payload)
{
    if (payload.size() > maxAutomixPcmPayloadBytes || pending.size() + payload.size() > maxPendingAutomixPcmBytes)
    {
        pending.clear();
        return;
    }
    const size_t frameBytes = static_cast<size_t>(channels) * sizeof(float);
    if (frameBytes == 0)
        return;
    pending.insert(pending.end(), payload.begin(), payload.end());
    const size_t frameCount = pending.size() / frameBytes;
    if (frameCount == 0)
        return;
    const size_t sampleCount = frameCount * static_cast<size_t>(channels);
    std::vector<float> samples(sampleCount);
    std::memcpy(samples.data(), pending.data(), sampleCount * sizeof(float));
    if (! source.pushAutomixNext(samples.data(), static_cast<int>(frameCount)))
        return;
    pending.erase(pending.begin(), pending.begin() + static_cast<std::ptrdiff_t>(sampleCount * sizeof(float)));
}

double getJsonDouble(const echo_audio_host::Json& object, const char* key, double fallback)
{
    return echo_audio_host::jsonDoubleValue(object, key, fallback);
}

uint64_t getJsonRevision(const echo_audio_host::Json& object, const char* key)
{
    if (! object.is_object() || ! object.contains(key) || ! object[key].is_number())
        return 0;
    constexpr double maxSafeInteger = 9007199254740991.0;
    const double parsed = object[key].get<double>();
    const double integral = std::floor(parsed);
    return std::isfinite(parsed)
            && parsed > 0.0
            && parsed <= maxSafeInteger
            && parsed == integral
        ? static_cast<uint64_t>(integral)
        : 0;
}

void prepareAutomixFromPayload(PcmRingAudioSource& source, double sampleRate, const std::vector<char>& payload)
{
    if (payload.empty())
        return;
    const auto object = echo_audio_host::parseJson(std::string_view(payload.data(), payload.size()));
    if (! object.is_object())
        return;
    source.prepareAutomix(
        sampleRate,
        getJsonDouble(object, "fadeStartSeconds", 0.0),
        getJsonDouble(object, "overlapSeconds", 0.001),
        getJsonDouble(object, "currentGainDb", 0.0),
        getJsonDouble(object, "nextGainDb", 0.0));
}

int base64Value(char c)
{
    if (c >= 'A' && c <= 'Z') return c - 'A';
    if (c >= 'a' && c <= 'z') return c - 'a' + 26;
    if (c >= '0' && c <= '9') return c - '0' + 52;
    if (c == '+') return 62;
    if (c == '/') return 63;
    return -1;
}

bool decodeBase64Payload(std::string_view text, std::vector<char>& output)
{
    output.clear();
    const int length = static_cast<int>(text.size());
    if (length == 0)
        return true;
    if ((length % 4) != 0)
        return false;
    int padding = 0;
    bool sawPadding = false;
    for (int i = 0; i < length; ++i)
    {
        const char c = text[static_cast<size_t>(i)];
        if (c == '=')
        {
            sawPadding = true;
            ++padding;
            if (padding > 2)
                return false;
            continue;
        }
        if (sawPadding || base64Value(c) < 0)
            return false;
    }
    if (padding > 0 && text[static_cast<size_t>(length - 1)] != '=')
        return false;
    if (padding == 2 && text[static_cast<size_t>(length - 2)] != '=')
        return false;
    int accumulator = 0;
    int bits = -8;
    for (int i = 0; i < length; ++i)
    {
        const char c = text[static_cast<size_t>(i)];
        if (c == '=')
            break;
        accumulator = (accumulator << 6) | base64Value(c);
        bits += 6;
        if (bits >= 0)
        {
            output.push_back(static_cast<char>((accumulator >> bits) & 0xff));
            bits -= 8;
        }
    }
    return output.size() <= maxAutomixPcmPayloadBytes;
}

uint64_t getJsonSessionId(const echo_audio_host::Json& object)
{
    if (! object.is_object() || ! object.contains("sessionId") || ! object["sessionId"].is_number())
        return 0;
    const double parsed = object["sessionId"].get<double>();
    const double integral = std::floor(parsed);
    return std::isfinite(parsed) && parsed > 0.0 && std::abs(parsed - integral) < std::numeric_limits<double>::epsilon()
        ? static_cast<uint64_t>(integral)
        : 0;
}

void writeJsonRpcFd(RpcOutputWriter& writer, const char* data, size_t bytes);

int64_t getJsonRpcIntegerId(const echo_audio_host::Json& object)
{
    if (! object.is_object() || ! object.contains("id") || ! object["id"].is_number())
        return -1;
    const double parsed = object["id"].get<double>();
    const double integral = std::floor(parsed);
    return std::isfinite(parsed) && parsed >= 0.0 && std::abs(parsed - integral) < std::numeric_limits<double>::epsilon()
        ? static_cast<int64_t>(integral)
        : -1;
}

const echo_audio_host::Json* getJsonObjectParams(const echo_audio_host::Json& params)
{
    if (params.is_object())
        return &params;
    if (params.is_array() && ! params.empty() && params[0].is_object())
        return &params[0];
    return nullptr;
}

bool requestsProNativeProcessing(const echo_audio_host::Json& params)
{
    if (! params.is_object() || ! params.contains("processing") || ! params["processing"].is_object())
        return false;
    const auto& processing = params["processing"];
    if (processing.contains("echoSrc") && processing["echoSrc"].is_object())
        return true;
    if (processing.contains("sdm") && processing["sdm"].is_object())
        return true;
    if (processing.contains("dither") && processing["dither"].is_object())
    {
        const auto mode = echo_audio_host::jsonStringValue(processing["dither"], "mode", "off");
        return !mode.empty() && mode != "off";
    }
    return false;
}

void writeJsonRpcBooleanResult(RpcOutputWriter& writer, int64_t id, bool result)
{
    if (id < 0)
        return;
    const std::string response = std::string("{\"jsonrpc\":\"2.0\",\"result\":")
        + (result ? "true" : "false")
        + ",\"id\":" + std::to_string(id) + "}\n";
    writeJsonRpcFd(writer, response.data(), response.size());
}

void writeJsonRpcResult(RpcOutputWriter& writer, int64_t id, const echo_audio_host::Json& result)
{
    if (id < 0)
        return;
    echo_audio_host::Json response = {
        {"jsonrpc", "2.0"},
        {"result", result},
        {"id", id},
    };
    const std::string serialized = response.dump() + "\n";
    writeJsonRpcFd(writer, serialized.data(), serialized.size());
}

void writeJsonRpcError(RpcOutputWriter& writer, int64_t id, int code, const std::string& message)
{
    if (id < 0)
        return;
    echo_audio_host::Json response = {
        {"jsonrpc", "2.0"},
        {"error", {
            {"code", code},
            {"message", message},
        }},
        {"id", id},
    };
    const std::string serialized = response.dump() + "\n";
    writeJsonRpcFd(writer, serialized.data(), serialized.size());
}

std::string getJsonString(const echo_audio_host::Json& object, const char* key, std::string_view fallback = {})
{
    return echo_audio_host::jsonStringValue(object, key, fallback);
}

class EqControlServer final
{
public:
    EqControlServer(
        int portToUse,
        echo::EqProcessor& processorToUse,
        echo::ChannelBalanceProcessor& channelBalanceProcessorToUse)
        : port(portToUse), processor(processorToUse), channelBalanceProcessor(channelBalanceProcessorToUse)
    {
    }

    EqControlServer(
        int portToUse,
        echo::EqProcessor& processorToUse,
        echo::ChannelBalanceProcessor& channelBalanceProcessorToUse,
        echo::ConvolutionProcessor& convolutionProcessorToUse,
        echo::DspHeadroomProcessor& headroomProcessorToUse,
        echo::ReplayGainProcessor& replayGainProcessorToUse,
        echo::PlaybackRateProcessor& rateProcessorToUse,
        echo::LevelMeterProcessor& meterProcessorToUse)
        : port(portToUse), processor(processorToUse), channelBalanceProcessor(channelBalanceProcessorToUse),
          convolutionProcessor(&convolutionProcessorToUse), headroomProcessor(&headroomProcessorToUse),
          replayGainProcessor(&replayGainProcessorToUse), rateProcessor(&rateProcessorToUse), meterProcessor(&meterProcessorToUse)
    {
    }

    ~EqControlServer() { stop(); }

    bool start()
    {
        if (port <= 0)
            return false;
        logLine("EQ control listener unavailable in native audio-host build");
        return false;
    }

    void stop()
    {
        running.store(false, std::memory_order_release);
        if (worker.joinable())
            worker.join();
    }

private:
    const int port = 0;
    echo::EqProcessor& processor;
    echo::ChannelBalanceProcessor& channelBalanceProcessor;
    echo::ConvolutionProcessor* convolutionProcessor = nullptr;
    echo::DspHeadroomProcessor* headroomProcessor = nullptr;
    echo::ReplayGainProcessor* replayGainProcessor = nullptr;
    echo::PlaybackRateProcessor* rateProcessor = nullptr;
    echo::LevelMeterProcessor* meterProcessor = nullptr;
    std::thread worker;
    std::atomic<bool> running { false };
};

template <typename StopSource, typename StopBackend, typename StopControl>
void cleanupHostAndAck(StopSource&& stopSource, StopBackend&& stopBackend, StopControl&& stopControl, bool& shutdownAckSent)
{
    std::forward<StopSource>(stopSource)();
    std::forward<StopBackend>(stopBackend)();
    std::forward<StopControl>(stopControl)();
    if (! shutdownAckSent)
    {
        writeJsonLine("{\"event\":\"shutdown-ack\"}");
        shutdownAckSent = true;
    }
}

template <typename StopSource, typename StopBackend>
void cleanupHostAndAck(StopSource&& stopSource, StopBackend&& stopBackend, bool& shutdownAckSent)
{
    cleanupHostAndAck(std::forward<StopSource>(stopSource), std::forward<StopBackend>(stopBackend), [] {}, shutdownAckSent);
}

void cleanupPcmSource(PcmRingAudioSource& source)
{
    try { source.requestStop(); }
    catch (const std::exception& error) { logLine(std::string("source.requestStop cleanup failed: ") + error.what()); }
    catch (...) { logLine("source.requestStop cleanup failed"); }
}

void cleanupEqControlServer(EqControlServer& eqControlServer)
{
    try { eqControlServer.stop(); }
    catch (const std::exception& error) { logLine(std::string("eqControlServer.stop cleanup failed: ") + error.what()); }
    catch (...) { logLine("eqControlServer.stop cleanup failed"); }
}

struct MiniaudioSharedOutput
{
    ma_context context{};
    PcmRingAudioSource* source = nullptr;
    ma_device device{};
    std::string deviceName;
    int deviceIndex = -1;
    int deviceNativeChannels = 0;
    ma_device_id selectedDeviceId{};
#if defined(__APPLE__)
    MacCoreAudioDeviceWatcher deviceWatcher;
    MacCoreAudioHogMode hogMode;
    bool followsDefaultDevice = false;
    std::atomic<bool> stopping { false };
#endif
    bool contextInitialized = false;
    bool initialized = false;
    bool started = false;
};

#if defined(__APPLE__)
void writeCoreAudioDeviceNotification(
    void*,
    const char* event,
    const std::string& deviceUid,
    const char* reason,
    std::uint32_t code,
    bool currentDevice,
    bool followsDefaultDevice)
{
    writeJsonLine(
        "{\"event\":\"" + jsonEscape(event != nullptr ? event : "device_state_changed")
        + "\",\"deviceId\":\"" + jsonEscape(deviceUid)
        + "\",\"reason\":\"" + jsonEscape(reason != nullptr ? reason : "unknown")
        + "\",\"code\":" + std::to_string(code)
        + ",\"currentDevice\":" + std::string(currentDevice ? "true" : "false")
        + ",\"followsDefaultDevice\":" + std::string(followsDefaultDevice ? "true" : "false")
        + "}");
}

void miniaudioSharedNotificationCallback(const ma_device_notification* notification)
{
    if (notification == nullptr || notification->pDevice == nullptr)
        return;
    auto* state = static_cast<MiniaudioSharedOutput*>(notification->pDevice->pUserData);
    if (state == nullptr || state->stopping.load(std::memory_order_acquire))
        return;

    switch (notification->type)
    {
        case ma_device_notification_type_stopped:
            writeCoreAudioDeviceNotification(
                nullptr,
                "audio_session_disconnected",
                {},
                "device_stopped",
                0,
                true,
                state->followsDefaultDevice);
            break;
        case ma_device_notification_type_rerouted:
            writeCoreAudioDeviceNotification(
                nullptr,
                "default_device_changed",
                {},
                "rerouted",
                0,
                true,
                state->followsDefaultDevice);
            break;
        case ma_device_notification_type_interruption_began:
            writeCoreAudioDeviceNotification(
                nullptr,
                "audio_session_disconnected",
                {},
                "interruption_began",
                0,
                true,
                state->followsDefaultDevice);
            break;
        default:
            break;
    }
}
#endif

void miniaudioSharedDataCallback(ma_device* pDevice, void* pOutput, const void* pInput, ma_uint32 frameCount)
{
    (void)pInput;
    auto* output = static_cast<float*>(pOutput);
    const auto outputChannels = pDevice != nullptr ? pDevice->playback.channels : 0;
    if (output == nullptr || outputChannels == 0)
        return;
    auto* state = pDevice != nullptr ? static_cast<MiniaudioSharedOutput*>(pDevice->pUserData) : nullptr;
    if (state == nullptr || state->source == nullptr)
    {
        std::memset(output, 0, static_cast<size_t>(frameCount) * outputChannels * sizeof(float));
        return;
    }
    configureAudioCallbackThread();
    state->source->renderInterleaved(output, frameCount, outputChannels);
}

bool initMiniaudioSharedOutput(MiniaudioSharedOutput& output, PcmRingAudioSource& source, const Options& options,
    int sampleRate, int channels, int bufferFrames, std::string& error)
{
    output.source = &source;
#if defined(__APPLE__)
    output.stopping.store(false, std::memory_order_release);
    output.followsDefaultDevice = options.deviceIndex < 0 && options.deviceName.empty();
#endif
    if (! initMiniaudioOutputContext(output.context, options.sharedBackend, error, options.exclusive))
    {
        output.source = nullptr;
        return false;
    }
    output.contextInitialized = true;

    ma_device_info* playbackDevices = nullptr;
    ma_uint32 playbackDeviceCount = 0;
    ma_result result = ma_context_get_devices(&output.context, &playbackDevices, &playbackDeviceCount, nullptr, nullptr);
    if (result != MA_SUCCESS)
    {
        error = std::string("miniaudio shared device enumeration failed: ") + ma_result_description(result);
        ma_context_uninit(&output.context);
        output.contextInitialized = false;
        output.source = nullptr;
        return false;
    }

    std::vector<const ma_device_info*> candidates;
    candidates.reserve(playbackDeviceCount);
    for (ma_uint32 i = 0; i < playbackDeviceCount; ++i)
        if (! options.exclusive || shouldListMiniaudioExclusiveDevice(playbackDevices[i]))
            candidates.push_back(&playbackDevices[i]);

    const ma_device_info* selectedDevice = nullptr;
    int selectedIndex = -1;
    bool hardwareIdRequested = false;
    const auto selectMatching = [&](const std::string& requested, bool exact)
    {
        for (size_t i = 0; i < candidates.size(); ++i)
        {
            const auto& info = *candidates[i];
            if (requested.empty() || (exact
                    ? requested != getMiniaudioStableId(info, static_cast<int>(i), options.exclusive)
                        && requested != info.name
                    : ! isMiniaudioSelectionMatch(info, static_cast<int>(i), requested, options.exclusive)))
                continue;
            selectedDevice = &info;
            selectedIndex = static_cast<int>(i);
            return true;
        }
        return false;
    };
#if defined(__linux__)
    // A stable hardware ID outranks a stale display index or duplicate name.
    hardwareIdRequested = options.exclusive && options.deviceId.rfind("exclusive:", 0) == 0
        && isLinuxAlsaHardwareDevice(std::string_view(options.deviceId).substr(10));
    if (hardwareIdRequested)
        selectMatching(options.deviceId, true);
#endif
    if (selectedDevice == nullptr && ! hardwareIdRequested && ! options.deviceName.empty())
    {
        if (! selectMatching(options.deviceName, true))
            selectMatching(options.deviceName, false);
    }
    if (selectedDevice == nullptr && ! hardwareIdRequested && options.deviceName.empty()
        && options.deviceIndex >= 0 && static_cast<size_t>(options.deviceIndex) < candidates.size())
    {
        selectedIndex = options.deviceIndex;
        selectedDevice = candidates[static_cast<size_t>(selectedIndex)];
    }
#if defined(__linux__)
    if (options.exclusive && ! hardwareIdRequested && options.deviceName.empty()
        && options.deviceIndex < 0 && ! candidates.empty())
    {
        selectedDevice = candidates.front();
        selectedIndex = 0;
    }
#endif
    if (selectedDevice == nullptr && (hardwareIdRequested || options.deviceIndex >= 0 || ! options.deviceName.empty()
#if defined(__linux__)
        || options.exclusive
#endif
        ))
    {
        error = "miniaudio output device not found";
        ma_context_uninit(&output.context);
        output.contextInitialized = false;
        output.source = nullptr;
        return false;
    }
    if (selectedDevice != nullptr)
    {
        output.selectedDeviceId = selectedDevice->id;
        output.deviceIndex = selectedIndex;
        output.deviceName = selectedDevice->name;
        output.deviceNativeChannels = pickMiniaudioDeviceChannels(*selectedDevice);
    }

#if defined(__APPLE__)
    if (options.exclusive
        && ! output.hogMode.acquire(selectedDevice != nullptr ? selectedDevice->id.coreaudio : nullptr, error))
    {
        ma_context_uninit(&output.context);
        output.contextInitialized = false;
        output.source = nullptr;
        return false;
    }
#endif

    auto config = ma_device_config_init(ma_device_type_playback);
    const ma_device_id* selectedDeviceId = selectedDevice != nullptr ? &output.selectedDeviceId : nullptr;
    config.playback.format = ma_format_f32;
    config.playback.channels = static_cast<ma_uint32>(std::max(1, channels));
    config.playback.pDeviceID = selectedDeviceId;
#if defined(__APPLE__)
    // CoreAudio's AudioUnit remains in shared mode while Hog Mode owns the
    // physical device exclusively for this process.
    config.playback.shareMode = ma_share_mode_shared;
#else
    config.playback.shareMode = options.exclusive ? ma_share_mode_exclusive : ma_share_mode_shared;
#endif
    config.sampleRate = static_cast<ma_uint32>(std::max(1, sampleRate));
#if defined(__APPLE__)
    // Desktop CoreAudio otherwise pins the HAL device to its current nominal
    // rate and miniaudio inserts SRC. Match the hardware to the source rate;
    // miniaudio safely falls back to the closest supported native rate.
    config.coreaudio.allowNominalSampleRateChange = options.exclusive ? MA_TRUE : MA_FALSE;
#endif
    config.periodSizeInFrames = static_cast<ma_uint32>(std::max(1, bufferFrames));
    config.performanceProfile = ma_performance_profile_low_latency;
    config.dataCallback = miniaudioSharedDataCallback;
#if defined(__APPLE__)
    config.notificationCallback = miniaudioSharedNotificationCallback;
#endif
#if defined(__linux__)
    if (options.exclusive)
    {
        config.alsa.noAutoResample = MA_TRUE;
        config.alsa.noAutoChannels = MA_TRUE;
    }
#endif
    config.pUserData = &output;

    const auto openDevice = [&] { return ma_device_init(&output.context, &config, &output.device); };
#if defined(__linux__)
    result = options.exclusive
        ? openLinuxAlsaAfterIdleRelease(openDevice, [](int ms) {
            std::this_thread::sleep_for(std::chrono::milliseconds(ms));
        }, MA_BUSY)
        : openDevice();
#else
    result = openDevice();
#endif
    if (result != MA_SUCCESS)
    {
        error = std::string("ma_device_init failed: ") + ma_result_description(result);
#if defined(__linux__)
        if (options.exclusive && result == MA_BUSY)
            error = "alsa_exclusive_device_busy: Device or resource busy; release this output in other audio applications and retry";
#endif
#if defined(__APPLE__)
        output.hogMode.release();
#endif
        ma_context_uninit(&output.context);
        output.contextInitialized = false;
        output.source = nullptr;
        return false;
    }
    output.initialized = true;
    const auto nativeRate = output.device.sampleRate > 0 ? output.device.sampleRate : config.sampleRate;
    const auto nativeBufferFrames = output.device.playback.internalPeriodSizeInFrames > 0
        ? static_cast<int>(output.device.playback.internalPeriodSizeInFrames)
        : std::max(1, bufferFrames);
    source.prepareForNativeRender(nativeBufferFrames, static_cast<double>(nativeRate));

    result = ma_device_start(&output.device);
    if (result != MA_SUCCESS)
    {
        error = std::string("ma_device_start failed: ") + ma_result_description(result);
        ma_device_uninit(&output.device);
        output.initialized = false;
#if defined(__APPLE__)
        output.hogMode.release();
#endif
        ma_context_uninit(&output.context);
        output.contextInitialized = false;
        output.source = nullptr;
        return false;
    }
    output.started = true;
#if defined(__APPLE__)
    std::string watcherError;
    if (! output.deviceWatcher.start(
            selectedDevice != nullptr ? selectedDevice->id.coreaudio : nullptr,
            output.followsDefaultDevice,
            writeCoreAudioDeviceNotification,
            &output,
            watcherError))
    {
        logLine("CoreAudio device watcher unavailable: " + watcherError);
    }
#endif
    return true;
}

void stopMiniaudioSharedOutput(MiniaudioSharedOutput& output)
{
#if defined(__APPLE__)
    output.stopping.store(true, std::memory_order_release);
    output.deviceWatcher.stop();
#endif
    output.source = nullptr;
    if (output.started)
    {
        ma_device_stop(&output.device);
        output.started = false;
    }
    if (output.initialized)
    {
        ma_device_uninit(&output.device);
        output.initialized = false;
    }
    if (output.contextInitialized)
    {
        ma_context_uninit(&output.context);
        output.contextInitialized = false;
    }
#if defined(__APPLE__)
    output.hogMode.release();
#endif
}

struct NativePcmOutputReadyInfo
{
    int sampleRate = 0;
    int hardwareSampleRate = 0;
    int channels = 0;
    int deviceBufferFrames = 0;
    int openedDeviceBufferFrames = 0;
    bool exclusive = false;
    bool asio = false;
    std::string format;
    std::string backend;
    std::string backendImpl;
    std::string deviceType;
    std::string deviceName;
};

struct NativePcmOutputNotification
{
    std::string event;
    std::string deviceId;
    std::string reason;
    std::uint32_t code = 0;
    bool currentDevice = false;
    bool followsDefaultDevice = false;
};

class NativePcmOutputNotificationQueue final
{
public:
    static constexpr std::size_t maximumPendingNotifications = 16;

    void push(NativePcmOutputNotification notification)
    {
        std::lock_guard<std::mutex> lock(mutex);
        if (notifications.size() >= maximumPendingNotifications)
            notifications.pop_front();
        notifications.push_back(std::move(notification));
    }

    bool pop(NativePcmOutputNotification& notification)
    {
        std::lock_guard<std::mutex> lock(mutex);
        if (notifications.empty())
            return false;
        notification = std::move(notifications.front());
        notifications.pop_front();
        return true;
    }

private:
    std::mutex mutex;
    std::deque<NativePcmOutputNotification> notifications;
};

echo_audio_host::Json buildNativePcmOutputNotificationJson(const NativePcmOutputNotification& notification)
{
    return {
        {"event", notification.event},
        {"deviceId", notification.deviceId},
        {"reason", notification.reason},
        {"code", notification.code},
        {"currentDevice", notification.currentDevice},
        {"followsDefaultDevice", notification.followsDefaultDevice},
    };
}

class NativePcmOutputBackend
{
public:
    virtual ~NativePcmOutputBackend() = default;
    virtual bool open(
        NativePlaybackPipeline& pipeline,
        const Options& options,
        int sampleRate,
        int channels,
        int bufferFrames,
        std::string& error) = 0;
    virtual void close() noexcept = 0;
    virtual const NativePcmOutputReadyInfo& readyInfo() const noexcept = 0;
    virtual bool pollNotification(NativePcmOutputNotification&) { return false; }
};

class MiniaudioPcmOutputBackend final : public NativePcmOutputBackend
{
public:
    ~MiniaudioPcmOutputBackend() override { close(); }

    bool open(
        NativePlaybackPipeline& pipeline,
        const Options& options,
        int sampleRate,
        int channels,
        int bufferFrames,
        std::string& error) override
    {
        if (! initMiniaudioSharedOutput(output, pipeline.pcmSource(), options, sampleRate, channels, bufferFrames, error))
            return false;
        const bool directSound = options.sharedBackend == "directsound";
        info.sampleRate = output.device.sampleRate > 0 ? static_cast<int>(output.device.sampleRate) : sampleRate;
#if defined(__APPLE__)
        info.hardwareSampleRate = output.device.playback.internalSampleRate > 0
            ? static_cast<int>(output.device.playback.internalSampleRate)
            : info.sampleRate;
#else
        info.hardwareSampleRate = info.sampleRate;
#endif
        info.channels = output.device.playback.channels > 0 ? static_cast<int>(output.device.playback.channels) : channels;
        info.deviceBufferFrames = output.device.playback.internalPeriodSizeInFrames > 0
            ? static_cast<int>(output.device.playback.internalPeriodSizeInFrames)
            : bufferFrames;
        info.openedDeviceBufferFrames = info.deviceBufferFrames;
        info.exclusive = options.exclusive;
        info.asio = false;
        info.format = "f32";
#if defined(__APPLE__)
        info.backend = options.exclusive ? "coreaudio-exclusive" : "coreaudio-shared";
        info.backendImpl = options.exclusive
            ? "miniaudio-coreaudio-hog-native-rate"
            : "miniaudio-coreaudio-native-rate";
        info.deviceType = options.exclusive ? "CoreAudio Hog Mode" : "CoreAudio";
#elif defined(__linux__)
        info.backend = options.exclusive
            ? "alsa-exclusive"
            : options.sharedBackend == "alsa" ? "alsa-shared" : "miniaudio-shared";
        info.backendImpl = options.exclusive
            ? "miniaudio-alsa-exclusive"
            : options.sharedBackend == "alsa" ? "miniaudio-alsa-shared" : "miniaudio-shared";
        info.deviceType = options.exclusive
            ? "ALSA exclusive"
            : options.sharedBackend == "alsa" ? "ALSA" : "miniaudio-shared";
#else
        info.backend = directSound ? "directsound-shared" : "miniaudio-shared";
        info.backendImpl = directSound ? "miniaudio-directsound-shared" : "miniaudio-shared";
        info.deviceType = directSound ? "DirectSound" : "miniaudio-shared";
#endif
        info.deviceName = output.deviceName.empty() ? "miniaudio default output" : output.deviceName;
        return true;
    }

    void close() noexcept override
    {
        stopMiniaudioSharedOutput(output);
    }

    const NativePcmOutputReadyInfo& readyInfo() const noexcept override { return info; }

private:
    MiniaudioSharedOutput output;
    NativePcmOutputReadyInfo info;
};

#ifdef _WIN32
void queueWasapiNotification(void* userData, const wasapi_host_notification* notification)
{
    auto* queue = static_cast<NativePcmOutputNotificationQueue*>(userData);
    if (queue == nullptr || notification == nullptr || notification->event == nullptr)
        return;
    queue->push({
        notification->event,
        wideToUtf8(notification->deviceId),
        notification->reason != nullptr ? notification->reason : "unknown",
        notification->code,
        notification->currentDevice != 0,
        notification->followsDefaultDevice != 0,
    });
}

unsigned int renderNativePcm(
    void* userData,
    float* output,
    unsigned int frameCount,
    unsigned int channels)
{
    auto* source = static_cast<PcmRingAudioSource*>(userData);
    if (source == nullptr || output == nullptr || channels == 0)
        return 0;
    return source->renderInterleaved(output, frameCount, channels);
}

bool pollAsioNotification(
    asio_runtime* runtime,
    std::uint32_t& pendingRuntimeEvents,
    std::uint32_t& pendingSampleRate,
    NativePcmOutputNotification& notification)
{
    if (pendingRuntimeEvents == ASIO_RUNTIME_EVENT_NONE && runtime != nullptr)
    {
        asio_runtime_events events {};
        if (asio_poll_runtime_events(runtime, &events) != 0)
        {
            pendingRuntimeEvents = events.flags;
            pendingSampleRate = events.sampleRate;
        }
    }

    if ((pendingRuntimeEvents & ASIO_RUNTIME_EVENT_RENDER_FAILED) != 0)
    {
        pendingRuntimeEvents &= ~ASIO_RUNTIME_EVENT_RENDER_FAILED;
        notification = {"audio_session_disconnected", {}, "asio_render_failed", 0, true, false};
        return true;
    }
    if ((pendingRuntimeEvents & ASIO_RUNTIME_EVENT_RESET_REQUEST) != 0)
    {
        pendingRuntimeEvents &= ~ASIO_RUNTIME_EVENT_RESET_REQUEST;
        notification = {"audio_session_disconnected", {}, "asio_reset_request", 0, true, false};
        return true;
    }
    if ((pendingRuntimeEvents & ASIO_RUNTIME_EVENT_RESYNC_REQUEST) != 0)
    {
        pendingRuntimeEvents &= ~ASIO_RUNTIME_EVENT_RESYNC_REQUEST;
        notification = {"audio_session_disconnected", {}, "asio_resync_request", 0, true, false};
        return true;
    }
    if ((pendingRuntimeEvents & ASIO_RUNTIME_EVENT_LATENCIES_CHANGED) != 0)
    {
        pendingRuntimeEvents &= ~ASIO_RUNTIME_EVENT_LATENCIES_CHANGED;
        notification = {"audio_session_disconnected", {}, "asio_latencies_changed", 0, true, false};
        return true;
    }
    if ((pendingRuntimeEvents & ASIO_RUNTIME_EVENT_SAMPLE_RATE_CHANGED) != 0)
    {
        pendingRuntimeEvents &= ~ASIO_RUNTIME_EVENT_SAMPLE_RATE_CHANGED;
        notification = {"device_sample_rate_changed", {}, "asio_sample_rate_changed", pendingSampleRate, true, false};
        pendingSampleRate = 0;
        return true;
    }
    return false;
}

class WasapiSharedPcmOutputBackend final : public NativePcmOutputBackend
{
public:
    ~WasapiSharedPcmOutputBackend() override { close(); }

    bool open(
        NativePlaybackPipeline& pipeline,
        const Options& options,
        int sampleRate,
        int channels,
        int bufferFrames,
        std::string& error) override
    {
        char errorBuffer[2048]{};
        wasapi_shared_ready_info ready{};
        auto& source = pipeline.pcmSource();
        source.prepareForNativeRender(std::max(16384, bufferFrames), static_cast<double>(sampleRate));
        const int result = wasapi_shared_start(
            options.deviceName.empty() ? nullptr : options.deviceName.c_str(),
            options.deviceIndex,
            static_cast<uint32_t>(sampleRate),
            static_cast<uint32_t>(channels),
            static_cast<uint32_t>(std::max(0, bufferFrames)),
            renderNativePcm,
            &source,
            queueWasapiNotification,
            &notifications,
            &runtime,
            &ready,
            errorBuffer,
            sizeof(errorBuffer));
        if (result != 0)
        {
            error = errorBuffer[0] != '\0' ? errorBuffer : "WASAPI shared start failed";
            return false;
        }
        info.sampleRate = static_cast<int>(ready.sampleRate);
        info.hardwareSampleRate = static_cast<int>(ready.hardwareSampleRate);
        info.channels = static_cast<int>(ready.channels);
        info.deviceBufferFrames = static_cast<int>(ready.bufferFrameCount);
        info.openedDeviceBufferFrames = info.deviceBufferFrames;
        info.exclusive = false;
        info.asio = false;
        info.format = ready.format;
        info.backend = "wasapi-shared";
        info.backendImpl = "wasapi-shared-native";
        info.deviceType = "Windows Audio";
        info.deviceName = options.deviceName.empty() ? "Default Output" : options.deviceName;
        return true;
    }

    void close() noexcept override
    {
        if (runtime != nullptr)
        {
            wasapi_shared_stop(runtime);
            runtime = nullptr;
        }
    }

    const NativePcmOutputReadyInfo& readyInfo() const noexcept override { return info; }
    bool pollNotification(NativePcmOutputNotification& notification) override
    {
        return notifications.pop(notification);
    }

private:
    wasapi_shared_runtime* runtime = nullptr;
    NativePcmOutputReadyInfo info;
    NativePcmOutputNotificationQueue notifications;
};

class WasapiExclusivePcmOutputBackend final : public NativePcmOutputBackend
{
public:
    ~WasapiExclusivePcmOutputBackend() override { close(); }

    bool open(
        NativePlaybackPipeline& pipeline,
        const Options& options,
        int sampleRate,
        int channels,
        int bufferFrames,
        std::string& error) override
    {
        char errorBuffer[2048]{};
        wasapi_exclusive_ready_info ready{};
        auto& source = pipeline.pcmSource();
        source.prepareForNativeRender(std::max(16384, bufferFrames), static_cast<double>(sampleRate));
        const int result = wasapi_exclusive_start(
            options.deviceName.empty() ? nullptr : options.deviceName.c_str(),
            options.deviceIndex,
            static_cast<uint32_t>(sampleRate),
            static_cast<uint32_t>(channels),
            static_cast<uint32_t>(std::max(0, bufferFrames)),
            options.mqaPcmPassthroughRequested ? 1 : 0,
            renderNativePcm,
            &source,
            queueWasapiNotification,
            &notifications,
            &runtime,
            &ready,
            errorBuffer,
            sizeof(errorBuffer));
        if (result != 0)
        {
            error = errorBuffer[0] != '\0' ? errorBuffer : "WASAPI exclusive start failed";
            return false;
        }
        info.sampleRate = static_cast<int>(ready.sampleRate);
        info.hardwareSampleRate = static_cast<int>(ready.hardwareSampleRate);
        info.channels = static_cast<int>(ready.channels);
        info.deviceBufferFrames = static_cast<int>(ready.bufferFrameCount);
        info.openedDeviceBufferFrames = info.deviceBufferFrames;
        info.exclusive = true;
        info.asio = false;
        info.format = ready.format;
        info.backend = "wasapi-exclusive";
        info.backendImpl = "wasapi-exclusive-native";
        info.deviceType = "WASAPI exclusive";
        info.deviceName = options.deviceName.empty() ? "Default Exclusive Output" : options.deviceName;
        return true;
    }

    void close() noexcept override
    {
        if (runtime != nullptr)
        {
            wasapi_exclusive_stop(runtime);
            runtime = nullptr;
        }
    }

    const NativePcmOutputReadyInfo& readyInfo() const noexcept override { return info; }
    bool pollNotification(NativePcmOutputNotification& notification) override
    {
        return notifications.pop(notification);
    }

private:
    wasapi_exclusive_runtime* runtime = nullptr;
    NativePcmOutputReadyInfo info;
    NativePcmOutputNotificationQueue notifications;
};

class AsioPcmOutputBackend final : public NativePcmOutputBackend
{
public:
    ~AsioPcmOutputBackend() override { close(); }

    bool open(
        NativePlaybackPipeline& pipeline,
        const Options& options,
        int sampleRate,
        int channels,
        int bufferFrames,
        std::string& error) override
    {
        char errorBuffer[2048]{};
        asio_ready_info ready{};
        auto& source = pipeline.pcmSource();
        source.prepareForNativeRender(std::max(16384, bufferFrames), static_cast<double>(sampleRate));
        const int result = asio_start(
            options.deviceName.empty() ? nullptr : options.deviceName.c_str(),
            options.deviceIndex,
            static_cast<uint32_t>(sampleRate),
            static_cast<uint32_t>(channels),
            static_cast<uint32_t>(std::max(0, bufferFrames)),
            static_cast<uint32_t>(std::max(0, options.asioOutputChannelStart)),
            options.mqaPcmPassthroughRequested ? 1 : 0,
            renderNativePcm,
            &source,
            &runtime,
            &ready,
            errorBuffer,
            sizeof(errorBuffer));
        if (result != 0)
        {
            error = errorBuffer[0] != '\0' ? errorBuffer : "ASIO start failed";
            return false;
        }
        info.sampleRate = static_cast<int>(ready.sampleRate);
        info.hardwareSampleRate = info.sampleRate;
        info.channels = static_cast<int>(ready.channels);
        info.deviceBufferFrames = static_cast<int>(ready.bufferFrameCount);
        info.openedDeviceBufferFrames = static_cast<int>(ready.bufferFrameCount);
        info.exclusive = true;
        info.asio = true;
        info.format = ready.format;
        info.backend = "asio";
        info.backendImpl = "asio-native";
        info.deviceType = "ASIO";
        info.deviceName = ready.deviceName[0] != '\0'
            ? ready.deviceName
            : (options.deviceName.empty() ? "Default ASIO Output" : options.deviceName);
        return true;
    }

    void close() noexcept override
    {
        if (runtime != nullptr)
        {
            asio_stop(runtime);
            runtime = nullptr;
        }
    }

    const NativePcmOutputReadyInfo& readyInfo() const noexcept override { return info; }
    bool pollNotification(NativePcmOutputNotification& notification) override
    {
        return pollAsioNotification(runtime, pendingRuntimeEvents, pendingSampleRate, notification);
    }

private:
    asio_runtime* runtime = nullptr;
    NativePcmOutputReadyInfo info;
    std::uint32_t pendingRuntimeEvents = ASIO_RUNTIME_EVENT_NONE;
    std::uint32_t pendingSampleRate = 0;
};

class KernelStreamingPcmOutputBackend final : public NativePcmOutputBackend
{
public:
    ~KernelStreamingPcmOutputBackend() override { close(); }

    bool open(NativePlaybackPipeline& pipeline, const Options& options, int sampleRate, int channels,
              int bufferFrames, std::string& error) override
    {
        char errorBuffer[2048]{};
        ks_ready_info ready{};
        auto& source = pipeline.pcmSource();
        source.prepareForNativeRender(std::max(16384, bufferFrames), static_cast<double>(sampleRate));
        const int result = ks_output_start(
            options.deviceName.empty() ? nullptr : options.deviceName.c_str(), options.deviceIndex,
            static_cast<uint32_t>(sampleRate), static_cast<uint32_t>(channels),
            static_cast<uint32_t>(std::max(0, bufferFrames)), renderNativePcm, &source,
            &runtime, &ready, errorBuffer, sizeof(errorBuffer));
        if (result != 0)
        {
            error = errorBuffer[0] != '\0' ? errorBuffer : "WDM-KS start failed";
            return false;
        }
        info.sampleRate = static_cast<int>(ready.sampleRate);
        info.hardwareSampleRate = info.sampleRate;
        info.channels = static_cast<int>(ready.channels);
        info.deviceBufferFrames = static_cast<int>(ready.bufferFrameCount);
        info.openedDeviceBufferFrames = bufferFrames;
        info.exclusive = true;
        info.asio = false;
        info.format = ready.format;
        info.backend = "wdm-ks";
        info.backendImpl = "wdm-ks-native";
        info.deviceType = "WDM-KS";
        info.deviceName = ready.deviceName[0] != '\0' ? ready.deviceName : options.deviceName;
        return true;
    }

    void close() noexcept override
    {
        if (runtime != nullptr)
        {
            ks_output_stop(runtime);
            runtime = nullptr;
        }
    }

    const NativePcmOutputReadyInfo& readyInfo() const noexcept override { return info; }

private:
    ks_runtime* runtime = nullptr;
    NativePcmOutputReadyInfo info;
};

unsigned int renderNativeDop(
    void* userData,
    uint32_t* output,
    unsigned int frameCount,
    unsigned int channels)
{
    auto* source = static_cast<DopRingSource*>(userData);
    return source != nullptr ? source->renderInterleaved(output, frameCount, channels) : 0;
}

unsigned int renderNativeDsd(
    void* userData,
    uint8_t* output,
    unsigned int byteFrameCount,
    unsigned int channels)
{
    auto* source = static_cast<NativeDsdRingSource*>(userData);
    return source != nullptr ? source->renderInterleaved(output, byteFrameCount, channels) : 0;
}

class WasapiExclusiveDopOutputBackend final : public NativePcmOutputBackend
{
public:
    ~WasapiExclusiveDopOutputBackend() override { close(); }

    bool open(NativePlaybackPipeline& pipeline, const Options& options, int sampleRate, int channels,
              int bufferFrames, std::string& error) override
    {
        char errorBuffer[2048]{};
        wasapi_exclusive_ready_info ready{};
        const int result = wasapi_exclusive_start_dop(
            options.deviceName.empty() ? nullptr : options.deviceName.c_str(), options.deviceIndex,
            static_cast<uint32_t>(sampleRate), static_cast<uint32_t>(channels),
            static_cast<uint32_t>(std::max(0, bufferFrames)), renderNativeDop, &pipeline.dopSource(),
            queueWasapiNotification, &notifications, &runtime, &ready, errorBuffer, sizeof(errorBuffer));
        if (result != 0)
        {
            error = errorBuffer[0] != '\0' ? errorBuffer : "WASAPI exclusive DoP start failed";
            return false;
        }
        info.sampleRate = static_cast<int>(ready.sampleRate);
        info.hardwareSampleRate = static_cast<int>(ready.hardwareSampleRate);
        info.channels = static_cast<int>(ready.channels);
        info.deviceBufferFrames = static_cast<int>(ready.bufferFrameCount);
        info.openedDeviceBufferFrames = info.deviceBufferFrames;
        info.exclusive = true;
        info.format = ready.format;
        info.backend = "wasapi-exclusive-dop";
        info.backendImpl = "wasapi-exclusive-dop-native";
        info.deviceType = "WASAPI exclusive DoP";
        info.deviceName = options.deviceName.empty() ? "Default Exclusive DoP Output" : options.deviceName;
        return true;
    }

    void close() noexcept override
    {
        if (runtime != nullptr) { wasapi_exclusive_stop(runtime); runtime = nullptr; }
    }
    const NativePcmOutputReadyInfo& readyInfo() const noexcept override { return info; }
    bool pollNotification(NativePcmOutputNotification& notification) override
    {
        return notifications.pop(notification);
    }

private:
    wasapi_exclusive_runtime* runtime = nullptr;
    NativePcmOutputReadyInfo info;
    NativePcmOutputNotificationQueue notifications;
};

class AsioDopOutputBackend final : public NativePcmOutputBackend
{
public:
    ~AsioDopOutputBackend() override { close(); }

    bool open(NativePlaybackPipeline& pipeline, const Options& options, int sampleRate, int channels,
              int bufferFrames, std::string& error) override
    {
        char errorBuffer[2048]{};
        asio_ready_info ready{};
        const int result = asio_start_dop(
            options.deviceName.empty() ? nullptr : options.deviceName.c_str(), options.deviceIndex,
            static_cast<uint32_t>(sampleRate), static_cast<uint32_t>(channels),
            static_cast<uint32_t>(std::max(0, bufferFrames)), static_cast<uint32_t>(std::max(0, options.asioOutputChannelStart)),
            renderNativeDop, &pipeline.dopSource(), &runtime, &ready, errorBuffer, sizeof(errorBuffer));
        if (result != 0)
        {
            error = errorBuffer[0] != '\0' ? errorBuffer : "ASIO DoP start failed";
            return false;
        }
        fillReady(ready, options, "asio-dop", "asio-dop-native", "ASIO DoP");
        return true;
    }

    void close() noexcept override { if (runtime != nullptr) { asio_stop(runtime); runtime = nullptr; } }
    const NativePcmOutputReadyInfo& readyInfo() const noexcept override { return info; }
    bool pollNotification(NativePcmOutputNotification& notification) override
    {
        return pollAsioNotification(runtime, pendingRuntimeEvents, pendingSampleRate, notification);
    }

protected:
    void fillReady(const asio_ready_info& ready, const Options& options, const char* backend, const char* impl, const char* type)
    {
        info.sampleRate = static_cast<int>(ready.sampleRate);
        info.hardwareSampleRate = info.sampleRate;
        info.channels = static_cast<int>(ready.channels);
        info.deviceBufferFrames = static_cast<int>(ready.bufferFrameCount);
        info.openedDeviceBufferFrames = static_cast<int>(ready.bufferFrameCount);
        info.exclusive = true;
        info.asio = true;
        info.format = ready.format;
        info.backend = backend;
        info.backendImpl = impl;
        info.deviceType = type;
        info.deviceName = ready.deviceName[0] != '\0' ? ready.deviceName : options.deviceName;
    }

    asio_runtime* runtime = nullptr;
    NativePcmOutputReadyInfo info;
    std::uint32_t pendingRuntimeEvents = ASIO_RUNTIME_EVENT_NONE;
    std::uint32_t pendingSampleRate = 0;
};

class AsioNativeDsdOutputBackend final : public NativePcmOutputBackend
{
public:
    ~AsioNativeDsdOutputBackend() override { close(); }

    bool open(NativePlaybackPipeline& pipeline, const Options& options, int sampleRate, int channels,
              int bufferFrames, std::string& error) override
    {
        char errorBuffer[2048]{};
        asio_ready_info ready{};
        const int result = asio_start_native_dsd(
            options.deviceName.empty() ? nullptr : options.deviceName.c_str(), options.deviceIndex,
            static_cast<uint32_t>(sampleRate), static_cast<uint32_t>(channels),
            static_cast<uint32_t>(std::max(0, bufferFrames)), static_cast<uint32_t>(std::max(0, options.asioOutputChannelStart)),
            renderNativeDsd, &pipeline.nativeDsdSource(), &runtime, &ready, errorBuffer, sizeof(errorBuffer));
        if (result != 0)
        {
            error = errorBuffer[0] != '\0' ? errorBuffer : "ASIO native DSD start failed";
            return false;
        }
        info.sampleRate = static_cast<int>(ready.sampleRate);
        info.hardwareSampleRate = info.sampleRate;
        info.channels = static_cast<int>(ready.channels);
        info.deviceBufferFrames = static_cast<int>(ready.bufferFrameCount);
        info.openedDeviceBufferFrames = static_cast<int>(ready.bufferFrameCount);
        info.exclusive = true;
        info.asio = true;
        info.format = ready.format;
        info.backend = "asio-native-dsd";
        info.backendImpl = "asio-native-dsd";
        info.deviceType = "ASIO native DSD";
        info.deviceName = ready.deviceName[0] != '\0' ? ready.deviceName : options.deviceName;
        return true;
    }

    void close() noexcept override { if (runtime != nullptr) { asio_stop(runtime); runtime = nullptr; } }
    const NativePcmOutputReadyInfo& readyInfo() const noexcept override { return info; }
    bool pollNotification(NativePcmOutputNotification& notification) override
    {
        return pollAsioNotification(runtime, pendingRuntimeEvents, pendingSampleRate, notification);
    }

private:
    asio_runtime* runtime = nullptr;
    NativePcmOutputReadyInfo info;
    std::uint32_t pendingRuntimeEvents = ASIO_RUNTIME_EVENT_NONE;
    std::uint32_t pendingSampleRate = 0;
};
#endif

#if defined(__APPLE__)
class MacCoreAudioDopOutputBackend final : public NativePcmOutputBackend
{
public:
    ~MacCoreAudioDopOutputBackend() override { close(); }

    bool open(
        NativePlaybackPipeline& pipeline,
        const Options& options,
        int sampleRate,
        int channels,
        int bufferFrames,
        std::string& error) override
    {
        if (! output.open(
                pipeline.dopSource(),
                options.deviceName.empty() ? nullptr : options.deviceName.c_str(),
                options.deviceIndex,
                sampleRate,
                channels,
                bufferFrames,
                writeCoreAudioDeviceNotification,
                &output,
                error))
        {
            return false;
        }
        const auto& ready = output.readyInfo();
        info.sampleRate = ready.sampleRate;
        info.hardwareSampleRate = ready.hardwareSampleRate;
        info.channels = ready.channels;
        info.deviceBufferFrames = ready.bufferFrameCount;
        info.openedDeviceBufferFrames = ready.bufferFrameCount;
        info.exclusive = true;
        info.asio = false;
        info.format = ready.format;
        info.backend = "coreaudio-exclusive-dop";
        info.backendImpl = "coreaudio-hal-ioproc-dop";
        info.deviceType = "CoreAudio Exclusive DoP";
        info.deviceName = ready.deviceName.empty() ? "Default CoreAudio DoP Output" : ready.deviceName;
        return true;
    }

    void close() noexcept override { output.close(); }
    const NativePcmOutputReadyInfo& readyInfo() const noexcept override { return info; }

private:
    MacCoreAudioDopOutput output;
    NativePcmOutputReadyInfo info;
};
#endif

#if defined(__linux__)
class LinuxPipeWireSharedOutputBackend final : public NativePcmOutputBackend
{
public:
    ~LinuxPipeWireSharedOutputBackend() override { close(); }

    bool open(
        NativePlaybackPipeline& pipeline,
        const Options& options,
        int sampleRate,
        int channels,
        int bufferFrames,
        std::string& error) override
    {
        if (! output.open(
                pipeline.pcmSource(),
                options.deviceName.empty() ? nullptr : options.deviceName.c_str(),
                options.deviceIndex,
                options.deviceId.empty() ? nullptr : options.deviceId.c_str(),
                sampleRate,
                channels,
                bufferFrames,
                error))
        {
            return false;
        }
        const auto& ready = output.readyInfo();
        info.sampleRate = ready.sampleRate;
        info.hardwareSampleRate = ready.hardwareSampleRate;
        info.channels = ready.channels;
        info.deviceBufferFrames = ready.bufferFrameCount;
        info.openedDeviceBufferFrames = ready.bufferFrameCount;
        info.exclusive = false;
        info.asio = false;
        info.format = ready.format;
        info.backend = "pipewire-shared";
        info.backendImpl = "pipewire-stream-f32";
        info.deviceType = "PipeWire";
        info.deviceName = ready.deviceName.empty() ? ready.nodeName : ready.deviceName;
        return true;
    }

    void close() noexcept override { output.close(); }
    const NativePcmOutputReadyInfo& readyInfo() const noexcept override { return info; }
    bool pollNotification(NativePcmOutputNotification& notification) override
    {
        const int failure = output.takeFailure();
        if (failure == 0) return false;
        notification = {"audio_session_disconnected", {}, "pipewire_stream_failed", static_cast<std::uint32_t>(failure), true, false};
        return true;
    }

private:
    LinuxPipeWireSharedOutput output;
    NativePcmOutputReadyInfo info;
};

class LinuxAlsaNativeDsdOutputBackend final : public NativePcmOutputBackend
{
public:
    ~LinuxAlsaNativeDsdOutputBackend() override { close(); }

    bool open(
        NativePlaybackPipeline& pipeline,
        const Options& options,
        int sampleRate,
        int channels,
        int bufferFrames,
        std::string& error) override
    {
        if (! output.open(
                pipeline.nativeDsdSource(),
                options.deviceName.empty() ? nullptr : options.deviceName.c_str(),
                options.deviceIndex,
                options.deviceId.empty() ? nullptr : options.deviceId.c_str(),
                sampleRate,
                channels,
                bufferFrames,
                error))
        {
            return false;
        }
        const auto& ready = output.readyInfo();
        info.sampleRate = ready.sampleRate;
        info.hardwareSampleRate = ready.hardwareSampleRate;
        info.channels = ready.channels;
        info.deviceBufferFrames = ready.bufferFrameCount;
        info.openedDeviceBufferFrames = ready.bufferFrameCount;
        info.exclusive = true;
        info.asio = false;
        info.format = ready.format;
        info.backend = "alsa-exclusive-native-dsd";
        info.backendImpl = "alsa-dsd-u32-be";
        info.deviceType = "ALSA exclusive native DSD";
        info.deviceName = ready.deviceName.empty() ? ready.pcmName : ready.deviceName;
        return true;
    }

    void close() noexcept override { output.close(); }
    const NativePcmOutputReadyInfo& readyInfo() const noexcept override { return info; }
    bool pollNotification(NativePcmOutputNotification& notification) override
    {
        const int failure = output.takeFailure();
        if (failure == 0) return false;
        notification = {"audio_session_disconnected", {}, "alsa_native_dsd_failed", static_cast<std::uint32_t>(failure), true, false};
        return true;
    }

private:
    LinuxAlsaNativeDsdOutput output;
    NativePcmOutputReadyInfo info;
};
#endif

std::unique_ptr<NativePcmOutputBackend> createNativePcmOutputBackend(const Options& options)
{
#ifdef _WIN32
    if (options.asioNativeDsdOutput)
        return std::make_unique<AsioNativeDsdOutputBackend>();
    if (options.dopOutput && options.asio)
        return std::make_unique<AsioDopOutputBackend>();
    if (options.dopOutput && options.exclusive)
        return std::make_unique<WasapiExclusiveDopOutputBackend>();
#endif
#if defined(__APPLE__)
    if (options.dopOutput && options.exclusive)
        return std::make_unique<MacCoreAudioDopOutputBackend>();
#endif
#if defined(__linux__)
    if (options.asioNativeDsdOutput && options.exclusive)
        return std::make_unique<LinuxAlsaNativeDsdOutputBackend>();
#endif
    switch (selectNativePcmOutputBackend(options))
    {
        case NativePcmOutputBackendKind::Miniaudio:
            return std::make_unique<MiniaudioPcmOutputBackend>();
        case NativePcmOutputBackendKind::PipeWire:
#if defined(__linux__)
            return std::make_unique<LinuxPipeWireSharedOutputBackend>();
#else
            break;
#endif
#ifdef _WIN32
        case NativePcmOutputBackendKind::WasapiShared:
            return std::make_unique<WasapiSharedPcmOutputBackend>();
        case NativePcmOutputBackendKind::WasapiExclusive:
            return std::make_unique<WasapiExclusivePcmOutputBackend>();
        case NativePcmOutputBackendKind::Asio:
            return std::make_unique<AsioPcmOutputBackend>();
        case NativePcmOutputBackendKind::KernelStreaming:
            return std::make_unique<KernelStreamingPcmOutputBackend>();
#else
        case NativePcmOutputBackendKind::WasapiShared:
        case NativePcmOutputBackendKind::WasapiExclusive:
        case NativePcmOutputBackendKind::Asio:
        case NativePcmOutputBackendKind::KernelStreaming:
            break;
#endif
    }
    throw std::runtime_error("requested native PCM output backend is unavailable on this platform");
}

bool isTransientAsioBufferRejection(const std::string& error)
{
    return containsIgnoreCase(error, "ASIO buffer size candidates rejected")
        || containsIgnoreCase(error, "ASIOCreateBuffers failed");
}

bool openNativePcmOutputWithFallback(
    NativePcmOutputBackend& backend,
    NativePlaybackPipeline& pipeline,
    const Options& options,
    int sampleRate,
    int channels,
    int requestedBufferFrames,
    int& openedBufferFrames,
    std::string& error,
    int preferredBufferFrames = 0)
{
    static constexpr int asioSettleDelaysMs[] { 250, 500 };
    static constexpr size_t maxAsioSettleRetries = sizeof(asioSettleDelaysMs) / sizeof(asioSettleDelaysMs[0]);
    size_t asioSettleRetriesUsed = 0;

    std::string lastError;
    const auto attempts = buildPreferredBufferSizeAttempts(requestedBufferFrames, preferredBufferFrames);
    size_t attemptIndex = 0;
    while (attemptIndex < attempts.size())
    {
        const int candidate = attempts[attemptIndex];
        backend.close();
        std::string attemptError;
        if (backend.open(pipeline, options, sampleRate, channels, candidate, attemptError))
        {
            const int actualOpenedBufferFrames = backend.readyInfo().openedDeviceBufferFrames;
            openedBufferFrames = actualOpenedBufferFrames > 0 ? actualOpenedBufferFrames : candidate;
            error.clear();
            return true;
        }
        lastError = attemptError;
        logLine("native PCM output buffer attempt failed: frames=" + std::to_string(candidate)
            + " error=" + attemptError);
        if (containsIgnoreCase(attemptError, "alsa_exclusive_device_busy"))
            break; // The bounded idle-release wait already ran; buffer size cannot fix ownership.
        if (containsIgnoreCase(attemptError, "format unsupported")
            || containsIgnoreCase(attemptError, "format_unsupported")
            || containsIgnoreCase(attemptError, "AUDCLNT_E_UNSUPPORTED_FORMAT")
            || containsIgnoreCase(attemptError, "0x88890008"))
        {
            logLine("native PCM output format is unsupported; skipping buffer-only retries");
            break;
        }
        if (asioSettleRetriesUsed < maxAsioSettleRetries && isTransientAsioBufferRejection(attemptError))
        {
            const int settleDelayMs = asioSettleDelaysMs[asioSettleRetriesUsed];
            ++asioSettleRetriesUsed;
            logLine("ASIO driver rejected all buffer sizes; retrying open after "
                + std::to_string(settleDelayMs) + "ms settle delay");
            std::this_thread::sleep_for(std::chrono::milliseconds(settleDelayMs));
            continue;
        }
        ++attemptIndex;
    }
    error = lastError.empty() ? "all native PCM output buffer attempts failed" : lastError;
    return false;
}

bool configureRuntimeOutputOptions(
    const echo_audio_host::Json& params,
    const Options& current,
    Options& next,
    std::string& error)
{
    next = current;
    const std::string outputMode = getJsonString(params, "outputMode",
        current.kernelStreaming ? "ks" : current.asio ? "asio" : current.exclusive ? "exclusive" : "shared");
    if (outputMode == "shared" || outputMode == "system")
    {
        next.asio = false;
        next.kernelStreaming = false;
        next.exclusive = false;
    }
    else if (outputMode == "exclusive")
    {
        next.asio = false;
        next.kernelStreaming = false;
        next.exclusive = true;
    }
    else if (outputMode == "asio")
    {
        next.asio = true;
        next.kernelStreaming = false;
        next.exclusive = false;
    }
    else if (outputMode == "ks")
    {
        next.asio = false;
        next.kernelStreaming = true;
        next.exclusive = false;
    }
    else
    {
        error = "unsupported outputMode: " + outputMode;
        return false;
    }

#if defined(__APPLE__)
    if (next.asio || next.kernelStreaming)
    {
        error = "ASIO and WDM-KS output are only supported on Windows";
        return false;
    }
#elif !defined(_WIN32)
    if (next.asio || next.kernelStreaming)
    {
        error = "ASIO and WDM-KS output are only supported on Windows";
        return false;
    }
#endif

    const std::string sharedBackend = getJsonString(params, "sharedBackend", next.sharedBackend);
    if (sharedBackend != "auto" && sharedBackend != "windows" && sharedBackend != "directsound"
        && sharedBackend != "alsa" && sharedBackend != "pipewire" && sharedBackend != "miniaudio")
    {
        error = "unsupported sharedBackend: " + sharedBackend;
        return false;
    }
    next.sharedBackend = sharedBackend;
    next.deviceId = getJsonString(params, "deviceId", next.deviceId);
    next.deviceName = getJsonString(params, "deviceName", next.deviceName);
    next.deviceIndex = static_cast<int>(getJsonDouble(params, "deviceIndex", next.deviceIndex));
    next.sampleRate = std::max(1, static_cast<int>(getJsonDouble(params, "sampleRate", next.sampleRate)));
    const double requestedChannels = getJsonDouble(params, "channels", current.channels);
    if (!std::isfinite(requestedChannels) || requestedChannels != std::floor(requestedChannels)
        || requestedChannels < 2 || requestedChannels > maxNativePcmChannels)
    {
        error = "invalid native PCM output channel count";
        return false;
    }
    next.channels = static_cast<int>(requestedChannels);
    next.bufferSize = std::max(0, static_cast<int>(getJsonDouble(params, "bufferSize", next.bufferSize)));
    next.asioOutputChannelStart = std::max(0, static_cast<int>(getJsonDouble(params, "asioOutputChannelStart", next.asioOutputChannelStart)));

    if (next.channels != current.channels)
    {
        error = "daemon channel layout requires a new host initialization";
        return false;
    }
    const auto processing = params.find("processing");
    const std::string outputFormat = processing != params.end() && processing->is_object()
        ? getJsonString(*processing, "outputFormat", "pcm") : "pcm";
    if (next.channels > 2 && (!next.asio || outputFormat != "pcm"))
    {
        error = "multichannel native PCM output currently requires ASIO";
        return false;
    }

    if (next.bufferSize <= 0)
    {
        const std::string latencyProfile = getJsonString(params, "latencyProfile", "balanced");
        next.bufferSize = latencyProfile == "lowLatency" ? 1024 : latencyProfile == "stable" ? 8192 : 2048;
    }
    return true;
}

echo::PcmDitherMode parseNativeDitherMode(const std::string& mode)
{
    if (mode == "tpdf") return echo::PcmDitherMode::Tpdf;
    if (mode == "highpass-tpdf") return echo::PcmDitherMode::HighpassTpdf;
    if (mode == "ns-5") return echo::PcmDitherMode::NoiseShaped5;
    if (mode == "ns-9") return echo::PcmDitherMode::NoiseShaped9;
    if (mode == "ultra-shaped") return echo::PcmDitherMode::UltraShaped;
    return echo::PcmDitherMode::Off;
}

echo::SdmQualityProfile parseNativeSdmProfile(const std::string& profile)
{
    if (profile == "hifi") return echo::SdmQualityProfile::Hifi;
    if (profile == "reference") return echo::SdmQualityProfile::Reference;
    if (profile == "insane") return echo::SdmQualityProfile::Insane;
    if (profile == "adaptive") return echo::SdmQualityProfile::Adaptive;
    if (profile == "adaptive-light") return echo::SdmQualityProfile::AdaptiveLight;
    if (profile == "adaptive-super") return echo::SdmQualityProfile::AdaptiveSuper;
    if (profile == "reference-ec") return echo::SdmQualityProfile::ReferenceEc;
    return echo::SdmQualityProfile::Safe;
}

const char* nativeOutputFormatText(NativePlaybackPipeline::OutputFormat format)
{
    switch (format)
    {
        case NativePlaybackPipeline::OutputFormat::Dop: return "dop24le";
        case NativePlaybackPipeline::OutputFormat::NativeDsd: return "dsd-native-raw";
        case NativePlaybackPipeline::OutputFormat::Pcm: return "pcm";
    }
    return "pcm";
}

const char* nativeComputeBackendText(NativePlaybackPipeline::ComputeBackend backend)
{
    return backend == NativePlaybackPipeline::ComputeBackend::Cuda ? "cuda" : "cpu";
}

const char* nativeDitherModeText(echo::PcmDitherMode mode)
{
    switch (mode)
    {
        case echo::PcmDitherMode::Tpdf: return "tpdf";
        case echo::PcmDitherMode::HighpassTpdf: return "highpass-tpdf";
        case echo::PcmDitherMode::NoiseShaped5: return "ns-5";
        case echo::PcmDitherMode::NoiseShaped9: return "ns-9";
        case echo::PcmDitherMode::UltraShaped: return "ultra-shaped";
        case echo::PcmDitherMode::Off: return "off";
    }
    return "off";
}

echo_audio_host::Json nativeFallbackReasonJson(const std::string& reason)
{
    return reason.empty() ? echo_audio_host::Json(nullptr) : echo_audio_host::Json(reason);
}

echo_audio_host::Json buildNativeProcessingTelemetry(const NativePlaybackPipeline& pipeline)
{
    const auto status = pipeline.processingStatus();
    echo_audio_host::Json telemetry {
        {"outputFormat", nativeOutputFormatText(status.outputFormat)},
        {"dither", {
            {"active", status.ditherActive},
            {"mode", nativeDitherModeText(status.ditherMode)},
            {"bitDepth", status.ditherBitDepth},
        }},
        {"limiter", {
            {"active", status.limiter.active},
            {"protecting", status.limiter.protecting},
            {"ceilingDb", status.limiter.ceilingDb},
            {"gainReductionDb", status.limiter.gainReductionDb},
        }},
        {"mqaPassthrough", {
            {"requested", status.mqaPassthrough.requested},
            {"active", status.mqaPassthrough.active},
            {"reason", nativeFallbackReasonJson(status.mqaPassthrough.reason)},
            {"sourceSampleRate", status.mqaPassthrough.sourceSampleRate > 0
                ? echo_audio_host::Json(status.mqaPassthrough.sourceSampleRate)
                : echo_audio_host::Json(nullptr)},
            {"sourceBitDepth", status.mqaPassthrough.sourceBitDepth > 0
                ? echo_audio_host::Json(status.mqaPassthrough.sourceBitDepth)
                : echo_audio_host::Json(nullptr)},
            {"endpointFormat", status.mqaPassthrough.endpointFormat.empty()
                ? echo_audio_host::Json(nullptr)
                : echo_audio_host::Json(status.mqaPassthrough.endpointFormat)},
        }},
        {"workshopAudioEffect", {
            {"configured", status.workshopAudioEffect.configured},
            {"active", status.workshopAudioEffect.active},
            {"effect", echo::workshopAudioEffectTypeName(status.workshopAudioEffect.effect)},
            {"bitDepth", status.workshopAudioEffect.bitDepth},
            {"sampleRateHz", status.workshopAudioEffect.sampleRateHz},
            {"mix", status.workshopAudioEffect.mix},
            {"outputGainDb", status.workshopAudioEffect.outputGainDb},
            {"pulseMix", status.workshopAudioEffect.pulseMix},
            {"triangleMix", status.workshopAudioEffect.triangleMix},
            {"noiseMix", status.workshopAudioEffect.noiseMix},
            {"drive", status.workshopAudioEffect.drive},
            {"vocalCutStrength", status.workshopAudioEffect.vocalCutStrength},
            {"vocalCutBassPreserveHz", status.workshopAudioEffect.vocalCutBassPreserveHz},
            {"bypassReason", nativeFallbackReasonJson(status.workshopAudioEffect.bypassReason)},
        }},
        {"echoSrc", {
            {"active", status.echoSrc.active},
            {"sourceSampleRate", status.echoSrc.sourceSampleRate},
            {"targetSampleRate", status.echoSrc.targetSampleRate},
            {"stageCount", status.echoSrc.stageCount},
            {"requestedBackend", nativeComputeBackendText(status.echoSrc.requestedBackend)},
            {"activeBackend", nativeComputeBackendText(status.echoSrc.activeBackend)},
            {"estimatedMacsPerSecond", status.echoSrc.estimatedMacsPerSecond},
            {"nominalLatencyFrames", status.echoSrc.nominalLatencyFrames},
            {"nominalLatencyMilliseconds", status.echoSrc.nominalLatencyMilliseconds},
            {"processedBlocks", status.echoSrc.processedBlocks},
            {"lastInputFrames", status.echoSrc.lastInputFrames},
            {"lastOutputFrames", status.echoSrc.lastOutputFrames},
            {"lastProcessMilliseconds", status.echoSrc.lastProcessMilliseconds},
            {"averageProcessMilliseconds", status.echoSrc.averageProcessMilliseconds},
            {"peakProcessMilliseconds", status.echoSrc.peakProcessMilliseconds},
            {"warmupMilliseconds", status.echoSrc.warmupMilliseconds},
            {"runtimeFallbacks", status.echoSrc.runtimeFallbacks},
            {"deviceName", status.echoSrc.deviceName.empty()
                ? echo_audio_host::Json(nullptr)
                : echo_audio_host::Json(status.echoSrc.deviceName)},
            {"fallbackReason", nativeFallbackReasonJson(status.echoSrc.fallbackReason)},
        }},
        {"sdm", {
            {"active", status.sdm.active},
            {"sourceSampleRate", status.sdm.sourceSampleRate},
            {"targetSampleRate", status.sdm.targetSampleRate},
            {"stageCount", status.sdm.stageCount},
            {"requestedBackend", nativeComputeBackendText(status.sdm.requestedBackend)},
            {"activeBackend", nativeComputeBackendText(status.sdm.activeBackend)},
            {"modulatorBackend", nativeComputeBackendText(status.sdm.modulatorBackend)},
            {"oversamplingBackend", nativeComputeBackendText(status.sdm.oversamplingBackend)},
            {"estimatedMacsPerSecond", status.sdm.estimatedMacsPerSecond},
            {"processedBlocks", status.sdm.processedBlocks},
            {"lastInputFrames", status.sdm.lastInputFrames},
            {"lastOutputFrames", status.sdm.lastOutputFrames},
            {"lastProcessMilliseconds", status.sdm.lastProcessMilliseconds},
            {"averageProcessMilliseconds", status.sdm.averageProcessMilliseconds},
            {"peakProcessMilliseconds", status.sdm.peakProcessMilliseconds},
            {"warmupMilliseconds", status.sdm.warmupMilliseconds},
            {"runtimeFallbacks", status.sdm.runtimeFallbacks},
            {"oversamplingProcessedBlocks", status.sdm.oversamplingProcessedBlocks},
            {"oversamplingLastInputFrames", status.sdm.oversamplingLastInputFrames},
            {"oversamplingLastOutputFrames", status.sdm.oversamplingLastOutputFrames},
            {"oversamplingLastProcessMilliseconds", status.sdm.oversamplingLastProcessMilliseconds},
            {"oversamplingAverageProcessMilliseconds", status.sdm.oversamplingAverageProcessMilliseconds},
            {"oversamplingPeakProcessMilliseconds", status.sdm.oversamplingPeakProcessMilliseconds},
            {"oversamplingWarmupMilliseconds", status.sdm.oversamplingWarmupMilliseconds},
            {"oversamplingRuntimeFallbacks", status.sdm.oversamplingRuntimeFallbacks},
            {"realtimeAdmissionRatio", status.sdm.realtimeAdmissionRatio},
            {"realtimeAdmissionWithinBudget", status.sdm.realtimeAdmissionWithinBudget},
            {"realtimeAdmissionWarning", nativeFallbackReasonJson(status.sdm.realtimeAdmissionWarning)},
            {"deviceName", status.sdm.deviceName.empty()
                ? echo_audio_host::Json(nullptr)
                : echo_audio_host::Json(status.sdm.deviceName)},
            {"fallbackReason", nativeFallbackReasonJson(status.sdm.fallbackReason)},
            {"oversamplingFallbackReason",
                nativeFallbackReasonJson(status.sdm.oversamplingFallbackReason)},
            {"pcmDspRouted", status.sdm.pcmDspRouted},
            {"pcmDspSampleRate", status.sdm.pcmDspSampleRate},
            {"pcmDspMaximumBlockFrames", status.sdm.pcmDspMaximumBlockFrames},
            {"modulatorOrder", status.sdm.modulatorOrder},
            {"decisionMode", status.sdm.decisionMode.empty()
                ? echo_audio_host::Json(nullptr)
                : echo_audio_host::Json(status.sdm.decisionMode)},
            {"preModulatorProfile", status.sdm.preModulatorProfile.empty()
                ? echo_audio_host::Json(nullptr)
                : echo_audio_host::Json(status.sdm.preModulatorProfile)},
            {"ultimateEnabled", status.sdm.ultimateEnabled},
            {"sampleRateFamily", status.sdm.sampleRateFamily.empty()
                ? echo_audio_host::Json(nullptr)
                : echo_audio_host::Json(status.sdm.sampleRateFamily)},
            {"rationalConversion", status.sdm.rationalConversion},
            {"ntfPeakGain", status.sdm.ntfPeakGain},
            {"peakFeedbackState", status.sdm.peakFeedbackState},
            {"stabilityRecoveries", status.sdm.stabilityRecoveries},
        }},
    };
    if (const auto memory = readCurrentProcessMemoryTelemetry()) {
        telemetry["resourceUsage"] = {
            {"pid", memory->processId},
            {"workingSetBytes", memory->workingSetBytes},
            {"privateBytes", memory->privateBytes
                ? echo_audio_host::Json(*memory->privateBytes)
                : echo_audio_host::Json(nullptr)},
        };
    }
    return telemetry;
}

bool configureNativeProcessing(
    const echo_audio_host::Json& params,
    Options& options,
    NativePlaybackPipeline& pipeline,
    std::string& error)
{
    const echo_audio_host::Json* processing = nullptr;
    if (params.contains("processing") && params["processing"].is_object())
        processing = &params["processing"];
    const echo_audio_host::Json empty = echo_audio_host::Json::object();
    const auto& config = processing != nullptr ? *processing : empty;
    const std::string outputFormat = getJsonString(config, "outputFormat", "pcm");
    NativePlaybackPipeline::OutputFormat nativeFormat = NativePlaybackPipeline::OutputFormat::Pcm;
    if (outputFormat == "dop24le") nativeFormat = NativePlaybackPipeline::OutputFormat::Dop;
    else if (outputFormat == "dsd-native-raw") nativeFormat = NativePlaybackPipeline::OutputFormat::NativeDsd;
    else if (outputFormat != "pcm") { error = "native_processing_invalid_output_format"; return false; }

    if (nativeFormat == NativePlaybackPipeline::OutputFormat::Dop && !options.asio && !options.exclusive)
    {
        error = "native_sdm_dop_requires_exclusive_or_asio";
        return false;
    }
    if (nativeFormat == NativePlaybackPipeline::OutputFormat::NativeDsd && !options.asio)
    {
#if defined(__linux__)
        if (! options.exclusive)
        {
            error = "native_sdm_native_dsd_requires_alsa_exclusive";
            return false;
        }
#else
        error = "native_sdm_native_dsd_requires_asio";
        return false;
#endif
    }
    if (nativeFormat == NativePlaybackPipeline::OutputFormat::NativeDsd && options.sampleRate % 16 != 0)
    {
        error = "native_sdm_native_dsd_sample_rate_mismatch";
        return false;
    }

    NativePlaybackPipeline::ProcessingConfig processingConfig;
    processingConfig.outputFormat = nativeFormat;
    if (config.contains("mqaPassthrough") && config["mqaPassthrough"].is_object())
    {
        const auto& mqa = config["mqaPassthrough"];
        processingConfig.mqaPassthrough.requested = mqa.value("requested", false);
        processingConfig.mqaPassthrough.sourceSampleRate = static_cast<int>(getJsonDouble(mqa, "sourceSampleRate", 0));
        processingConfig.mqaPassthrough.sourceBitDepth = static_cast<int>(getJsonDouble(mqa, "bitDepth", 0));
        if (processingConfig.mqaPassthrough.requested
            && (processingConfig.mqaPassthrough.sourceSampleRate <= 0
                || (processingConfig.mqaPassthrough.sourceBitDepth != 16
                    && processingConfig.mqaPassthrough.sourceBitDepth != 24)))
        {
            error = "mqa_passthrough_invalid_source_format";
            return false;
        }
    }

    const auto parseComputeBackend = [&](const echo_audio_host::Json& object,
                                         const char* prefix,
                                         NativePlaybackPipeline::ComputeBackend& destination) -> bool
    {
        const std::string requested = getJsonString(object, "computeBackend", getJsonString(object, "backend", "cpu"));
        if (requested == "cpu")
        {
            destination = NativePlaybackPipeline::ComputeBackend::Cpu;
            return true;
        }
        if (requested == "cuda")
        {
            destination = NativePlaybackPipeline::ComputeBackend::Cuda;
            return true;
        }
        error = std::string(prefix) + "_invalid_compute_backend";
        return false;
    };

    const auto parseFir = [&](const echo_audio_host::Json& firJson,
                              const char* prefix,
                              NativePlaybackPipeline::FirConfig& destination,
                              int requiredDeviceTargetSampleRate,
                              int maximumUpsampleFactor) -> bool
    {
        destination.sourceSampleRate = static_cast<int>(getJsonDouble(firJson, "sourceSampleRate", 0));
        destination.targetSampleRate = static_cast<int>(getJsonDouble(firJson, "targetSampleRate", 0));
        if (!parseComputeBackend(firJson, prefix, destination.requestedBackend))
            return false;
        if (!firJson.contains("stages") || !firJson["stages"].is_array())
            return true;
        if (firJson["stages"].size() > 8)
        {
            error = std::string(prefix) + "_stage_limit_exceeded";
            return false;
        }

        int upsampleFactor = 1;
        for (const auto& stageJson : firJson["stages"])
        {
            if (!stageJson.is_object() || !stageJson.contains("taps") || !stageJson["taps"].is_array())
            {
                error = std::string(prefix) + "_invalid_stage";
                return false;
            }
            echo::EchoSrcStageConfig stage;
            stage.upsampleFactor = static_cast<int>(getJsonDouble(stageJson, "upsampleFactor", 1));
            if (stage.upsampleFactor != 1 && stage.upsampleFactor != 2
                && stage.upsampleFactor != 4 && stage.upsampleFactor != 8)
            {
                error = std::string(prefix) + "_invalid_upsample_factor";
                return false;
            }
            if (upsampleFactor > maximumUpsampleFactor / stage.upsampleFactor)
            {
                error = std::string(prefix) + "_upsample_factor_limit_exceeded";
                return false;
            }
            upsampleFactor *= stage.upsampleFactor;
            if (stageJson["taps"].size() > 8192)
            {
                error = std::string(prefix) + "_taps_limit_exceeded";
                return false;
            }
            for (const auto& tap : stageJson["taps"])
            {
                if (!tap.is_number())
                {
                    error = std::string(prefix) + "_invalid_tap";
                    return false;
                }
                stage.taps.push_back(tap.get<float>());
            }
            destination.stages.push_back(std::move(stage));
        }

        if (!destination.stages.empty()
            && (destination.sourceSampleRate <= 0 || destination.targetSampleRate <= 0
                || destination.sourceSampleRate > std::numeric_limits<int>::max() / upsampleFactor
                || destination.sourceSampleRate * upsampleFactor != destination.targetSampleRate
                || (requiredDeviceTargetSampleRate > 0
                    && destination.targetSampleRate != requiredDeviceTargetSampleRate)))
        {
            error = std::string(prefix) + "_sample_rate_mismatch";
            return false;
        }
        return true;
    };

    if (config.contains("echoSrc") && config["echoSrc"].is_object())
    {
        if (!parseFir(config["echoSrc"], "native_echo_src", processingConfig.echoSrc, options.sampleRate, 16))
            return false;
    }

    std::string ditherMode = "off";
    int ditherBitDepth = 16;
    if (config.contains("dither") && config["dither"].is_object())
    {
        ditherMode = getJsonString(config["dither"], "mode", "off");
        ditherBitDepth = static_cast<int>(getJsonDouble(config["dither"], "bitDepth", 16));
    }
    if (ditherMode != "off" && ditherMode != "tpdf" && ditherMode != "highpass-tpdf"
        && ditherMode != "ns-5" && ditherMode != "ns-9" && ditherMode != "ultra-shaped")
    {
        error = "native_dither_invalid_mode";
        return false;
    }
    if (ditherBitDepth != 16 && ditherBitDepth != 24)
    {
        error = "native_dither_invalid_bit_depth";
        return false;
    }
    processingConfig.ditherMode = parseNativeDitherMode(ditherMode);
    processingConfig.ditherBitDepth = ditherBitDepth;

    processingConfig.sdm.sourceSampleRate = options.sampleRate;
    processingConfig.sdm.targetSampleRate = options.sampleRate;
    std::string sdmProfile = "safe";
    std::string sdmExtraLoop = "off";
    std::string sdmPreModulatorProfile = "standard";
    if (config.contains("sdm") && config["sdm"].is_object())
    {
        const auto& sdm = config["sdm"];
        sdmProfile = getJsonString(sdm, "qualityProfile", "safe");
        sdmExtraLoop = getJsonString(sdm, "extraLoop", "off");
        sdmPreModulatorProfile = getJsonString(sdm, "preModulatorProfile", "standard");
        processingConfig.sdm.ultimateEnabled =
            sdm.contains("ultimateEnabled") && sdm["ultimateEnabled"].is_boolean()
            && sdm["ultimateEnabled"].get<bool>();
        processingConfig.sdm.sampleRateFamily = getJsonString(sdm, "sampleRateFamily", "44.1k");
        processingConfig.sdm.rationalConversion =
            sdm.contains("rationalConversion") && sdm["rationalConversion"].is_boolean()
            && sdm["rationalConversion"].get<bool>();
        processingConfig.sdm.heavyPreMod =
            sdm.contains("heavyPreMod") && sdm["heavyPreMod"].is_boolean()
            && sdm["heavyPreMod"].get<bool>();
        if (!parseComputeBackend(sdm, "native_sdm", processingConfig.sdm.requestedBackend))
            return false;
        processingConfig.sdm.sourceSampleRate = static_cast<int>(getJsonDouble(sdm, "sourceSampleRate", options.sampleRate));
        processingConfig.sdm.targetSampleRate = static_cast<int>(getJsonDouble(sdm, "targetSampleRate", options.sampleRate));
        const echo_audio_host::Json* sdmFir = nullptr;
        if (sdm.contains("oversampling") && sdm["oversampling"].is_object())
            sdmFir = &sdm["oversampling"];
        else if (sdm.contains("stages") && sdm["stages"].is_array())
            // Current control-plane schema keeps SDM FIR fields directly under
            // sdm. Keep the nested form accepted for forward compatibility.
            sdmFir = &sdm;
        if (sdmFir != nullptr)
        {
            const int requiredSdmTargetSampleRate =
                nativeFormat == NativePlaybackPipeline::OutputFormat::NativeDsd
                    ? options.sampleRate / 16
                    : options.sampleRate;
            if (!parseFir(
                    *sdmFir,
                    "native_sdm_oversampling",
                    processingConfig.sdm.oversampling,
                    requiredSdmTargetSampleRate,
                    32))
                return false;
            processingConfig.sdm.sourceSampleRate = processingConfig.sdm.oversampling.sourceSampleRate;
            processingConfig.sdm.targetSampleRate = processingConfig.sdm.oversampling.targetSampleRate;
        }
    }
    if (sdmProfile != "safe" && sdmProfile != "hifi" && sdmProfile != "reference" && sdmProfile != "insane"
        && sdmProfile != "adaptive" && sdmProfile != "adaptive-light" && sdmProfile != "adaptive-super"
        && sdmProfile != "reference-ec")
    {
        error = "native_sdm_invalid_quality_profile";
        return false;
    }
    if (sdmExtraLoop != "off" && sdmExtraLoop != "extra" && sdmExtraLoop != "horizon")
    {
        error = "native_sdm_invalid_extra_loop";
        return false;
    }
    if (sdmPreModulatorProfile != "standard" && sdmPreModulatorProfile != "heavy"
        && sdmPreModulatorProfile != "ultimate-reference"
        && sdmPreModulatorProfile != "ultimate-transient")
    {
        error = "native_sdm_invalid_pre_modulator_profile";
        return false;
    }

    processingConfig.sdm.qualityProfile = parseNativeSdmProfile(sdmProfile);
    processingConfig.sdm.decisionMode = sdmExtraLoop == "horizon"
        ? echo::SdmDecisionMode::Horizon
        : sdmExtraLoop == "extra"
            ? echo::SdmDecisionMode::Extra
            : echo::SdmDecisionMode::Greedy;
    processingConfig.sdm.preModulatorProfile =
        sdmPreModulatorProfile == "ultimate-reference"
        ? echo::SdmPreModulatorProfile::UltimateReference
        : sdmPreModulatorProfile == "ultimate-transient"
            ? echo::SdmPreModulatorProfile::UltimateTransient
            : sdmPreModulatorProfile == "heavy"
                ? echo::SdmPreModulatorProfile::Heavy
                : echo::SdmPreModulatorProfile::Standard;
    if (!pipeline.configure(processingConfig, error))
        return false;

    options.dopOutput = nativeFormat != NativePlaybackPipeline::OutputFormat::Pcm;
    options.asioNativeDsdOutput = nativeFormat == NativePlaybackPipeline::OutputFormat::NativeDsd;
    options.mqaPcmPassthroughRequested = processingConfig.mqaPassthrough.requested
        && nativeFormat == NativePlaybackPipeline::OutputFormat::Pcm;
    return true;
}

bool hasSameRuntimeOutputOptions(const Options& left, const Options& right)
{
    return left.asio == right.asio
        && left.kernelStreaming == right.kernelStreaming
        && left.exclusive == right.exclusive
        && left.sampleRate == right.sampleRate
        && left.channels == right.channels
        && left.deviceIndex == right.deviceIndex
        && left.bufferSize == right.bufferSize
        && left.asioOutputChannelStart == right.asioOutputChannelStart
        && left.dopOutput == right.dopOutput
        && left.asioNativeDsdOutput == right.asioNativeDsdOutput
        && left.mqaPcmPassthroughRequested == right.mqaPcmPassthroughRequested
        && left.deviceName == right.deviceName
        && left.deviceId == right.deviceId
        && left.sharedBackend == right.sharedBackend;
}

bool hasSameRuntimeBufferCacheScope(const Options& left, const Options& right)
{
    return left.asio == right.asio
        && left.kernelStreaming == right.kernelStreaming
        && left.exclusive == right.exclusive
        && left.channels == right.channels
        && left.deviceIndex == right.deviceIndex
        && left.bufferSize == right.bufferSize
        && left.asioOutputChannelStart == right.asioOutputChannelStart
        && left.dopOutput == right.dopOutput
        && left.asioNativeDsdOutput == right.asioNativeDsdOutput
        && left.deviceName == right.deviceName
        && left.deviceId == right.deviceId
        && left.sharedBackend == right.sharedBackend;
}

uint64_t fingerprintNativeProcessing(const echo_audio_host::Json& params)
{
    const std::string serialized = params.contains("processing")
        ? params["processing"].dump()
        : "{}";
    uint64_t fingerprint = 1469598103934665603ull;
    for (const unsigned char byte : serialized)
    {
        fingerprint ^= static_cast<uint64_t>(byte);
        fingerprint *= 1099511628211ull;
    }
    return fingerprint;
}

echo_audio_host::Json buildDeviceReadyJson(
    const NativePcmOutputReadyInfo& ready,
    int requestedSampleRate,
    int requestedChannels,
    int requestedBufferFrames,
    int openedBufferFrames,
    int fifoCapacityFrames,
    int startupPrebufferFrames,
    int startupPrebufferTimeoutMs,
    const PcmRingAudioSource& source)
{
    const int sampleRate = ready.sampleRate > 0 ? ready.sampleRate : requestedSampleRate;
    const int channels = ready.channels > 0 ? ready.channels : requestedChannels;
    const int deviceBufferFrames = ready.deviceBufferFrames > 0 ? ready.deviceBufferFrames : requestedBufferFrames;
    return {
        {"ready", true},
        {"readyLevel", "device"},
        {"protocolVersion", 1},
        {"backendContractVersion", 2},
        {"capabilities", {
            {"deviceReadyV2", true},
            {"runtimeDeviceConfigureV1", true},
            {"hostOwnedLocalPlaybackV1", true},
            {"nativeDspV1", true},
            {"proDspGateV1", true},
            {"nativeCudaDspV1", CudaFirProcessor::builtWithCuda()},
            {"wasapiExclusive", hasWasapiExclusiveOutput},
            {"asio", hasAsioOutput},
            {"kernelStreaming", hasKernelStreamingOutput},
            {"coreAudioExclusive", hasCoreAudioExclusiveOutput},
        }},
        {"sampleRate", sampleRate},
        {"hardwareSampleRate", ready.hardwareSampleRate > 0 ? ready.hardwareSampleRate : sampleRate},
        {"sharedDeviceSampleRate", sampleRate},
        {"sharedSampleRate", sampleRate},
        {"channels", channels},
        {"exclusive", ready.exclusive},
        {"eqControlPort", 0},
        {"deviceBufferFrames", deviceBufferFrames},
        {"nativeActualBufferFrames", deviceBufferFrames},
        {"actualBufferFrames", deviceBufferFrames},
        {"requestedDeviceBufferFrames", requestedBufferFrames},
        {"openedDeviceBufferFrames", openedBufferFrames},
        {"bufferSizeFallback", openedBufferFrames != requestedBufferFrames},
        {"fifoCapacityFrames", fifoCapacityFrames},
        {"startupPrebufferFrames", startupPrebufferFrames},
        {"startupPrebufferTimeoutMs", startupPrebufferTimeoutMs},
        {"dspActive", source.isDspActive()},
        {"dspClippingRisk", source.hasDspClippingRisk()},
        {"dspLimiterProtecting", source.isDspLimiterProtecting()},
        {"backend", ready.backend},
        {"backendImpl", ready.backendImpl},
        {"format", ready.format},
        {"deviceType", ready.deviceType},
        {"deviceName", ready.deviceName},
    };
}

void cleanupMiniaudioSharedAndAck(PcmRingAudioSource& source, MiniaudioSharedOutput& output, EqControlServer& eqControlServer, bool& shutdownAckSent)
{
    cleanupHostAndAck(
        [&] { cleanupPcmSource(source); },
        [&]
        {
            try { stopMiniaudioSharedOutput(output); }
            catch (const std::exception& error) { logLine(std::string("miniaudio cleanup failed: ") + error.what()); }
            catch (...) { logLine("miniaudio cleanup failed"); }
        },
        [&] { cleanupEqControlServer(eqControlServer); },
        shutdownAckSent);
}

int runLibavDecodePcm(const Options& options)
{
    if (options.decodeFile.empty())
        throw std::runtime_error("libav decode failed: missing input file");

#ifdef _WIN32
    _setmode(_fileno(stdout), _O_BINARY);
#endif

    echo::LibavPcmStreamDecoder decoder;
    decoder.open(options.decodeFile);

    const int sourceSampleRate = decoder.sampleRate();
    if (sourceSampleRate <= 0)
        throw std::runtime_error("libav decode failed: source sample rate unavailable");

    if (sourceSampleRate != options.sampleRate)
        throw std::runtime_error("libav decode resampling unsupported: source=" + std::to_string(sourceSampleRate) + " requested=" + std::to_string(options.sampleRate));

    const int sourceChannels = decoder.channels();
    if (sourceChannels <= 0 || sourceChannels > maxNativePcmChannels)
        throw std::runtime_error("libav decode unsupported channel count: " + std::to_string(sourceChannels));

    if (sourceChannels != options.channels)
        throw std::runtime_error("libav decode channel remap unsupported: source=" + std::to_string(sourceChannels) + " requested=" + std::to_string(options.channels));

    if (options.decodeStartSeconds > 0.0)
        decoder.seek(options.decodeStartSeconds);

    constexpr int blockFrames = 4096;
    while (! decoder.eof())
    {
        auto chunk = decoder.readFrames(blockFrames);
        if (chunk.frames <= 0)
            break;
        const auto bytes = static_cast<std::streamsize>(chunk.frames * sourceChannels * static_cast<int>(sizeof(float)));
        std::cout.write(reinterpret_cast<const char*>(chunk.samples.data()), bytes);
        if (! std::cout.good())
            throw std::runtime_error("libav decode failed while writing PCM");
    }

    return 0;
}

int readJsonRpcFd(int fd, char* buffer, size_t bytes)
{
#ifdef _WIN32
    const intptr_t osHandle = _get_osfhandle(fd);
    if (osHandle == -1)
    {
        errno = EBADF;
        return -1;
    }

    DWORD available = 0;
    if (! PeekNamedPipe(reinterpret_cast<HANDLE>(osHandle), nullptr, 0, nullptr, &available, nullptr))
    {
        const auto error = GetLastError();
        if (error == ERROR_BROKEN_PIPE || error == ERROR_HANDLE_EOF)
            return 0;

        errno = EAGAIN;
        return -1;
    }

    if (available == 0)
    {
        errno = EAGAIN;
        return -1;
    }

    const auto bytesToRead = static_cast<unsigned int>(std::min<size_t>(bytes, available));
    return _read(fd, buffer, bytesToRead);
#else
    const auto result = read(fd, buffer, bytes);
    if (result > static_cast<ssize_t>(std::numeric_limits<int>::max()))
        return std::numeric_limits<int>::max();
    return static_cast<int>(result);
#endif
}

#ifndef _WIN32
bool configureDaemonInputFds(int rpcInputFd, int listenTogetherInputFd)
{
    const auto setNonBlocking = [](int fd) -> bool
    {
        const int flags = fcntl(fd, F_GETFL);
        return flags >= 0 && fcntl(fd, F_SETFL, flags | O_NONBLOCK) == 0;
    };

    if (rpcInputFd < 0 || ! setNonBlocking(rpcInputFd))
        return false;
    return listenTogetherInputFd < 0 || setNonBlocking(listenTogetherInputFd);
}
#endif

void writeJsonRpcFd(RpcOutputWriter& writer, const char* data, size_t bytes)
{
    writer.enqueue(std::string(data, bytes));
}

bool writeListenTogetherPacketFd(int fd, const std::vector<std::uint8_t>& packet)
{
    if (fd < 0 || packet.empty()
        || packet.size() > echo::listen_together::kMaxEncodedAudioPacketBytes)
        return false;

    std::vector<std::uint8_t> framed(packet.size() + 4);
    const auto size = static_cast<std::uint32_t>(packet.size());
    framed[0] = static_cast<std::uint8_t>(size & 0xffu);
    framed[1] = static_cast<std::uint8_t>((size >> 8u) & 0xffu);
    framed[2] = static_cast<std::uint8_t>((size >> 16u) & 0xffu);
    framed[3] = static_cast<std::uint8_t>((size >> 24u) & 0xffu);
    const auto destination = framed.begin() + 4;
    std::copy(packet.begin(), packet.end(), destination);
#ifdef _WIN32
    return _write(fd, framed.data(), static_cast<unsigned int>(framed.size()))
        == static_cast<int>(framed.size());
#else
    return write(fd, framed.data(), framed.size()) == static_cast<ssize_t>(framed.size());
#endif
}

class ListenTogetherPacketPipeReader final
{
public:
    void poll(int fd, echo::listen_together::ListenTogetherGuestAudioStream& guest)
    {
        if (fd < 0)
            return;
        std::array<char, 4096> chunk{};
        for (int reads = 0; reads < 8; ++reads)
        {
            const int bytes = readJsonRpcFd(fd, chunk.data(), chunk.size());
            if (bytes <= 0)
                break;
            pending_.insert(pending_.end(), chunk.begin(), chunk.begin() + bytes);
        }

        while (pending_.size() >= 4)
        {
            const auto size = static_cast<std::size_t>(static_cast<std::uint8_t>(pending_[0]))
                | static_cast<std::size_t>(static_cast<std::uint8_t>(pending_[1])) << 8u
                | static_cast<std::size_t>(static_cast<std::uint8_t>(pending_[2])) << 16u
                | static_cast<std::size_t>(static_cast<std::uint8_t>(pending_[3])) << 24u;
            if (size == 0 || size > echo::listen_together::kMaxEncodedAudioPacketBytes)
            {
                pending_.clear();
                logLine("listen-together input pipe rejected an invalid frame length");
                return;
            }
            if (pending_.size() < size + 4)
                return;
            guest.pushPacket(
                reinterpret_cast<const std::uint8_t*>(pending_.data() + 4),
                size);
            pending_.erase(
                pending_.begin(),
                pending_.begin() + static_cast<std::ptrdiff_t>(size + 4));
        }
    }

    void reset() { pending_.clear(); }

private:
    std::vector<char> pending_;
};

std::string formatMiniaudioDeviceJson(const ma_device_info& info, ma_uint32 index)
{
    std::string json = "{";
    json += "\"id\":\"" + getMiniaudioStableId(static_cast<int>(index)) + "\"";
    json += ",\"name\":\"" + jsonEscape(info.name) + "\"";

    std::string sampleRates = "[";
    {
        std::set<int> rates;
        for (ma_uint32 i = 0; i < info.nativeDataFormatCount; ++i)
            if (info.nativeDataFormats[i].sampleRate > 0)
                rates.insert(static_cast<int>(info.nativeDataFormats[i].sampleRate));
        bool first = true;
        for (int sr : rates)
        {
            if (!first) sampleRates += ",";
            sampleRates += std::to_string(sr);
            first = false;
        }
    }
    sampleRates += "]";
    json += ",\"sampleRates\":" + sampleRates;

    std::string channels = "[";
    {
        std::set<int> chs;
        for (ma_uint32 i = 0; i < info.nativeDataFormatCount; ++i)
            if (info.nativeDataFormats[i].channels > 0)
                chs.insert(static_cast<int>(info.nativeDataFormats[i].channels));
        bool first = true;
        for (int ch : chs)
        {
            if (!first) channels += ",";
            channels += std::to_string(ch);
            first = false;
        }
    }
    channels += "]";
    json += ",\"channels\":" + channels;
    json += ",\"modes\":[\"shared\"]";
    json += ",\"isDefault\":" + std::string(info.isDefault ? "true" : "false");
    json += "}";
    return json;
}

void runJsonRpcOnStdio(
    int stdinFd,
    std::shared_ptr<RpcOutputWriter> rpcOutputWriter,
    echo::EqProcessor& eq,
    echo::ChannelBalanceProcessor& cb,
    echo::ConvolutionProcessor& conv,
    echo::DspHeadroomProcessor& headroom,
    echo::ReplayGainProcessor& rg,
    echo::CompressorProcessor& compressor,
    echo::SpatialDspProcessor& spatialDsp,
    echo::PlaybackRateProcessor& rate,
    echo::LevelMeterProcessor& meter,
    echo::DspRackOrder& rackOrder,
    echo::WorkshopAudioEffectController& workshopAudioEffect,
    std::atomic<bool>& running,
    std::atomic<bool>* shutdownRequested,
    PcmRingAudioSource* audioSource = nullptr,
    double audioSampleRate = 0.0,
    int audioChannels = 0,
    std::function<bool(int sampleRate, int channels, int bufferFrames, echo_audio_host::Json& ready, std::string& error)>* deferredDeviceInit = nullptr,
    std::atomic<bool>* deviceOpened = nullptr,
    RawPcmInputReader* pcmInputReader = nullptr,
    std::function<bool(const echo_audio_host::Json& params, echo_audio_host::Json& result, std::string& error)>* runtimeDeviceConfigure = nullptr,
    std::function<void()>* runtimeDeviceClose = nullptr,
    echo_audio_host::Json* lastDeviceReady = nullptr,
    NativePlaybackPipeline* playbackPipeline = nullptr,
    RuntimeOutputTransitionCoordinator* outputTransitionCoordinator = nullptr,
    echo::listen_together::ListenTogetherHostAudioStream* listenTogetherHost = nullptr,
    echo::listen_together::ListenTogetherGuestAudioStream* listenTogetherGuest = nullptr,
    int listenTogetherInputFd = -1,
    std::atomic<std::uint64_t>* listenTogetherGuestGeneration = nullptr,
    std::atomic<bool>* strictAsioPcmOutput = nullptr,
    bool proDspAllowed = false)
{
    RpcOutputWriter& stdoutFd = *rpcOutputWriter;
#ifndef _WIN32
    // Both inputs share this pump. Leaving the Listen Together pipe blocking
    // stalls every JSON-RPC command while no guest audio packets are arriving.
    if (! configureDaemonInputFds(stdinFd, listenTogetherInputFd))
    {
        logLine("daemon input pipe non-blocking setup failed");
        if (shutdownRequested != nullptr)
            shutdownRequested->store(true, std::memory_order_release);
        running.store(false, std::memory_order_release);
        return;
    }
#endif

    std::string pending;
    std::vector<char> pendingAutomixPcm;
    uint64_t activeAutomixRevision = 0;
    uint64_t lastAutomixRevision = 0;
    uint64_t sessionPcmStartBytes = 0;
    uint64_t sessionPcmDiscardThroughBytes = 0;
    uint64_t pendingResumeGateSessionId = 0;
    bool pendingResumeGateSoftTimeoutReported = false;
    char buf[4096];
    ListenTogetherPacketPipeReader listenTogetherPacketReader;
    const auto stopListenTogetherGuestSession = [&]() {
        if (listenTogetherGuest == nullptr)
            return false;
        const auto guestGeneration = listenTogetherGuestGeneration != nullptr
            ? listenTogetherGuestGeneration->exchange(0, std::memory_order_acq_rel)
            : 0;
        if (playbackPipeline != nullptr && guestGeneration != 0)
            playbackPipeline->requestStopForGeneration(guestGeneration);
        listenTogetherGuest->stop();
        listenTogetherPacketReader.reset();
        return true;
    };

    while (running.load(std::memory_order_acquire))
    {
        if (audioSource != nullptr && pendingResumeGateSessionId != 0)
        {
            const auto gate = audioSource->pollInitialPlayGate(
                pendingResumeGateSessionId,
                pendingResumeGateSessionId);
            if (gate.state == PcmRingAudioSource::InitialPlayGateState::Released
                || gate.state == PcmRingAudioSource::InitialPlayGateState::Cancelled)
            {
                pendingResumeGateSessionId = 0;
                pendingResumeGateSoftTimeoutReported = false;
            }
            else if (gate.state == PcmRingAudioSource::InitialPlayGateState::Failed)
            {
                const auto failedSessionId = pendingResumeGateSessionId;
                pendingResumeGateSessionId = 0;
                pendingResumeGateSoftTimeoutReported = false;
                const auto notification = echo::JsonRpcProtocol::createJsonRpcNotification(
                    "audio.error",
                    {
                        {"sessionId", failedSessionId},
                        {"operationId", failedSessionId},
                        {"message", "pcm_initial_gate_input_ended_without_pcm"},
                    }) + "\n";
                writeJsonRpcFd(stdoutFd, notification.data(), notification.size());
            }
            else if (gate.state == PcmRingAudioSource::InitialPlayGateState::SoftTimeout
                && !pendingResumeGateSoftTimeoutReported)
            {
                pendingResumeGateSoftTimeoutReported = true;
                logLine("audio.resume initial gate delayed: session="
                    + std::to_string(pendingResumeGateSessionId)
                    + " ready=" + std::to_string(gate.readyFrames)
                    + " required=" + std::to_string(gate.requiredFrames));
            }
        }
        if (listenTogetherGuest != nullptr)
            listenTogetherPacketReader.poll(listenTogetherInputFd, *listenTogetherGuest);
        if (outputTransitionCoordinator != nullptr)
            outputTransitionCoordinator->pump();
        const int n = readJsonRpcFd(stdinFd, buf, sizeof(buf));
        if (n > 0)
        {
            pending.append(buf, static_cast<size_t>(n));
            size_t nl;
            while ((nl = pending.find('\n')) != std::string::npos)
            {
                std::string line = pending.substr(0, nl);
                pending.erase(0, nl + 1);
                if (!line.empty())
                {
                    echo_audio_host::Json object;
                    try { object = echo_audio_host::parseJson(line); }
                    catch (...) { object = echo_audio_host::Json::object(); }
                    const auto method = getJsonString(object, "method", {});
                    if (audioSource != nullptr)
                    {
                        const echo_audio_host::Json emptyParams = echo_audio_host::Json::object();
                        const auto& params = object.is_object() && object.contains("params") ? object["params"] : emptyParams;
                        const auto* paramsObject = getJsonObjectParams(params);
                        const bool sessionBeginMethod = method == "audio.sessionBegin";
                        const bool inputEndMethod = method == "audio.inputEnd";
                        const bool sessionAbortMethod = method == "audio.sessionAbort";
                        const bool hasSessionId = paramsObject != nullptr
                            && paramsObject->contains("sessionId")
                            && (*paramsObject)["sessionId"].is_number();
                        const bool sessionPauseMethod = method == "audio.pause" && hasSessionId;
                        const bool sessionResumeMethod = method == "audio.resume" && hasSessionId;
                        const bool automixMethod = method == "audio.automixPrepare"
                            || method == "audio.automixNext"
                            || method == "audio.automixNextEnd"
                            || method == "audio.automixCancel";
                        const auto sessionId = (sessionBeginMethod || inputEndMethod || sessionAbortMethod
                            || sessionPauseMethod || sessionResumeMethod || automixMethod)
                            ? getJsonSessionId(paramsObject != nullptr ? *paramsObject : emptyParams)
                            : 0;
                        const bool sessionMatches = automixMethod
                            && sessionId != 0
                            && sessionId == audioSource->session_.generation();
                        if (automixMethod && ! sessionMatches)
                        {
                            logLine(method + " rejected: session=" + std::to_string(sessionId)
                                + " generation=" + std::to_string(audioSource->session_.generation()));
                        }

                        if (handleListenTogetherRpc(method, object, paramsObject, stdoutFd,
                            audioSource, playbackPipeline, listenTogetherHost, listenTogetherGuest,
                            listenTogetherGuestGeneration, stopListenTogetherGuestSession,
                            [&] { listenTogetherPacketReader.reset(); },
                            [&](std::string& error) {
                                if (deferredDeviceInit == nullptr || deviceOpened == nullptr
                                    || deviceOpened->load(std::memory_order_acquire)) return true;
                                echo_audio_host::Json ready;
                                if (!(*deferredDeviceInit)(48000, 2, 512, ready, error)) return false;
                                if (lastDeviceReady != nullptr) *lastDeviceReady = ready;
                                return true;
                            }, lastDeviceReady ? static_cast<int>(getJsonDouble(*lastDeviceReady, "deviceBufferFrames", 0.0)) : 0))
                            continue;

                        if (sessionBeginMethod)
                        {
                            if (listenTogetherGuest != nullptr && listenTogetherGuest->stats().running)
                                stopListenTogetherGuestSession();
                            echo_audio_host::Json readyResult = lastDeviceReady != nullptr
                                ? *lastDeviceReady
                                : echo_audio_host::Json::object();
                            if (deferredDeviceInit != nullptr && deviceOpened != nullptr && !deviceOpened->load(std::memory_order_acquire))
                            {
                                const int sr = paramsObject ? static_cast<int>(getJsonDouble(*paramsObject, "sr", audioSampleRate)) : static_cast<int>(audioSampleRate);
                                const int ch = paramsObject ? static_cast<int>(getJsonDouble(*paramsObject, "ch", static_cast<double>(audioChannels))) : audioChannels;
                                const int buffer = paramsObject ? static_cast<int>(getJsonDouble(*paramsObject, "buffer", 0)) : 0;
                                std::string error;
                                if (!(*deferredDeviceInit)(sr, ch, buffer, readyResult, error))
                                {
                                    const auto rpcId = getJsonRpcIntegerId(object);
                                    writeJsonRpcError(stdoutFd, rpcId, -32000, error);
                                    continue;
                                }
                                if (lastDeviceReady != nullptr)
                                    *lastDeviceReady = readyResult;
                            }

                            bool accepted = false;
                            const auto generation = playbackPipeline != nullptr
                                ? playbackPipeline->generation()
                                : audioSource->session_.generation();
                            // A daemon-side backend object can be recreated while the
                            // native daemon remains resident. In that case the client
                            // cannot safely infer the next native generation. Omitted
                            // sessionId delegates allocation to the authoritative host.
                            const auto assignedSessionId = sessionId != 0
                                ? sessionId
                                : deferredDeviceInit != nullptr ? generation + 1 : 0;
                            if (assignedSessionId == generation + 1)
                            {
                                pendingResumeGateSessionId = 0;
                                pendingResumeGateSoftTimeoutReported = false;
                                pendingAutomixPcm.clear();
                                activeAutomixRevision = 0;
                                lastAutomixRevision = 0;
                                const bool startPaused = paramsObject != nullptr
                                    && paramsObject->contains("startPaused")
                                    && (*paramsObject)["startPaused"].is_boolean()
                                    && (*paramsObject)["startPaused"].get<bool>();
                                if (playbackPipeline != nullptr) playbackPipeline->beginSession(startPaused);
                                else audioSource->beginSession(startPaused);
                                const bool initialPlayGateRequested = paramsObject != nullptr
                                    && paramsObject->contains("initialPlayGate")
                                    && (*paramsObject)["initialPlayGate"].is_boolean()
                                    && (*paramsObject)["initialPlayGate"].get<bool>();
                                const bool pcmSession = playbackPipeline != nullptr
                                    && playbackPipeline->outputFormat() == NativePlaybackPipeline::OutputFormat::Pcm;
                                if (initialPlayGateRequested && startPaused && pcmSession)
                                    audioSource->armInitialPlayGate(assignedSessionId);
                                sessionPcmStartBytes = pcmInputReader != nullptr
                                    ? std::max(pcmInputReader->bytesConsumed(), sessionPcmDiscardThroughBytes)
                                    : 0;
                                accepted = true;
                            }
                            else if (assignedSessionId == generation)
                            {
                                pendingAutomixPcm.clear();
                                accepted = true;
                            }
                            if (! accepted)
                            {
                                logLine("audio.sessionBegin rejected: requestedSessionId="
                                    + std::to_string(sessionId) + " assignedSessionId="
                                    + std::to_string(assignedSessionId) + " generation="
                                    + std::to_string(generation));
                            }
                            if (deferredDeviceInit != nullptr)
                            {
                                echo_audio_host::Json result = {
                                    {"accepted", accepted},
                                    {"sessionId", assignedSessionId},
                                    {"ready", readyResult},
                                };
                                writeJsonRpcResult(stdoutFd, getJsonRpcIntegerId(object), result);
                            }
                            else
                            {
                                writeJsonRpcBooleanResult(stdoutFd, getJsonRpcIntegerId(object), accepted);
                            }
                            continue;
                        }

                        if (inputEndMethod)
                        {
                            bool accepted = sessionId != 0 && sessionId == (playbackPipeline != nullptr
                                ? playbackPipeline->generation() : audioSource->session_.generation());
                            if (accepted && pcmInputReader != nullptr && paramsObject != nullptr)
                            {
                                const double requestedBytes = getJsonDouble(*paramsObject, "pcmBytes", -1.0);
                                if (requestedBytes >= 0.0)
                                {
                                    const auto pcmBytes = static_cast<uint64_t>(requestedBytes);
                                    accepted = pcmInputReader->waitUntilBytesConsumed(
                                        sessionPcmStartBytes + pcmBytes,
                                        std::chrono::seconds(5));
                                    if (! accepted)
                                        logLine("audio.inputEnd timed out waiting for PCM bytes: expected="
                                            + std::to_string(pcmBytes) + " consumed="
                                            + std::to_string(pcmInputReader->bytesConsumed() - sessionPcmStartBytes));
                                }
                            }
                            if (accepted)
                                if (playbackPipeline != nullptr) playbackPipeline->markInputEnded();
                                else audioSource->markInputEnded();
                            else
                                logLine("audio.inputEnd rejected: session=" + std::to_string(sessionId)
                                    + " generation=" + std::to_string(audioSource->session_.generation()));
                            writeJsonRpcBooleanResult(stdoutFd, getJsonRpcIntegerId(object), accepted);
                            continue;
                        }

                        if (sessionAbortMethod)
                        {
                            bool accepted = sessionId != 0 && sessionId == (playbackPipeline != nullptr
                                ? playbackPipeline->generation() : audioSource->session_.generation());
                            if (accepted && pcmInputReader != nullptr && paramsObject != nullptr)
                            {
                                const double requestedBytes = getJsonDouble(*paramsObject, "pcmBytes", -1.0);
                                if (requestedBytes >= 0.0)
                                {
                                    const auto pcmBytes = static_cast<uint64_t>(requestedBytes);
                                    sessionPcmDiscardThroughBytes = std::max(
                                        sessionPcmDiscardThroughBytes,
                                        sessionPcmStartBytes + pcmBytes);
                                    pcmInputReader->discardThrough(sessionPcmDiscardThroughBytes);
                                }
                            }
                            if (accepted)
                            {
                                if (pendingResumeGateSessionId == sessionId)
                                {
                                    audioSource->cancelInitialPlayGate(
                                        sessionId,
                                        PcmRingAudioSource::InitialPlayGateReleaseReason::StopRequested,
                                        sessionId);
                                    pendingResumeGateSessionId = 0;
                                    pendingResumeGateSoftTimeoutReported = false;
                                }
                                pendingAutomixPcm.clear();
                                if (playbackPipeline != nullptr) playbackPipeline->requestStop();
                                else audioSource->requestStop();
                                if (pcmInputReader != nullptr && sessionPcmDiscardThroughBytes > 0)
                                {
                                    accepted = pcmInputReader->waitUntilBytesConsumed(
                                        sessionPcmDiscardThroughBytes,
                                        std::chrono::milliseconds(1000));
                                    if (! accepted)
                                    {
                                        logLine("audio.sessionAbort timed out establishing PCM discard barrier: target="
                                            + std::to_string(sessionPcmDiscardThroughBytes) + " consumed="
                                            + std::to_string(pcmInputReader->bytesConsumed()));
                                    }
                                }
                            }
                            else
                            {
                                logLine("audio.sessionAbort rejected: session=" + std::to_string(sessionId)
                                    + " generation=" + std::to_string(audioSource->session_.generation()));
                            }
                            writeJsonRpcBooleanResult(stdoutFd, getJsonRpcIntegerId(object), accepted);
                            continue;
                        }

                        if (sessionPauseMethod || sessionResumeMethod)
                        {
                            const auto activeGeneration = playbackPipeline != nullptr
                                ? playbackPipeline->generation() : audioSource->session_.generation();
                            bool accepted = sessionId != 0 && sessionId == activeGeneration;
                            if (accepted && sessionPauseMethod)
                            {
                                if (pendingResumeGateSessionId == sessionId)
                                {
                                    audioSource->cancelInitialPlayGate(
                                        sessionId,
                                        PcmRingAudioSource::InitialPlayGateReleaseReason::ExplicitPause,
                                        sessionId);
                                    pendingResumeGateSessionId = 0;
                                    pendingResumeGateSoftTimeoutReported = false;
                                }
                                if (playbackPipeline != nullptr) playbackPipeline->setPaused(true);
                                else audioSource->setPaused(true);
                            }
                            else if (accepted)
                            {
                                const auto ready = lastDeviceReady != nullptr && lastDeviceReady->is_object()
                                    ? *lastDeviceReady
                                    : echo_audio_host::Json::object();
                                const bool asioPcmSession = playbackPipeline != nullptr
                                    && playbackPipeline->outputFormat() == NativePlaybackPipeline::OutputFormat::Pcm
                                    && ((strictAsioPcmOutput != nullptr
                                            && strictAsioPcmOutput->load(std::memory_order_acquire))
                                        || ready.value("backend", "") == "asio");
                                if (asioPcmSession)
                                {
                                    if (!audioSource->isInitialPlayGateArmed(sessionId))
                                        audioSource->armInitialPlayGate(sessionId);
                                    if (audioSource->isInitialPlayGateArmed(sessionId))
                                    {
                                        accepted = audioSource->beginInitialPlayGateWait(sessionId, sessionId);
                                        if (accepted)
                                        {
                                            pendingResumeGateSessionId = sessionId;
                                            pendingResumeGateSoftTimeoutReported = false;
                                        }
                                    }
                                    else
                                    {
                                        const auto gate = audioSource->getInitialPlayGateSnapshot();
                                        accepted = gate.generation == sessionId
                                            && gate.operationId == sessionId
                                            && gate.state == PcmRingAudioSource::InitialPlayGateState::Released;
                                        if (accepted)
                                            playbackPipeline->setPaused(false);
                                    }
                                }
                                else if (playbackPipeline != nullptr) playbackPipeline->setPaused(false);
                                else audioSource->setPaused(false);
                            }
                            else if (sessionId == 0 || sessionId != activeGeneration)
                                logLine(method + " rejected: session=" + std::to_string(sessionId)
                                    + " generation=" + std::to_string(activeGeneration));
                            writeJsonRpcBooleanResult(stdoutFd, getJsonRpcIntegerId(object), accepted);
                            continue;
                        }

                        if (method == "audio.automixPrepare" && paramsObject != nullptr)
                        {
                            if (sessionMatches)
                            {
                                const uint64_t automixRevision = getJsonRevision(*paramsObject, "automixRevision");
                                if (automixRevision != 0 && automixRevision <= lastAutomixRevision)
                                {
                                    logLine("audio.automixPrepare rejected stale revision="
                                        + std::to_string(automixRevision) + " active="
                                        + std::to_string(activeAutomixRevision));
                                    continue;
                                }
                                pendingAutomixPcm.clear();
                                audioSource->prepareAutomix(
                                    getJsonDouble(*paramsObject, "sampleRate", audioSampleRate),
                                    getJsonDouble(*paramsObject, "fadeStartSeconds", 0.0),
                                    getJsonDouble(*paramsObject, "overlapSeconds", 0.001),
                                    getJsonDouble(*paramsObject, "currentGainDb", 0.0),
                                    getJsonDouble(*paramsObject, "nextGainDb", 0.0));
                                activeAutomixRevision = automixRevision;
                                if (automixRevision != 0)
                                    lastAutomixRevision = automixRevision;
                            }
                            continue;
                        }

                        if (method == "audio.automixNext" && paramsObject != nullptr)
                        {
                            if (sessionMatches)
                            {
                                const uint64_t automixRevision = getJsonRevision(*paramsObject, "automixRevision");
                                if (automixRevision != activeAutomixRevision)
                                {
                                    logLine("audio.automixNext rejected stale revision="
                                        + std::to_string(automixRevision) + " active="
                                        + std::to_string(activeAutomixRevision));
                                    continue;
                                }
                                std::vector<char> payload;
                                if (decodeBase64Payload(getJsonString(*paramsObject, "pcmBase64", {}), payload))
                                    pushAutomixNextPcmPayload(*audioSource, audioChannels, pendingAutomixPcm, payload);
                            }
                            continue;
                        }

                        if (method == "audio.automixNextEnd")
                        {
                            if (sessionMatches)
                            {
                                const uint64_t automixRevision = paramsObject != nullptr
                                    ? getJsonRevision(*paramsObject, "automixRevision")
                                    : 0;
                                if (automixRevision != activeAutomixRevision)
                                {
                                    logLine("audio.automixNextEnd rejected stale revision="
                                        + std::to_string(automixRevision) + " active="
                                        + std::to_string(activeAutomixRevision));
                                    continue;
                                }
                                pendingAutomixPcm.clear();
                                audioSource->markAutomixNextEnded();
                            }
                            continue;
                        }

                        if (method == "audio.automixCancel")
                        {
                            if (sessionMatches)
                            {
                                const uint64_t automixRevision = paramsObject != nullptr
                                    ? getJsonRevision(*paramsObject, "automixRevision")
                                    : 0;
                                if (automixRevision != activeAutomixRevision)
                                {
                                    logLine("audio.automixCancel rejected stale revision="
                                        + std::to_string(automixRevision) + " active="
                                        + std::to_string(activeAutomixRevision));
                                    continue;
                                }
                                pendingAutomixPcm.clear();
                                audioSource->cancelAutomixWithFade();
                                activeAutomixRevision = 0;
                            }
                            continue;
                        }

                        if (method == "device.enumerate")
                        {
                            MiniaudioContextScope scope;
                            std::string contextError;
                            if (!initMiniaudioOutputContext(scope, "auto", contextError))
                            {
                                const auto rpcId = getJsonRpcIntegerId(object);
                                if (rpcId >= 0)
                                {
                                    const std::string errResp = std::string("{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32603,\"message\":\"")
                                        + jsonEscape(contextError) + "\"},\"id\":" + std::to_string(rpcId) + "}\n";
                                    writeJsonRpcFd(stdoutFd, errResp.data(), errResp.size());
                                }
                                continue;
                            }

                            ma_device_info* playbackDevices = nullptr;
                            ma_uint32 playbackDeviceCount = 0;
                            const ma_result enumResult = ma_context_get_devices(
                                &scope.context, &playbackDevices, &playbackDeviceCount, nullptr, nullptr);
                            if (enumResult != MA_SUCCESS)
                            {
                                const auto rpcId = getJsonRpcIntegerId(object);
                                if (rpcId >= 0)
                                {
                                    const std::string errResp = std::string("{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32603,\"message\":\"")
                                        + jsonEscape(std::string("device enumeration failed: ") + ma_result_description(enumResult))
                                        + "\"},\"id\":" + std::to_string(rpcId) + "}\n";
                                    writeJsonRpcFd(stdoutFd, errResp.data(), errResp.size());
                                }
                                continue;
                            }

                            std::string resultJson = "[";
                            for (ma_uint32 i = 0; i < playbackDeviceCount; ++i)
                            {
                                if (i > 0) resultJson += ",";
                                resultJson += formatMiniaudioDeviceJson(playbackDevices[i], i);
                            }
                            resultJson += "]";

                            const auto rpcId = getJsonRpcIntegerId(object);
                            if (rpcId >= 0)
                            {
                                const std::string resp = std::string("{\"jsonrpc\":\"2.0\",\"result\":")
                                    + resultJson + ",\"id\":" + std::to_string(rpcId) + "}\n";
                                writeJsonRpcFd(stdoutFd, resp.data(), resp.size());
                            }
                            continue;
                        }

                        if (method == "device.configure")
                        {
                            const auto rpcId = getJsonRpcIntegerId(object);
                            if (runtimeDeviceConfigure == nullptr || paramsObject == nullptr)
                            {
                                writeJsonRpcError(stdoutFd, rpcId, -32602, "device.configure requires daemon runtime params");
                                continue;
                            }
                            if (! proDspAllowed && requestsProNativeProcessing(*paramsObject))
                            {
                                writeJsonRpcError(stdoutFd, rpcId, -32003, "ECHO Pro audio entitlement required");
                                continue;
                            }
                            echo_audio_host::Json result = echo_audio_host::Json::object();
                            std::string error;
                            if (!(*runtimeDeviceConfigure)(*paramsObject, result, error))
                                writeJsonRpcError(stdoutFd, rpcId, -32001, error);
                            else
                                writeJsonRpcResult(stdoutFd, rpcId, result);
                            continue;
                        }
                    }

                    echo::EqPresetStore presets;
                    std::string response = echo::JsonRpcProtocol::handleJsonLine(
                        line, eq, cb, conv, headroom, rg, compressor, spatialDsp, rate, meter, rackOrder,
                        workshopAudioEffect, presets, proDspAllowed) + "\n";
                    writeJsonRpcFd(stdoutFd, response.data(), response.size());
                    if (method == "rpc.shutdown")
                    {
                        if (shutdownRequested != nullptr)
                            shutdownRequested->store(true, std::memory_order_release);
                        running.store(false, std::memory_order_release);
                        if (runtimeDeviceClose != nullptr)
                        {
                            try { (*runtimeDeviceClose)(); }
                            catch (const std::exception& error) { logLine(std::string("runtime device cleanup failed: ") + error.what()); }
                            catch (...) { logLine("runtime device cleanup failed"); }
                        }
                        if (outputTransitionCoordinator != nullptr)
                            outputTransitionCoordinator->close();
                        return;
                    }
                }
            }
        }
        else if (n == 0)
        {
            break;
        }
        else if (errno == EAGAIN || errno == EWOULDBLOCK)
        {
            std::this_thread::sleep_for(std::chrono::milliseconds(10));
        }
        else if (errno == EINTR)
        {
            continue;
        }
        else
        {
            break;
        }
    }
    if (outputTransitionCoordinator != nullptr)
        outputTransitionCoordinator->close();
}

int runNativePcmHost(const Options& options)
{
    Options runtimeOptions = options;
    const int requestedDeviceBufferFrames = getDeviceBufferSize(options);

    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    echo::ConvolutionProcessor convolutionProcessor;
    echo::DspHeadroomProcessor headroomProcessor;
    echo::ReplayGainProcessor replayGainProcessor;
    echo::CompressorProcessor compressorProcessor;
    echo::SpatialDspProcessor spatialDspProcessor;
    echo::PlaybackRateProcessor rateProcessor;
    echo::LevelMeterProcessor meterProcessor;
    echo::DspRackOrder dspRackOrder;
    echo::WorkshopAudioEffectController workshopAudioEffectController;
    int fifoCapacityFrames = options.deviceOpenDeferred
        ? kDeferredDaemonRingPlaceholderFrames
        : getFifoCapacityFrames(options, options.sampleRate);
    const int startupPrebufferFrames = options.deviceOpenDeferred
        ? framesForMilliseconds(48000, 60)
        : getStartupPrebufferFrames(options, options.sampleRate);
    constexpr int maximumDaemonNativeDsdBitRate = 24'576'000;
    int nativeDsdFifoCapacityByteFrames = options.deviceOpenDeferred
        ? kDeferredDaemonRingPlaceholderFrames
        : options.asioNativeDsdOutput
            ? getNativeDsdFifoCapacityByteFrames(options, options.sampleRate)
            : fifoCapacityFrames * 2;
    const int nativeDsdStartupPrebufferByteFrames = options.deviceOpenDeferred
        ? nativeDsdByteFramesForBitFrames(framesForMilliseconds(maximumDaemonNativeDsdBitRate, 20))
        : options.asioNativeDsdOutput
            ? getNativeDsdStartupPrebufferByteFrames(options, options.sampleRate)
            : startupPrebufferFrames * 2;
    const int startupPrebufferTimeoutMs = options.deviceOpenDeferred
        ? 500
        : getStartupPrebufferTimeoutMs(options);

    PcmRingAudioSource source(options.channels, fifoCapacityFrames, startupPrebufferFrames, startupPrebufferTimeoutMs,
        options.volume, eqProcessor, convolutionProcessor, channelBalanceProcessor, headroomProcessor,
        replayGainProcessor, compressorProcessor, spatialDspProcessor, rateProcessor, meterProcessor, &dspRackOrder,
        &workshopAudioEffectController);
    DopRingSource dopSource(options.channels, fifoCapacityFrames, startupPrebufferFrames, startupPrebufferTimeoutMs);
    NativeDsdRingSource nativeDsdSource(options.channels, nativeDsdFifoCapacityByteFrames,
        nativeDsdStartupPrebufferByteFrames, startupPrebufferTimeoutMs);
    NativePlaybackPipeline playbackPipeline(
        source, dopSource, nativeDsdSource, options.channels,
        eqProcessor, convolutionProcessor, channelBalanceProcessor, headroomProcessor,
        replayGainProcessor, compressorProcessor, spatialDspProcessor, rateProcessor, meterProcessor, &dspRackOrder,
        &workshopAudioEffectController);
    if (options.dopOutput)
    {
        std::string passthroughError;
        const auto passthroughFormat = options.asioNativeDsdOutput
            ? NativePlaybackPipeline::OutputFormat::NativeDsd
            : NativePlaybackPipeline::OutputFormat::Dop;
        if (! playbackPipeline.configurePassthroughOutput(passthroughFormat, passthroughError))
            throw std::runtime_error("native passthrough configuration failed: " + passthroughError);
    }

    auto outputBackend = createNativePcmOutputBackend(runtimeOptions);
    std::string outputError;

    int actualSampleRate;
    int openedDeviceBufferFrames;
    int actualDeviceBufferFrames;

    std::atomic<bool> deviceOpened{true};
    std::atomic<bool> strictAsioPcmOutput{
        runtimeOptions.asio && ! runtimeOptions.dopOutput && ! runtimeOptions.asioNativeDsdOutput};
    std::mutex deviceInitMutex;
    std::function<bool(int sampleRate, int channels, int bufferFrames, bool publishReady, echo_audio_host::Json& readyResult, std::string& error)> openRuntimeDeviceLocked;
    std::function<bool(int sampleRate, int channels, int bufferFrames, echo_audio_host::Json& readyResult, std::string& error)> deferredDeviceInit;
    std::function<bool(const echo_audio_host::Json& params, echo_audio_host::Json& result, std::string& error)> runtimeDeviceConfigure;
    std::function<void()> closeRuntimeDeviceLocked;
    std::function<void()> runtimeDeviceClose;
    uint64_t runtimeProcessingFingerprint = 0;
    bool runtimeProcessingConfigured = false;
    std::unordered_map<int, int> successfulAsioBufferFramesBySampleRate;
    std::atomic<bool> shutdownRequested { false };
    RuntimeOutputTransitionCoordinator outputTransitionCoordinator;
    outputTransitionCoordinator.setShutdownSignal(&shutdownRequested);
    outputTransitionCoordinator.setExecutionWatchdog(std::chrono::seconds(30), []
    {
        std::_Exit(EXIT_FAILURE);
    });
    echo_audio_host::Json lastDeviceReady = echo_audio_host::Json::object();

    if (!options.deviceOpenDeferred)
    {
        if (! openNativePcmOutputWithFallback(*outputBackend, playbackPipeline, options, options.sampleRate, options.channels,
                requestedDeviceBufferFrames, openedDeviceBufferFrames, outputError))
            throw std::runtime_error("native PCM output open failed: " + outputError);

        const auto& ready = outputBackend->readyInfo();
        actualSampleRate = ready.sampleRate > 0 ? ready.sampleRate : options.sampleRate;
        if (ready.openedDeviceBufferFrames > 0)
            openedDeviceBufferFrames = ready.openedDeviceBufferFrames;
        actualDeviceBufferFrames = ready.deviceBufferFrames > 0
            ? ready.deviceBufferFrames
            : requestedDeviceBufferFrames;
    }
    else
    {
        deviceOpened.store(false, std::memory_order_release);

        actualSampleRate = 48000;
        openedDeviceBufferFrames = requestedDeviceBufferFrames;
        actualDeviceBufferFrames = requestedDeviceBufferFrames;

        openRuntimeDeviceLocked = [&](int sampleRate, int channels, int bufferFrames, bool publishReady, echo_audio_host::Json& readyResult, std::string& error) -> bool
        {
            if (deviceOpened.load(std::memory_order_acquire))
            {
                readyResult = lastDeviceReady;
                return true;
            }

            applyDeferredDaemonRingCapacities(
                source,
                dopSource,
                nativeDsdSource,
                sampleRate,
                runtimeOptions.dopOutput,
                runtimeOptions.asioNativeDsdOutput,
                fifoCapacityFrames,
                nativeDsdFifoCapacityByteFrames);

            const int effectiveBuffer = bufferFrames > 0 ? bufferFrames : requestedDeviceBufferFrames;
            runtimeOptions.sampleRate = sampleRate;
            runtimeOptions.channels = channels;
            runtimeOptions.bufferSize = effectiveBuffer;
            outputBackend = createNativePcmOutputBackend(runtimeOptions);
            int openedBuffer = effectiveBuffer;
            int preferredBuffer = 0;
            if (runtimeOptions.asio && ! runtimeOptions.dopOutput && ! runtimeOptions.asioNativeDsdOutput)
            {
                const auto cached = successfulAsioBufferFramesBySampleRate.find(sampleRate);
                if (cached != successfulAsioBufferFramesBySampleRate.end())
                {
                    preferredBuffer = cached->second;
                    logLine("ASIO runtime buffer reuse: sampleRate=" + std::to_string(sampleRate)
                        + " frames=" + std::to_string(preferredBuffer));
                }
            }
            if (! openNativePcmOutputWithFallback(*outputBackend, playbackPipeline, runtimeOptions, sampleRate, channels,
                    effectiveBuffer, openedBuffer, error, preferredBuffer))
                return false;

            if (runtimeOptions.asio && ! runtimeOptions.dopOutput && ! runtimeOptions.asioNativeDsdOutput)
            {
                successfulAsioBufferFramesBySampleRate[sampleRate] = openedBuffer;
                if (preferredBuffer != openedBuffer)
                {
                    logLine("ASIO runtime buffer learned: sampleRate=" + std::to_string(sampleRate)
                        + " requestedFrames=" + std::to_string(effectiveBuffer)
                        + " openedFrames=" + std::to_string(openedBuffer));
                }
            }

            const auto& ready = outputBackend->readyInfo();
            readyResult = buildDeviceReadyJson(
                ready,
                sampleRate,
                channels,
                effectiveBuffer,
                openedBuffer,
                fifoCapacityFrames,
                startupPrebufferFrames,
                startupPrebufferTimeoutMs,
                source);
            deviceOpened.store(true, std::memory_order_release);
            if (publishReady)
            {
                lastDeviceReady = readyResult;
                writeJsonLine(readyResult.dump());
            }
            return true;
        };

        deferredDeviceInit = [&](int sampleRate, int channels, int bufferFrames, echo_audio_host::Json& readyResult, std::string& error) -> bool
        {
            std::lock_guard<std::mutex> lock(deviceInitMutex);
            return openRuntimeDeviceLocked(sampleRate, channels, bufferFrames, true, readyResult, error);
        };

        closeRuntimeDeviceLocked = [&]
        {
            if (! deviceOpened.load(std::memory_order_acquire))
                return;

            playbackPipeline.requestStop();
            playbackPipeline.markInputEnded();
            outputBackend->close();
            deviceOpened.store(false, std::memory_order_release);
            lastDeviceReady = echo_audio_host::Json::object();
        };
        runtimeDeviceClose = [&]
        {
            std::lock_guard<std::mutex> lock(deviceInitMutex);
            closeRuntimeDeviceLocked();
        };
        runtimeDeviceConfigure = [&](const echo_audio_host::Json& params, echo_audio_host::Json& result, std::string& error) -> bool
        {
            std::lock_guard<std::mutex> lock(deviceInitMutex);
            Options next;
            if (! configureRuntimeOutputOptions(params, runtimeOptions, next, error))
                return false;
            const uint64_t nextProcessingFingerprint = fingerprintNativeProcessing(params);
            const bool processingChanged = ! runtimeProcessingConfigured
                || runtimeProcessingFingerprint != nextProcessingFingerprint;
            const bool outputOptionsChanged = ! hasSameRuntimeOutputOptions(runtimeOptions, next);
            const bool bufferCacheScopeChanged = ! hasSameRuntimeBufferCacheScope(runtimeOptions, next);
            const bool changed = processingChanged || outputOptionsChanged;
            if (changed && deviceOpened.load(std::memory_order_acquire))
                closeRuntimeDeviceLocked();
            if (bufferCacheScopeChanged)
                successfulAsioBufferFramesBySampleRate.clear();
            if (processingChanged && ! configureNativeProcessing(params, next, playbackPipeline, error))
                return false;
            runtimeOptions = std::move(next);
            strictAsioPcmOutput.store(
                runtimeOptions.asio && ! runtimeOptions.dopOutput && ! runtimeOptions.asioNativeDsdOutput,
                std::memory_order_release);
            runtimeProcessingFingerprint = nextProcessingFingerprint;
            runtimeProcessingConfigured = true;
            result = {
                {"accepted", true},
                {"changed", changed},
                {"deviceOpened", deviceOpened.load(std::memory_order_acquire)},
                {"outputMode", runtimeOptions.kernelStreaming ? "ks" : runtimeOptions.asio ? "asio" : runtimeOptions.exclusive ? "exclusive" : "shared"},
                {"deviceId", runtimeOptions.deviceId},
                {"deviceIndex", runtimeOptions.deviceIndex},
                {"deviceName", runtimeOptions.deviceName},
                {"sampleRate", runtimeOptions.sampleRate},
                {"channels", runtimeOptions.channels},
                {"bufferSize", runtimeOptions.bufferSize},
                {"sharedBackend", runtimeOptions.sharedBackend},
                {"processing", buildNativeProcessingTelemetry(playbackPipeline)},
            };
            return true;
        };

        outputTransitionCoordinator.setHandler([&](int targetSampleRate) -> RuntimeOutputTransitionResult
        {
            const auto transitionStartedAt = std::chrono::steady_clock::now();
            auto finish = [&](RuntimeOutputTransitionResult transition)
            {
                transition.durationMs = std::chrono::duration<double, std::milli>(
                    std::chrono::steady_clock::now() - transitionStartedAt).count();
                return transition;
            };

            std::lock_guard<std::mutex> lock(deviceInitMutex);
            if (targetSampleRate <= 0)
                return finish({false, 0, {}, "invalid_target_sample_rate"});
            if (! runtimeOptions.asio || runtimeOptions.dopOutput || runtimeOptions.asioNativeDsdOutput)
                return finish({false, 0, {}, "asio_pcm_transition_not_available"});
            if (! deviceOpened.load(std::memory_order_acquire))
                return finish({false, 0, {}, "asio_pcm_transition_device_not_open"});
            if (playbackPipeline.decoderSampleRate(targetSampleRate) != targetSampleRate)
                return finish({false, 0, {}, "asio_pcm_transition_requires_resampling"});

            auto actualOpenSampleRate = [&]() -> int
            {
                const auto& ready = outputBackend->readyInfo();
                return ready.hardwareSampleRate > 0
                    ? ready.hardwareSampleRate
                    : ready.sampleRate > 0 ? ready.sampleRate : 0;
            };
            auto publishStableReady = [&](const echo_audio_host::Json& readyResult)
            {
                lastDeviceReady = readyResult;
                writeJsonLine(readyResult.dump());
            };

            const int previousActualSampleRate = actualOpenSampleRate();
            if (previousActualSampleRate == targetSampleRate)
                return finish({true, previousActualSampleRate, "resident"});

            const Options previousOptions = runtimeOptions;
            const int rollbackSampleRate = previousActualSampleRate > 0
                ? previousActualSampleRate
                : previousOptions.sampleRate;
            auto rollbackPreviousDevice = [&](std::string& rollbackError) -> int
            {
                runtimeOptions = previousOptions;
                runtimeOptions.sampleRate = rollbackSampleRate;
                echo_audio_host::Json rollbackReady;
                if (! openRuntimeDeviceLocked(
                        rollbackSampleRate,
                        runtimeOptions.channels,
                        runtimeOptions.bufferSize,
                        false,
                        rollbackReady,
                        rollbackError))
                    return 0;
                const int rollbackActualRate = actualOpenSampleRate();
                if (rollbackActualRate != rollbackSampleRate)
                {
                    closeRuntimeDeviceLocked();
                    rollbackError = "rollback_rate_mismatch:"
                        + std::to_string(rollbackSampleRate) + "->" + std::to_string(rollbackActualRate);
                    return 0;
                }
                publishStableReady(rollbackReady);
                return rollbackActualRate;
            };

            const auto closeStartedAt = std::chrono::steady_clock::now();
            closeRuntimeDeviceLocked();
            const double closeDurationMs = std::chrono::duration<double, std::milli>(
                std::chrono::steady_clock::now() - closeStartedAt).count();
            runtimeOptions.sampleRate = targetSampleRate;

            echo_audio_host::Json readyResult;
            std::string transitionError;
            const auto openStartedAt = std::chrono::steady_clock::now();
            if (! openRuntimeDeviceLocked(
                    targetSampleRate,
                    runtimeOptions.channels,
                    runtimeOptions.bufferSize,
                    false,
                    readyResult,
                    transitionError))
            {
                std::string rollbackError;
                const int rollbackActualRate = rollbackPreviousDevice(rollbackError);
                return finish({
                    false,
                    rollbackActualRate,
                    rollbackActualRate > 0 ? "rollback" : "failed",
                    "asio_pcm_transition_open_failed:" + transitionError
                        + (rollbackActualRate > 0 ? std::string{} : ";rollback_failed:" + rollbackError),
                });
            }
            const double openDurationMs = std::chrono::duration<double, std::milli>(
                std::chrono::steady_clock::now() - openStartedAt).count();

            const int actualRate = actualOpenSampleRate();
            if (actualRate != targetSampleRate)
            {
                closeRuntimeDeviceLocked();
                std::string rollbackError;
                const int rollbackActualRate = rollbackPreviousDevice(rollbackError);
                return finish({
                    false,
                    rollbackActualRate,
                    rollbackActualRate > 0 ? "rollback" : "failed",
                    "asio_pcm_transition_rate_mismatch:"
                        + std::to_string(targetSampleRate) + "->" + std::to_string(actualRate)
                        + (rollbackActualRate > 0 ? std::string{} : ";rollback_failed:" + rollbackError),
                });
            }

            publishStableReady(readyResult);
            logLine("ASIO PCM sample-rate transition completed: from="
                + std::to_string(previousActualSampleRate)
                + " to=" + std::to_string(actualRate)
                + " closeMs=" + std::to_string(closeDurationMs)
                + " openMs=" + std::to_string(openDurationMs));
            return finish({true, actualRate, "asio-full-reopen"});
        });
    }

    EqControlServer eqControlServer(options.eqControlPort, eqProcessor, channelBalanceProcessor, convolutionProcessor,
        headroomProcessor, replayGainProcessor, rateProcessor, meterProcessor);
    const bool eqControlReady = eqControlServer.start();

    std::unique_ptr<RawPcmInputReader> pcmInputReader;
    if (options.pcmInputFd >= 0)
    {
        const auto onInputError = [](const std::string& error) { logLine(error); };
        if (options.asioNativeDsdOutput)
        {
            pcmInputReader = std::make_unique<RawPcmInputReader>(
                options.pcmInputFd,
                options.channels,
                1,
                [&playbackPipeline](const uint8_t* samples, int byteFrames)
                {
                    return playbackPipeline.nativeDsdSource().push(samples, byteFrames);
                },
                onInputError);
        }
        else if (options.dopOutput)
        {
            pcmInputReader = std::make_unique<RawPcmInputReader>(
                options.pcmInputFd,
                options.channels,
                3,
                [&playbackPipeline, channels = options.channels](const uint8_t* payload, int frames)
                {
                    const size_t sampleCount = static_cast<size_t>(frames) * static_cast<size_t>(channels);
                    std::vector<uint32_t> samples(sampleCount);
                    for (size_t sample = 0; sample < sampleCount; ++sample)
                    {
                        const size_t byteOffset = sample * 3u;
                        samples[sample] =
                            static_cast<uint32_t>(payload[byteOffset])
                            | (static_cast<uint32_t>(payload[byteOffset + 1]) << 8)
                            | (static_cast<uint32_t>(payload[byteOffset + 2]) << 16);
                    }
                    return playbackPipeline.dopSource().push(samples.data(), frames);
                },
                onInputError);
        }
        else
        {
            pcmInputReader = std::make_unique<RawPcmInputReader>(
                options.pcmInputFd,
                options.channels,
                [&playbackPipeline](const float* samples, int frames) { return playbackPipeline.push(samples, frames); },
                onInputError);
        }
        pcmInputReader->start();
    }

    std::atomic<AudioDaemon*> listeningClockOwner { nullptr };
    std::atomic<std::uint64_t> listenTogetherGuestGeneration { 0 };
    std::unique_ptr<echo::listen_together::ListenTogetherHostAudioStream> listenTogetherHost;
    std::unique_ptr<echo::listen_together::ListenTogetherGuestAudioStream> listenTogetherGuest;
    if (options.listenTogetherOutputFd >= 0)
    {
        listenTogetherHost = std::make_unique<echo::listen_together::ListenTogetherHostAudioStream>(
            [&options](std::vector<std::uint8_t> packet) {
                return writeListenTogetherPacketFd(options.listenTogetherOutputFd, packet);
            }, [&listeningClockOwner](std::uint64_t sourceFrame, std::uint64_t outputFrame, int rate) {
                const auto* owner = listeningClockOwner.load(std::memory_order_acquire);
                return owner ? owner->listeningMediaPosition(sourceFrame, outputFrame, rate) : -1.0;
            });
        source.setRealtimeProgramPcmSink(listenTogetherHost.get());
    }
    if (options.listenTogetherInputFd >= 0)
    {
        listenTogetherGuest = std::make_unique<echo::listen_together::ListenTogetherGuestAudioStream>(
            [&playbackPipeline, &listenTogetherGuestGeneration](const float* samples, int frames) {
                const auto generation = listenTogetherGuestGeneration.load(std::memory_order_acquire);
                return generation != 0
                    && generation == playbackPipeline.generation()
                    && playbackPipeline.pushForGeneration(samples, frames, generation);
            });
    }

    std::atomic<bool> jsonRpcRunning{true};
    std::unique_ptr<std::thread> jsonRpcThread;
    auto rpcOutputWriter = std::make_shared<RpcOutputWriter>(options.rpcStdoutFd);
    if (options.rpcStdinFd >= 0 && options.rpcStdoutFd >= 0)
    {
        if (options.deviceOpenDeferred)
        {
            jsonRpcThread = std::make_unique<std::thread>(runJsonRpcOnStdio,
                options.rpcStdinFd, rpcOutputWriter, std::ref(eqProcessor), std::ref(channelBalanceProcessor),
                std::ref(convolutionProcessor), std::ref(headroomProcessor), std::ref(replayGainProcessor), std::ref(compressorProcessor), std::ref(spatialDspProcessor),
                std::ref(rateProcessor), std::ref(meterProcessor), std::ref(dspRackOrder), std::ref(workshopAudioEffectController),
                std::ref(jsonRpcRunning), &shutdownRequested, &source,
                static_cast<double>(actualSampleRate), options.channels,
                &deferredDeviceInit, &deviceOpened, pcmInputReader.get(), &runtimeDeviceConfigure, &runtimeDeviceClose, &lastDeviceReady,
                &playbackPipeline, &outputTransitionCoordinator, listenTogetherHost.get(), listenTogetherGuest.get(),
                options.listenTogetherInputFd, &listenTogetherGuestGeneration, &strictAsioPcmOutput, options.proDspAllowed);
        }
        else
        {
            jsonRpcThread = std::make_unique<std::thread>(runJsonRpcOnStdio,
                options.rpcStdinFd, rpcOutputWriter, std::ref(eqProcessor), std::ref(channelBalanceProcessor),
                std::ref(convolutionProcessor), std::ref(headroomProcessor), std::ref(replayGainProcessor), std::ref(compressorProcessor), std::ref(spatialDspProcessor),
                std::ref(rateProcessor), std::ref(meterProcessor), std::ref(dspRackOrder), std::ref(workshopAudioEffectController),
                std::ref(jsonRpcRunning), &shutdownRequested, &source,
                static_cast<double>(actualSampleRate), options.channels,
                nullptr, nullptr, pcmInputReader.get(), nullptr, nullptr, nullptr, &playbackPipeline, nullptr,
                listenTogetherHost.get(), listenTogetherGuest.get(), options.listenTogetherInputFd,
                &listenTogetherGuestGeneration, &strictAsioPcmOutput, options.proDspAllowed);
        }
    }

    std::unique_ptr<AudioDaemon> audioDaemon;

    AudioDaemon::SourceHooks sourceHooks{
        [&playbackPipeline]() { playbackPipeline.beginSession(); },
        [&playbackPipeline]() { playbackPipeline.continueSessionAfterDrain(); },
        [&playbackPipeline]() { playbackPipeline.markInputEnded(); },
        [&playbackPipeline]() { playbackPipeline.requestStop(); },
        [&playbackPipeline](bool paused) { playbackPipeline.setPaused(paused); },
        [&playbackPipeline](const float* samples, int frames, bool paused) { return playbackPipeline.replaceBufferedAudio(samples, frames, paused); },
        [&playbackPipeline](const float* samples, int frames) { return playbackPipeline.push(samples, frames); },
        [&playbackPipeline](const float* samples, int frames, uint64_t generation) { return playbackPipeline.pushForGeneration(samples, frames, generation); },
        [&playbackPipeline]() { return playbackPipeline.prepareGapless(); },
        [&playbackPipeline](const float* samples, int frames) { return playbackPipeline.pushGaplessNext(samples, frames); },
        [&playbackPipeline]() { playbackPipeline.markGaplessNextEnded(); },
        [&playbackPipeline]() { playbackPipeline.cancelGapless(); },
        [&playbackPipeline]() -> uint64_t { return playbackPipeline.getGaplessBoundaryFrame(); },
        [&playbackPipeline](
            uint64_t fadeStartFrame,
            uint64_t overlapFrames,
            double currentGainDb,
            double nextGainDb,
            double currentReplayGainDb,
            double nextReplayGainDb,
            bool rhythmicBassSwap) {
            return playbackPipeline.prepareAutomixFrames(
                fadeStartFrame,
                overlapFrames,
                currentGainDb,
                nextGainDb,
                currentReplayGainDb,
                nextReplayGainDb,
                rhythmicBassSwap);
        },
        [&playbackPipeline](const float* samples, int frames) {
            return playbackPipeline.pushAutomixNext(samples, frames);
        },
        [&playbackPipeline]() { playbackPipeline.markAutomixNextEnded(); },
        [&playbackPipeline]() { playbackPipeline.cancelAutomixWithFade(); },
        [&playbackPipeline](uint64_t fadeFrames) { playbackPipeline.failAutomixNext(fadeFrames); },
        [&playbackPipeline]() -> uint64_t { return playbackPipeline.getAutomixFadeStartFrame(); },
        [&playbackPipeline]() -> uint64_t { return playbackPipeline.getAutomixFadeEndFrame(); },
        [&playbackPipeline]() -> bool { return playbackPipeline.isAutomixActive(); },
        [&playbackPipeline]() -> uint64_t { return playbackPipeline.generation(); },
        [&playbackPipeline](float volume) { playbackPipeline.setGain(volume); },
        [&playbackPipeline](int outputSampleRate) { return playbackPipeline.decoderSampleRate(outputSampleRate); },
        [&playbackPipeline]() { return buildNativeProcessingTelemetry(playbackPipeline); },
        [&compressorProcessor]() { return echo::JsonRpcProtocol::buildCompressorTelemetry(compressorProcessor); },
        [&playbackPipeline]() { return playbackPipeline.seekPrerollFrames(); },
        [&playbackPipeline](
            const float* samples,
            int frames,
            int prerollFrames,
            bool paused) {
            return playbackPipeline.replaceBufferedAudioWithPreroll(
                samples,
                frames,
                prerollFrames,
                paused);
        },
        [&outputTransitionCoordinator](int targetSampleRate, nlohmann::json& result, std::string& error) {
            const auto transition = outputTransitionCoordinator.request(targetSampleRate);
            result = {
                {"success", transition.success},
                {"actualSampleRate", transition.actualSampleRate},
                {"mode", transition.mode},
                {"durationMs", transition.durationMs},
            };
            error = transition.error;
            return transition.success;
        },
        [&strictAsioPcmOutput]() {
            return strictAsioPcmOutput.load(std::memory_order_acquire);
        },
        [&playbackPipeline]() { playbackPipeline.allowTailDrainAfterStop(); },
        [&playbackPipeline]() { return playbackPipeline.isDrained(); },
        [&playbackPipeline]() {
            return playbackPipeline.outputFormat() == NativePlaybackPipeline::OutputFormat::NativeDsd;
        },
        [&playbackPipeline, &lastDeviceReady](const echo::AudioProbe& probe) {
            MqaPassthroughEndpoint endpoint;
            if (lastDeviceReady.is_object())
            {
                endpoint.backend = lastDeviceReady.value("backend", "");
                endpoint.format = lastDeviceReady.value("format", "");
                endpoint.sampleRate = lastDeviceReady.value("sampleRate", 0);
                endpoint.hardwareSampleRate = lastDeviceReady.value("hardwareSampleRate", endpoint.sampleRate);
            }
            const auto decision = evaluateMqaPassthrough(
                playbackPipeline.processingStatus(),
                probe,
                endpoint);
            playbackPipeline.setMqaPassthroughSourceStatus(
                decision.active,
                decision.reason,
                endpoint.format);
        },
        [&playbackPipeline](
            uint64_t fadeStartFrame,
            uint64_t overlapFrames,
            double currentGainDb,
            double nextGainDb,
            double currentReplayGainDb,
            double nextReplayGainDb,
            bool rhythmicBassSwap,
            bool tailRide,
            bool echoThrow,
            double echoDelaySeconds,
            double echoSend,
            double echoFeedback,
            double vocalExitStartProgress,
            double vocalEntryStartProgress,
            AutomixHandoffSchedule handoffSchedule) {
            return playbackPipeline.prepareAutomixDeckFrames(
                fadeStartFrame,
                overlapFrames,
                currentGainDb,
                nextGainDb,
                currentReplayGainDb,
                nextReplayGainDb,
                rhythmicBassSwap,
                tailRide,
                echoThrow,
                echoDelaySeconds,
                echoSend,
                echoFeedback,
                vocalExitStartProgress,
                vocalEntryStartProgress,
                handoffSchedule);
        },
        [&playbackPipeline](uint64_t deckLease, const float* samples, int frames) {
            return playbackPipeline.pushAutomixDeck(deckLease, samples, frames);
        },
        [&playbackPipeline](uint64_t deckLease) {
            playbackPipeline.markAutomixDeckEnded(deckLease);
        },
        [&playbackPipeline](uint64_t deckLease, uint64_t fadeFrames) {
            playbackPipeline.failAutomixDeck(deckLease, fadeFrames);
        },
        [&playbackPipeline](uint64_t deckLease) {
            return playbackPipeline.commitAutomixDeck(deckLease);
        },
        [&source](std::shared_ptr<const WorkshopTransitionWindow> window) {
            source.installWorkshopTransitionWindow(std::move(window));
        },
        [&source]() { return source.workshopTransitionDeadlineMissed(); },
        [&source, &playbackPipeline](uint64_t operationId) {
            if (playbackPipeline.outputFormat() != NativePlaybackPipeline::OutputFormat::Pcm)
                return false;
            source.armInitialPlayGate(operationId);
            return source.isInitialPlayGateArmed(source.session_.generation());
        },
        [&source](uint64_t generation) { return source.isInitialPlayGateArmed(generation); },
        [&source](uint64_t generation, uint64_t operationId) {
            return source.beginInitialPlayGateWait(generation, operationId);
        },
        [&source](uint64_t generation, uint64_t operationId, int& readyFrames, int& requiredFrames) {
            const auto snapshot = source.pollInitialPlayGate(generation, operationId);
            readyFrames = snapshot.readyFrames;
            requiredFrames = snapshot.requiredFrames;
            switch (snapshot.state)
            {
                case PcmRingAudioSource::InitialPlayGateState::SoftTimeout:
                    return AudioDaemon::InitialPlayGatePollResult::SoftTimeout;
                case PcmRingAudioSource::InitialPlayGateState::Released:
                    return AudioDaemon::InitialPlayGatePollResult::Released;
                case PcmRingAudioSource::InitialPlayGateState::Cancelled:
                    return AudioDaemon::InitialPlayGatePollResult::Cancelled;
                case PcmRingAudioSource::InitialPlayGateState::Failed:
                    return AudioDaemon::InitialPlayGatePollResult::Failed;
                default:
                    return AudioDaemon::InitialPlayGatePollResult::Waiting;
            }
        },
        [&source](uint64_t generation, uint64_t operationId) {
            source.cancelInitialPlayGate(
                generation,
                PcmRingAudioSource::InitialPlayGateReleaseReason::SessionReplaced,
                operationId);
        },
        [&playbackPipeline](uint64_t generation) {
            return playbackPipeline.requestStopForGeneration(generation);
        },
          [&playbackPipeline](int index, uint64_t frame) {
            playbackPipeline.publishGaplessReplayGainBoundary(index, frame);
          },
          [&replayGainProcessor]() { return replayGainProcessor.requiresTrackPreparation(); },
          [&source](std::shared_ptr<DualTrackMixBuffer> mix) { source.installDualTrackMix(std::move(mix)); },
    };
    audioDaemon = std::make_unique<AudioDaemon>(std::move(sourceHooks),
        actualSampleRate, rpcOutputWriter, shutdownRequested,
        AudioDaemon::DecodePath::StreamingLibav, options.channels);
    audioDaemon->initialize();
    listeningClockOwner.store(audioDaemon.get(), std::memory_order_release);
    meterProcessor.setCallback([daemon = audioDaemon.get()](const echo::LevelMeterSnapshot& snapshot) {
        daemon->queueLevelMeter(snapshot);
    });
    logLine("daemon mode: awaiting JSON-RPC commands");

    logLine("ready event writing");
    const std::string nativeCudaDspCapability =
        CudaFirProcessor::builtWithCuda() ? "true" : "false";
    const std::string wasapiExclusiveCapability = hasWasapiExclusiveOutput ? "true" : "false";
    const std::string asioCapability = hasAsioOutput ? "true" : "false";
    const std::string kernelStreamingCapability = hasKernelStreamingOutput ? "true" : "false";
    const std::string coreAudioExclusiveCapability = hasCoreAudioExclusiveOutput ? "true" : "false";
    if (options.deviceOpenDeferred)
    {
        const std::string listenTogetherRealtimeAudioCapability =
            options.listenTogetherOutputFd >= 0 && options.listenTogetherInputFd >= 0 ? "true" : "false";
        writeJsonLine(
            std::string("{\"ready\":true,\"readyLevel\":\"process\",\"protocolVersion\":1,\"backendContractVersion\":2,\"capabilities\":{\"deviceReadyV2\":true,\"runtimeDeviceConfigureV1\":true,\"hostOwnedLocalPlaybackV1\":true,\"nativeDspV1\":true,\"proDspGateV1\":true,\"listenTogetherRealtimeAudioV1\":")
            + listenTogetherRealtimeAudioCapability
            + ",\"nativeCudaDspV1\":"
            + nativeCudaDspCapability
            + ",\"wasapiExclusive\":" + wasapiExclusiveCapability
            + ",\"asio\":" + asioCapability
            + ",\"kernelStreaming\":" + kernelStreamingCapability
            + ",\"coreAudioExclusive\":" + coreAudioExclusiveCapability + "}"
            + (options.channels > 2 ? ",\"channels\":" + std::to_string(options.channels) : "") + "}");
    }
    else
    {
        const auto& ready = outputBackend->readyInfo();
        const std::string listenTogetherRealtimeAudioCapability =
            options.listenTogetherOutputFd >= 0 && options.listenTogetherInputFd >= 0 ? "true" : "false";
        writeJsonLine(
            std::string("{\"ready\":true,\"readyLevel\":\"device\",\"protocolVersion\":1,\"backendContractVersion\":2,\"capabilities\":{\"deviceReadyV2\":true,\"nativeDspV1\":true,\"proDspGateV1\":true,\"listenTogetherRealtimeAudioV1\":")
            + listenTogetherRealtimeAudioCapability
            + ",\"nativeCudaDspV1\":" + nativeCudaDspCapability
            + ",\"wasapiExclusive\":" + wasapiExclusiveCapability
            + ",\"asio\":" + asioCapability
            + ",\"kernelStreaming\":" + kernelStreamingCapability
            + ",\"coreAudioExclusive\":" + coreAudioExclusiveCapability
            + "},\"sampleRate\":" + std::to_string(actualSampleRate)
            + ",\"hardwareSampleRate\":" + std::to_string(ready.hardwareSampleRate > 0 ? ready.hardwareSampleRate : actualSampleRate)
            + ",\"sharedDeviceSampleRate\":" + std::to_string(actualSampleRate)
            + ",\"sharedSampleRate\":" + std::to_string(actualSampleRate)
            + ",\"channels\":" + std::to_string(ready.channels > 0 ? ready.channels : options.channels)
            + ",\"exclusive\":" + std::string(ready.exclusive ? "true" : "false")
            + ",\"eqControlPort\":" + std::to_string(eqControlReady ? options.eqControlPort : 0)
            + ",\"deviceBufferFrames\":" + std::to_string(actualDeviceBufferFrames)
            + ",\"nativeActualBufferFrames\":" + std::to_string(actualDeviceBufferFrames)
            + ",\"actualBufferFrames\":" + std::to_string(actualDeviceBufferFrames)
            + ",\"requestedDeviceBufferFrames\":" + std::to_string(requestedDeviceBufferFrames)
            + ",\"openedDeviceBufferFrames\":" + std::to_string(openedDeviceBufferFrames)
            + ",\"bufferSizeFallback\":" + std::string(openedDeviceBufferFrames != requestedDeviceBufferFrames ? "true" : "false")
            + ",\"fifoCapacityFrames\":" + std::to_string(fifoCapacityFrames)
            + ",\"startupPrebufferFrames\":" + std::to_string(startupPrebufferFrames)
            + ",\"startupPrebufferTimeoutMs\":" + std::to_string(startupPrebufferTimeoutMs)
            + ",\"dspActive\":" + std::string(source.isDspActive() ? "true" : "false")
            + ",\"dspClippingRisk\":" + std::string(source.hasDspClippingRisk() ? "true" : "false")
            + ",\"dspLimiterProtecting\":" + std::string(source.isDspLimiterProtecting() ? "true" : "false")
            + ",\"backend\":\"" + ready.backend + "\",\"backendImpl\":\"" + ready.backendImpl + "\",\"format\":\"" + jsonEscape(ready.format) + "\""
            + ",\"deviceType\":\"" + ready.deviceType + "\",\"deviceName\":\"" + jsonEscape(ready.deviceName) + "\"}");
    }

    uint64_t lastReportedSource = std::numeric_limits<uint64_t>::max();
    uint64_t lastReportedOutput = std::numeric_limits<uint64_t>::max();
    bool endedReported = false;
    bool shutdownAckSent = false;

    while (! shutdownRequested.load(std::memory_order_acquire))
    {
        for (std::size_t notificationIndex = 0;
             notificationIndex < NativePcmOutputNotificationQueue::maximumPendingNotifications;
             ++notificationIndex)
        {
            NativePcmOutputNotification notification;
            bool hasNotification = false;
            {
                std::lock_guard<std::mutex> lock(deviceInitMutex);
                if (deviceOpened.load(std::memory_order_acquire) && outputBackend != nullptr)
                    hasNotification = outputBackend->pollNotification(notification);
            }
            if (! hasNotification)
                break;

            const auto event = buildNativePcmOutputNotificationJson(notification);
            writeJsonLine(event.dump());
            if (options.rpcStdoutFd >= 0)
                audioDaemon->emitDeviceEvent(event);
        }

        audioDaemon->flushLevelMeter();
        const auto sourceFrames = playbackPipeline.getSourceTrackFrames();
        const auto outputFrames = playbackPipeline.getDeviceOutputFrames();
        if (sourceFrames != lastReportedSource || outputFrames != lastReportedOutput)
        {
            // Two position-reporting paths serve different consumers:
            //   stdout (fd 1) → DaemonHostProcess reads this via child.stdout for status tracking
            //   RPC pipe (fd 4 via options.rpcStdoutFd) → JsonRpcBridge receives structured JSON-RPC
            //   position notifications for AudioBackend consumers
            writeJsonLine(std::string("{\"pos\":") + std::to_string(sourceFrames)
                + ",\"sourceTrackFrames\":" + std::to_string(sourceFrames)
                + ",\"deviceOutputFrames\":" + std::to_string(outputFrames)
                + ",\"bufferedFrames\":" + std::to_string(playbackPipeline.getReadyFrames())
                + ",\"underrunCallbacks\":" + std::to_string(playbackPipeline.getUnderrunCallbacks())
                + ",\"underrunFrames\":" + std::to_string(playbackPipeline.getUnderrunFrames())
                + ",\"dspLimiterProtecting\":" + std::string(playbackPipeline.pcmSource().isDspLimiterProtecting() ? "true" : "false")
                + ",\"dspLimiterGainReductionDb\":" + std::to_string(playbackPipeline.pcmSource().getDspLimiterGainReductionDb()) + "}");
            lastReportedSource = sourceFrames;
            lastReportedOutput = outputFrames;

            audioDaemon->emitPosition(
                sourceFrames,
                outputFrames,
                playbackPipeline.getReadyFrames(),
                playbackPipeline.hasInputEnded());
        }

        if (playbackPipeline.isDrained())
        {
            if (! endedReported)
            {
                writeJsonLine("{\"event\":\"ended\"}");
                endedReported = true;
                audioDaemon->emitEnded();
            }
        }
        else
        {
            endedReported = false;
        }

        // The loop above stays at about 33 ms. Level-meter snapshots are published much more often, so the
        // visualizer sees a new spectrum as soon as the meter has one (flush is a cheap no-op when nothing is new).
        const auto sliceStart = std::chrono::steady_clock::now();
        while (! shutdownRequested.load(std::memory_order_acquire)
               && std::chrono::steady_clock::now() - sliceStart < std::chrono::milliseconds(33))
        {
            std::this_thread::sleep_for(std::chrono::milliseconds(4));
            audioDaemon->flushLevelMeter();
        }
    }


    const auto finalSourceFrames = playbackPipeline.getSourceTrackFrames();
    const auto finalOutputFrames = playbackPipeline.getDeviceOutputFrames();
    if (finalSourceFrames != lastReportedSource || finalOutputFrames != lastReportedOutput)
        writeJsonLine(std::string("{\"pos\":") + std::to_string(finalSourceFrames)
            + ",\"sourceTrackFrames\":" + std::to_string(finalSourceFrames)
            + ",\"deviceOutputFrames\":" + std::to_string(finalOutputFrames)
            + ",\"bufferedFrames\":" + std::to_string(playbackPipeline.getReadyFrames())
            + ",\"underrunCallbacks\":" + std::to_string(playbackPipeline.getUnderrunCallbacks())
            + ",\"underrunFrames\":" + std::to_string(playbackPipeline.getUnderrunFrames())
            + ",\"dspLimiterProtecting\":" + std::string(playbackPipeline.pcmSource().isDspLimiterProtecting() ? "true" : "false")
            + ",\"dspLimiterGainReductionDb\":" + std::to_string(playbackPipeline.pcmSource().getDspLimiterGainReductionDb()) + "}");

    if (playbackPipeline.getUnderrunCallbacks() > 0)
        logLine("Output underruns: callbacks=" + std::to_string(playbackPipeline.getUnderrunCallbacks())
            + " frames=" + std::to_string(playbackPipeline.getUnderrunFrames()));

    if (! endedReported)
        writeJsonLine("{\"event\":\"ended\"}");

    if (jsonRpcThread)
    {
        outputTransitionCoordinator.close();
        jsonRpcRunning.store(false);
        if (! outputTransitionCoordinator.waitUntilIdle(std::chrono::seconds(2)))
        {
            logLine("ASIO transition did not return during shutdown; forcing audio-host exit");
            std::_Exit(EXIT_FAILURE);
        }
        jsonRpcThread->join();
    }

    if (pcmInputReader)
    {
        playbackPipeline.requestStop();
        pcmInputReader->stop();
    }

    source.setRealtimeProgramPcmSink(nullptr);
    if (listenTogetherHost)
        listenTogetherHost->stop();
    listenTogetherGuestGeneration.store(0, std::memory_order_release);
    if (listenTogetherGuest)
    {
        playbackPipeline.requestStop();
        listenTogetherGuest->stop();
    }

    cleanupHostAndAck(
        [&] { cleanupPcmSource(source); },
        [&]
        {
            try { outputBackend->close(); }
            catch (const std::exception& error) { logLine(std::string("native PCM output cleanup failed: ") + error.what()); }
            catch (...) { logLine("native PCM output cleanup failed"); }
        },
        [&] { cleanupEqControlServer(eqControlServer); },
        shutdownAckSent);
    // The meter worker and JSON-RPC handlers capture AudioDaemon. Clear the
    // observer first so the worker cannot call into a destroyed daemon, then
    // destroy AudioDaemon while the remaining native services are still alive.
    meterProcessor.setCallback({});
    audioDaemon.reset();
    return 0;
}
int runHost(const Options& options)
{
    configureProcessPriority();
    ScopedTimerResolution timerResolution;
    if (options.asioNativeDsdOutput)
    {
#ifdef _WIN32
        if (! options.asio || ! options.dopOutput)
            throw std::runtime_error("ASIO native DSD output requires ASIO DoP output");
#elif defined(__linux__)
        if (! options.exclusive)
            throw std::runtime_error("native DSD output requires ALSA exclusive");
#else
        throw std::runtime_error("native DSD output is not supported on this platform");
#endif
    }
    if (options.dopOutput && ! options.asio && ! options.exclusive)
        throw std::runtime_error("DoP output requires WASAPI exclusive or ASIO");
#if !defined(_WIN32)
    if (options.kernelStreaming)
        throw std::runtime_error("WDM-KS output is only supported on Windows");
    if (options.asio)
        throw std::runtime_error("ASIO output is only supported on Windows");
#endif
#ifndef _WIN32
    if (options.sharedBackend == "directsound" || options.sharedBackend == "windows")
        throw std::runtime_error("requested Windows shared backend is unavailable on this platform");
#endif
    if (options.deviceOpenDeferred)
    {
        logLine("Backend: deferred runtime selection");
    }
    else
    {
        const auto backendKind = selectNativePcmOutputBackend(options);
        logLine(options.asioNativeDsdOutput && options.exclusive
#if defined(__linux__)
            ? "Backend: ALSA exclusive native DSD"
#else
            ? "Backend: native DSD"
#endif
            : backendKind == NativePcmOutputBackendKind::Miniaudio
            ? options.exclusive
#if defined(__linux__)
                ? "Backend: ALSA exclusive"
#elif defined(__APPLE__)
                ? options.dopOutput ? "Backend: CoreAudio HAL exclusive DoP" : "Backend: CoreAudio Hog Mode exclusive"
#else
                ? "Backend: miniaudio exclusive"
#endif
                : "Backend: miniaudio shared"
            : backendKind == NativePcmOutputBackendKind::PipeWire
                ? "Backend: native PipeWire shared"
            : backendKind == NativePcmOutputBackendKind::WasapiShared
                ? "Backend: native WASAPI shared"
                : backendKind == NativePcmOutputBackendKind::WasapiExclusive
                    ? "Backend: native WASAPI exclusive"
                    : backendKind == NativePcmOutputBackendKind::Asio
                        ? "Backend: native ASIO"
                        : "Backend: native WDM-KS (experimental)");
    }
    return runNativePcmHost(options);
}
} // namespace

#include "AudioDaemon.h"

#ifndef ECHO_AUDIO_HOST_TESTS
int main(int argc, char* argv[])
{
    Options options;

    try
    {
        options = parseOptions(getCommandLineArgs(argc, argv));
        if (options.deviceOpenDeferred && options.channelInitializationDeferred)
        {
            if (options.rpcStdinFd < 0 || options.rpcStdoutFd < 0)
                throw std::runtime_error("native channel initialization requires RPC transport");
            if (!initializeNativeDaemonChannels(options.rpcStdinFd, options.rpcStdoutFd, options.channels))
                return 0;
        }

        if (options.buildCapabilities)
        {
            std::cout << "{\"nativeCudaDspV1\":"
                      << (CudaFirProcessor::builtWithCuda() ? "true" : "false")
                      << "}" << std::endl;
            return 0;
        }

        if (options.list)
        {
            return listDevices(options);
        }

        if (options.asioSdmFamilyProbe)
        {
#ifdef _WIN32
            return probeAsioSdmFamilies(options);
#else
            throw std::runtime_error("ASIO SDM family probe is only supported on Windows");
#endif
        }

        if (options.decodePcm)
        {
            return runLibavDecodePcm(options);
        }

        if (options.cddaToc || options.cddaRead || options.cddaCapabilities || options.cddaEject || options.cddaStatus)
        {
            const WindowsCddaReader cddaReader;
            if (options.cddaEject)
                return cddaReader.eject(options.cddaDrive);
            if (options.cddaToc)
                return cddaReader.writeToc(options.cddaDrive);
            if (options.cddaRead)
                return cddaReader.streamTrack(options.cddaDrive, options.cddaTrack, options.decodeStartSeconds, options.cddaExpectedStartLba, options.cddaExpectedEndLba, options.cddaLastTrack, options.sampleRate);
            if (options.cddaStatus)
                return cddaReader.writeStatus(options.cddaDrive);
            return cddaReader.writeCapabilities();
        }

        return runHost(options);
    }
    catch (const std::exception& error)
    {
        logLine(error.what());
        if (! options.decodePcm && ! options.cddaRead)
            writeErrorEvent(error.what());
        return 1;
    }
}
#endif
