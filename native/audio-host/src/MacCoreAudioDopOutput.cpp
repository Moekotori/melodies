#include "MacCoreAudioDopOutput.h"

#if defined(__APPLE__)

#include "CoreAudioDopPacking.h"
#include "DopRingSource.h"
#include "MacCoreAudioDeviceInfo.h"

#include <CoreFoundation/CoreFoundation.h>

#include <algorithm>
#include <cctype>
#include <chrono>
#include <cmath>
#include <cstring>
#include <limits>
#include <string_view>
#include <thread>

namespace
{
constexpr auto propertySettleTimeout = std::chrono::milliseconds(1200);
constexpr auto propertySettleInterval = std::chrono::milliseconds(10);

std::string coreAudioError(const char* prefix, OSStatus status)
{
    return std::string(prefix) + ":osstatus=" + std::to_string(static_cast<std::int32_t>(status));
}

bool getPropertyData(
    AudioObjectID object,
    const AudioObjectPropertyAddress& address,
    UInt32 size,
    void* value) noexcept
{
    return AudioObjectHasProperty(object, &address)
        && AudioObjectGetPropertyData(object, &address, 0, nullptr, &size, value) == noErr;
}

std::string getCoreAudioString(AudioObjectID object, AudioObjectPropertySelector selector)
{
    CFStringRef value = nullptr;
    UInt32 size = sizeof(value);
    const AudioObjectPropertyAddress address {
        selector,
        kAudioObjectPropertyScopeGlobal,
        kAudioObjectPropertyElementMain,
    };
    if (! getPropertyData(object, address, size, &value) || value == nullptr)
        return {};

    char buffer[1024] {};
    const bool converted = CFStringGetCString(value, buffer, sizeof(buffer), kCFStringEncodingUTF8);
    CFRelease(value);
    return converted ? std::string(buffer) : std::string();
}

std::vector<AudioDeviceID> enumerateOutputDevices()
{
    const AudioObjectPropertyAddress devicesAddress {
        kAudioHardwarePropertyDevices,
        kAudioObjectPropertyScopeGlobal,
        kAudioObjectPropertyElementMain,
    };
    UInt32 size = 0;
    if (AudioObjectGetPropertyDataSize(
            kAudioObjectSystemObject, &devicesAddress, 0, nullptr, &size) != noErr
        || size == 0)
    {
        return {};
    }

    std::vector<AudioDeviceID> devices(size / sizeof(AudioDeviceID));
    if (AudioObjectGetPropertyData(
            kAudioObjectSystemObject, &devicesAddress, 0, nullptr, &size, devices.data()) != noErr)
    {
        return {};
    }

    const AudioObjectPropertyAddress streamsAddress {
        kAudioDevicePropertyStreams,
        kAudioDevicePropertyScopeOutput,
        kAudioObjectPropertyElementMain,
    };
    devices.erase(std::remove_if(devices.begin(), devices.end(), [&] (AudioDeviceID candidate)
    {
        UInt32 streamBytes = 0;
        return AudioObjectGetPropertyDataSize(candidate, &streamsAddress, 0, nullptr, &streamBytes) != noErr
            || streamBytes < sizeof(AudioStreamID);
    }), devices.end());
    return devices;
}

std::string normalized(std::string_view value)
{
    std::string result;
    result.reserve(value.size());
    for (const unsigned char character : std::string(value))
        if (! std::isspace(character))
            result.push_back(static_cast<char>(std::tolower(character)));
    return result;
}

AudioDeviceID selectOutputDevice(const char* requestedDevice, int deviceIndex)
{
    const std::string requested = requestedDevice != nullptr ? requestedDevice : "";
    const auto devices = enumerateOutputDevices();
    if (! requested.empty())
    {
        const auto normalizedRequested = normalized(requested);
        const auto found = std::find_if(devices.begin(), devices.end(), [&] (AudioDeviceID candidate)
        {
            const auto uid = getMacCoreAudioDeviceUid(candidate);
            const auto name = getCoreAudioString(candidate, kAudioObjectPropertyName);
            if (uid == requested || name == requested)
                return true;
            const auto normalizedName = normalized(name);
            return ! normalizedRequested.empty() && ! normalizedName.empty()
                && (normalizedName == normalizedRequested
                    || normalizedName.find(normalizedRequested) != std::string::npos
                    || normalizedRequested.find(normalizedName) != std::string::npos);
        });
        if (found != devices.end())
            return *found;
    }
    if (deviceIndex >= 0 && static_cast<size_t>(deviceIndex) < devices.size())
        return devices[static_cast<size_t>(deviceIndex)];
    return requested.empty() ? getMacCoreAudioDefaultOutputDevice() : kAudioObjectUnknown;
}

std::vector<AudioStreamID> getOutputStreams(AudioDeviceID device)
{
    const AudioObjectPropertyAddress address {
        kAudioDevicePropertyStreams,
        kAudioDevicePropertyScopeOutput,
        kAudioObjectPropertyElementMain,
    };
    UInt32 size = 0;
    if (AudioObjectGetPropertyDataSize(device, &address, 0, nullptr, &size) != noErr || size == 0)
        return {};
    std::vector<AudioStreamID> streams(size / sizeof(AudioStreamID));
    return AudioObjectGetPropertyData(device, &address, 0, nullptr, &size, streams.data()) == noErr
        ? streams
        : std::vector<AudioStreamID> {};
}

bool supportsRate(const AudioValueRange& range, int sampleRate) noexcept
{
    const double requested = static_cast<double>(sampleRate);
    return requested + 0.5 >= range.mMinimum && requested - 0.5 <= range.mMaximum;
}

std::uint32_t bytesPerSample(const AudioStreamBasicDescription& format) noexcept
{
    if (format.mChannelsPerFrame == 0)
        return 0;
    if ((format.mFormatFlags & kAudioFormatFlagIsNonInterleaved) != 0)
        return format.mBytesPerFrame;
    return format.mBytesPerFrame / format.mChannelsPerFrame;
}

bool isDopPcmFormat(const AudioStreamBasicDescription& format, int sampleRate, int channels) noexcept
{
    const auto containerBytes = bytesPerSample(format);
    return format.mFormatID == kAudioFormatLinearPCM
        && (format.mFormatFlags & kAudioFormatFlagIsFloat) == 0
        && (format.mFormatFlags & kAudioFormatFlagIsSignedInteger) != 0
        && format.mChannelsPerFrame == static_cast<UInt32>(channels)
        && format.mBitsPerChannel >= 24
        && format.mBitsPerChannel <= 32
        && (containerBytes == 3 || containerBytes == 4)
        && std::abs(format.mSampleRate - static_cast<double>(sampleRate)) <= 0.5;
}

bool sameDopPacking(
    const AudioStreamBasicDescription& left,
    const AudioStreamBasicDescription& right) noexcept
{
    constexpr AudioFormatFlags layoutFlags =
        kAudioFormatFlagIsBigEndian
        | kAudioFormatFlagIsNonInterleaved
        | kAudioFormatFlagIsAlignedHigh;
    return left.mFormatID == right.mFormatID
        && left.mChannelsPerFrame == right.mChannelsPerFrame
        && left.mBitsPerChannel == right.mBitsPerChannel
        && bytesPerSample(left) == bytesPerSample(right)
        && (left.mFormatFlags & layoutFlags) == (right.mFormatFlags & layoutFlags);
}

bool sameStreamFormat(
    const AudioStreamBasicDescription& left,
    const AudioStreamBasicDescription& right) noexcept
{
    return std::abs(left.mSampleRate - right.mSampleRate) <= 0.5
        && left.mFormatID == right.mFormatID
        && left.mFormatFlags == right.mFormatFlags
        && left.mBytesPerPacket == right.mBytesPerPacket
        && left.mFramesPerPacket == right.mFramesPerPacket
        && left.mBytesPerFrame == right.mBytesPerFrame
        && left.mChannelsPerFrame == right.mChannelsPerFrame
        && left.mBitsPerChannel == right.mBitsPerChannel;
}

void waitForRestoredFormat(
    AudioStreamID stream,
    const AudioObjectPropertyAddress& address,
    const AudioStreamBasicDescription& expected) noexcept
{
    const auto deadline = std::chrono::steady_clock::now() + propertySettleTimeout;
    do
    {
        AudioStreamBasicDescription actual {};
        UInt32 size = sizeof(actual);
        if (getPropertyData(stream, address, size, &actual) && sameStreamFormat(actual, expected))
            return;
        std::this_thread::sleep_for(propertySettleInterval);
    }
    while (std::chrono::steady_clock::now() < deadline);
}

std::vector<AudioStreamBasicDescription> availableDopFormats(
    AudioStreamID stream,
    AudioObjectPropertySelector selector,
    int sampleRate,
    int channels)
{
    const AudioObjectPropertyAddress address {
        selector,
        kAudioObjectPropertyScopeGlobal,
        kAudioObjectPropertyElementMain,
    };
    UInt32 size = 0;
    if (AudioObjectGetPropertyDataSize(stream, &address, 0, nullptr, &size) != noErr || size == 0)
        return {};

    std::vector<AudioStreamRangedDescription> ranged(size / sizeof(AudioStreamRangedDescription));
    if (AudioObjectGetPropertyData(stream, &address, 0, nullptr, &size, ranged.data()) != noErr)
        return {};

    std::vector<AudioStreamBasicDescription> formats;
    for (const auto& description : ranged)
    {
        auto format = description.mFormat;
        format.mSampleRate = static_cast<double>(sampleRate);
        if (supportsRate(description.mSampleRateRange, sampleRate)
            && isDopPcmFormat(format, sampleRate, channels))
        {
            formats.push_back(format);
        }
    }
    std::stable_sort(formats.begin(), formats.end(), [] (const auto& left, const auto& right)
    {
        const auto leftBytes = bytesPerSample(left);
        const auto rightBytes = bytesPerSample(right);
        if (leftBytes != rightBytes)
            return leftBytes > rightBytes;
        return left.mBitsPerChannel > right.mBitsPerChannel;
    });
    formats.erase(std::unique(formats.begin(), formats.end(), [] (const auto& left, const auto& right)
    {
        return sameDopPacking(left, right);
    }), formats.end());
    return formats;
}

bool waitForRate(AudioDeviceID device, int sampleRate) noexcept
{
    const AudioObjectPropertyAddress address {
        kAudioDevicePropertyNominalSampleRate,
        kAudioObjectPropertyScopeGlobal,
        kAudioObjectPropertyElementMain,
    };
    const auto deadline = std::chrono::steady_clock::now() + propertySettleTimeout;
    do
    {
        Float64 actual = 0.0;
        UInt32 size = sizeof(actual);
        if (getPropertyData(device, address, size, &actual)
            && std::abs(actual - static_cast<double>(sampleRate)) <= 0.5)
        {
            return true;
        }
        std::this_thread::sleep_for(propertySettleInterval);
    }
    while (std::chrono::steady_clock::now() < deadline);
    return false;
}

bool currentRateMatches(AudioDeviceID device, int sampleRate) noexcept
{
    const AudioObjectPropertyAddress address {
        kAudioDevicePropertyNominalSampleRate,
        kAudioObjectPropertyScopeGlobal,
        kAudioObjectPropertyElementMain,
    };
    Float64 actual = 0.0;
    UInt32 size = sizeof(actual);
    return getPropertyData(device, address, size, &actual)
        && std::abs(actual - static_cast<double>(sampleRate)) <= 0.5;
}

bool setNominalSampleRate(AudioDeviceID device, int sampleRate, std::string& error)
{
    const AudioObjectPropertyAddress address {
        kAudioDevicePropertyNominalSampleRate,
        kAudioObjectPropertyScopeGlobal,
        kAudioObjectPropertyElementMain,
    };
    if (currentRateMatches(device, sampleRate))
        return true;
    Boolean settable = false;
    if (AudioObjectIsPropertySettable(device, &address, &settable) != noErr || ! settable)
    {
        error = "coreaudio_dop_sample_rate_not_settable";
        return false;
    }
    const Float64 requested = static_cast<double>(sampleRate);
    const OSStatus status = AudioObjectSetPropertyData(
        device, &address, 0, nullptr, sizeof(requested), &requested);
    if (status != noErr)
    {
        error = coreAudioError("coreaudio_dop_sample_rate_set_failed", status);
        return false;
    }
    if (! waitForRate(device, sampleRate))
    {
        error = "coreaudio_dop_sample_rate_mismatch";
        return false;
    }
    return true;
}

bool waitForFormat(
    AudioStreamID stream,
    const AudioObjectPropertyAddress& address,
    const AudioStreamBasicDescription& requested,
    AudioStreamBasicDescription& actual) noexcept
{
    const auto deadline = std::chrono::steady_clock::now() + propertySettleTimeout;
    do
    {
        UInt32 size = sizeof(actual);
        if (getPropertyData(stream, address, size, &actual)
            && isDopPcmFormat(actual, static_cast<int>(std::llround(requested.mSampleRate)),
                static_cast<int>(requested.mChannelsPerFrame))
            && sameDopPacking(actual, requested))
        {
            return true;
        }
        std::this_thread::sleep_for(propertySettleInterval);
    }
    while (std::chrono::steady_clock::now() < deadline);
    return false;
}

bool currentFormatMatches(
    AudioStreamID stream,
    const AudioObjectPropertyAddress& address,
    const AudioStreamBasicDescription& requested,
    AudioStreamBasicDescription& actual) noexcept
{
    UInt32 size = sizeof(actual);
    return getPropertyData(stream, address, size, &actual)
        && isDopPcmFormat(actual, static_cast<int>(std::llround(requested.mSampleRate)),
            static_cast<int>(requested.mChannelsPerFrame))
        && sameDopPacking(actual, requested);
}

bool setStreamFormat(
    AudioStreamID stream,
    AudioObjectPropertySelector selector,
    const AudioStreamBasicDescription& requested,
    AudioStreamBasicDescription& actual,
    std::string& error)
{
    const AudioObjectPropertyAddress address {
        selector,
        kAudioObjectPropertyScopeGlobal,
        kAudioObjectPropertyElementMain,
    };
    if (currentFormatMatches(stream, address, requested, actual))
        return true;
    Boolean settable = false;
    if (AudioObjectIsPropertySettable(stream, &address, &settable) != noErr || ! settable)
    {
        error = selector == kAudioStreamPropertyPhysicalFormat
            ? "coreaudio_dop_physical_format_not_settable"
            : "coreaudio_dop_virtual_format_not_settable";
        return false;
    }
    const OSStatus status = AudioObjectSetPropertyData(
        stream, &address, 0, nullptr, sizeof(requested), &requested);
    if (status != noErr)
    {
        error = coreAudioError(
            selector == kAudioStreamPropertyPhysicalFormat
                ? "coreaudio_dop_physical_format_set_failed"
                : "coreaudio_dop_virtual_format_set_failed",
            status);
        return false;
    }
    if (! waitForFormat(stream, address, requested, actual))
    {
        error = selector == kAudioStreamPropertyPhysicalFormat
            ? "coreaudio_dop_physical_format_mismatch"
            : "coreaudio_dop_virtual_format_mismatch";
        return false;
    }
    return true;
}

bool setBufferFrames(AudioDeviceID device, int requestedFrames, UInt32& actualFrames, std::string& error)
{
    const AudioObjectPropertyAddress address {
        kAudioDevicePropertyBufferFrameSize,
        kAudioObjectPropertyScopeGlobal,
        kAudioObjectPropertyElementMain,
    };
    UInt32 size = sizeof(actualFrames);
    if (! getPropertyData(device, address, size, &actualFrames))
    {
        error = "coreaudio_dop_buffer_size_unavailable";
        return false;
    }
    if (requestedFrames <= 0 || actualFrames == static_cast<UInt32>(requestedFrames))
        return true;

    const AudioObjectPropertyAddress rangeAddress {
        kAudioDevicePropertyBufferFrameSizeRange,
        kAudioObjectPropertyScopeGlobal,
        kAudioObjectPropertyElementMain,
    };
    AudioValueRange range {};
    size = sizeof(range);
    if (! getPropertyData(device, rangeAddress, size, &range))
    {
        error = "coreaudio_dop_buffer_size_range_unavailable";
        return false;
    }
    const UInt32 clamped = static_cast<UInt32>(std::clamp<double>(
        requestedFrames, range.mMinimum, range.mMaximum));
    Boolean settable = false;
    if (AudioObjectIsPropertySettable(device, &address, &settable) != noErr || ! settable)
    {
        error = "coreaudio_dop_buffer_size_not_settable";
        return false;
    }
    const OSStatus status = AudioObjectSetPropertyData(
        device, &address, 0, nullptr, sizeof(clamped), &clamped);
    if (status != noErr)
    {
        error = coreAudioError("coreaudio_dop_buffer_size_set_failed", status);
        return false;
    }
    const auto deadline = std::chrono::steady_clock::now() + propertySettleTimeout;
    do
    {
        size = sizeof(actualFrames);
        if (getPropertyData(device, address, size, &actualFrames) && actualFrames == clamped)
            return true;
        std::this_thread::sleep_for(propertySettleInterval);
    }
    while (std::chrono::steady_clock::now() < deadline);
    if (actualFrames == 0)
    {
        error = "coreaudio_dop_buffer_size_mismatch";
        return false;
    }
    error = "coreaudio_dop_buffer_size_mismatch";
    return false;
}

std::string formatName(const AudioStreamBasicDescription& format)
{
    const auto endian = (format.mFormatFlags & kAudioFormatFlagIsBigEndian) != 0 ? "be" : "le";
    return "s" + std::to_string(format.mBitsPerChannel) + endian;
}
}

MacCoreAudioDopOutput::~MacCoreAudioDopOutput()
{
    close();
}

bool MacCoreAudioDopOutput::open(
    DopRingSource& source,
    const char* requestedDevice,
    int deviceIndex,
    int sampleRate,
    int channels,
    int bufferFrames,
    NotificationCallback notificationCallback,
    void* notificationUserData,
    std::string& error)
{
    close();
    if (sampleRate <= 0 || channels != 2)
    {
        error = "coreaudio_dop_requires_stereo_positive_rate";
        return false;
    }

    device = selectOutputDevice(requestedDevice, deviceIndex);
    if (device == kAudioObjectUnknown)
    {
        error = "coreaudio_dop_device_not_found";
        return false;
    }
    ready.deviceUid = getMacCoreAudioDeviceUid(device);
    ready.deviceName = getCoreAudioString(device, kAudioObjectPropertyName);
    followsDefaultDevice = (requestedDevice == nullptr || requestedDevice[0] == '\0') && deviceIndex < 0;

    const auto streams = getOutputStreams(device);
    if (streams.empty())
    {
        error = "coreaudio_dop_output_stream_unavailable";
        close();
        return false;
    }
    // AudioDeviceIOProc output buffers follow the device stream order. Keep
    // the first implementation deliberately scoped to the primary stream so
    // channel-to-buffer mapping cannot silently target a different endpoint.
    stream = streams.front();
    UInt32 startingChannel = 0;
    const AudioObjectPropertyAddress startingChannelAddress {
        kAudioStreamPropertyStartingChannel,
        kAudioObjectPropertyScopeGlobal,
        kAudioObjectPropertyElementMain,
    };
    UInt32 startingChannelSize = sizeof(startingChannel);
    if (! getPropertyData(stream, startingChannelAddress, startingChannelSize, &startingChannel)
        || startingChannel != 1
        || availableDopFormats(
            stream,
            kAudioStreamPropertyAvailablePhysicalFormats,
            sampleRate,
            channels).empty())
    {
        error = "coreaudio_dop_integer_format_unsupported";
        close();
        return false;
    }

    const AudioObjectPropertyAddress nominalAddress {
        kAudioDevicePropertyNominalSampleRate,
        kAudioObjectPropertyScopeGlobal,
        kAudioObjectPropertyElementMain,
    };
    UInt32 size = sizeof(originalNominalSampleRate);
    nominalSampleRateSaved = getPropertyData(device, nominalAddress, size, &originalNominalSampleRate);
    const AudioObjectPropertyAddress bufferAddress {
        kAudioDevicePropertyBufferFrameSize,
        kAudioObjectPropertyScopeGlobal,
        kAudioObjectPropertyElementMain,
    };
    size = sizeof(originalBufferFrames);
    bufferFramesSaved = getPropertyData(device, bufferAddress, size, &originalBufferFrames);
    const AudioObjectPropertyAddress physicalAddress {
        kAudioStreamPropertyPhysicalFormat,
        kAudioObjectPropertyScopeGlobal,
        kAudioObjectPropertyElementMain,
    };
    size = sizeof(originalPhysicalFormat);
    physicalFormatSaved = getPropertyData(stream, physicalAddress, size, &originalPhysicalFormat);
    const AudioObjectPropertyAddress virtualAddress {
        kAudioStreamPropertyVirtualFormat,
        kAudioObjectPropertyScopeGlobal,
        kAudioObjectPropertyElementMain,
    };
    size = sizeof(originalVirtualFormat);
    virtualFormatSaved = getPropertyData(stream, virtualAddress, size, &originalVirtualFormat);

    if (! hogMode.acquire(ready.deviceUid.empty() ? nullptr : ready.deviceUid.c_str(), error)
        || ! setNominalSampleRate(device, sampleRate, error))
    {
        close();
        return false;
    }

    const auto formats = availableDopFormats(
        stream,
        kAudioStreamPropertyAvailablePhysicalFormats,
        sampleRate,
        channels);
    bool formatConfigured = false;
    for (const auto& format : formats)
    {
        AudioStreamBasicDescription physical {};
        AudioStreamBasicDescription virtualFormat {};
        std::string attemptError;
        if (setStreamFormat(
                stream, kAudioStreamPropertyPhysicalFormat, format, physical, attemptError)
            && setStreamFormat(
                stream, kAudioStreamPropertyVirtualFormat, physical, virtualFormat, attemptError)
            && sameDopPacking(physical, virtualFormat))
        {
            activeFormat = virtualFormat;
            formatConfigured = true;
            break;
        }
        error = attemptError;
    }
    if (! formatConfigured)
    {
        if (error.empty())
            error = "coreaudio_dop_exact_integer_format_unavailable";
        close();
        return false;
    }

    UInt32 actualBufferFrames = 0;
    if (! setBufferFrames(device, bufferFrames, actualBufferFrames, error))
    {
        close();
        return false;
    }
    renderScratch.assign(
        static_cast<size_t>(std::max<UInt32>(actualBufferFrames, 32768u)) * static_cast<size_t>(channels),
        0u);
    ringSource = &source;
    stopping.store(false, std::memory_order_release);

    OSStatus status = AudioDeviceCreateIOProcID(device, audioIoProc, this, &ioProcId);
    if (status != noErr)
    {
        error = coreAudioError("coreaudio_dop_ioproc_create_failed", status);
        close();
        return false;
    }
    status = AudioDeviceStart(device, ioProcId);
    if (status != noErr)
    {
        error = coreAudioError("coreaudio_dop_device_start_failed", status);
        close();
        return false;
    }
    ioStarted = true;

    std::string watcherError;
    if (! deviceWatcher.start(
            ready.deviceUid.empty() ? nullptr : ready.deviceUid.c_str(),
            followsDefaultDevice,
            notificationCallback,
            notificationUserData,
            watcherError))
    {
        error = "coreaudio_dop_device_watcher_failed:" + watcherError;
        close();
        return false;
    }

    ready.sampleRate = sampleRate;
    ready.hardwareSampleRate = getMacCoreAudioNominalSampleRate(device);
    ready.channels = channels;
    ready.bufferFrameCount = static_cast<int>(actualBufferFrames);
    ready.format = formatName(activeFormat);
    error.clear();
    return true;
}

void MacCoreAudioDopOutput::close() noexcept
{
    stopping.store(true, std::memory_order_release);
    deviceWatcher.stop();
    if (device != kAudioObjectUnknown && ioStarted && ioProcId != nullptr)
    {
        AudioDeviceStop(device, ioProcId);
        ioStarted = false;
    }
    if (device != kAudioObjectUnknown && ioProcId != nullptr)
    {
        AudioDeviceDestroyIOProcID(device, ioProcId);
        ioProcId = nullptr;
    }
    ringSource = nullptr;
    renderScratch.clear();
    restoreDeviceState();
    hogMode.release();
    device = kAudioObjectUnknown;
    stream = kAudioObjectUnknown;
    activeFormat = {};
    ready = {};
}

void MacCoreAudioDopOutput::restoreDeviceState() noexcept
{
    if (device != kAudioObjectUnknown && nominalSampleRateSaved)
    {
        const AudioObjectPropertyAddress address {
            kAudioDevicePropertyNominalSampleRate,
            kAudioObjectPropertyScopeGlobal,
            kAudioObjectPropertyElementMain,
        };
        AudioObjectSetPropertyData(
            device, &address, 0, nullptr, sizeof(originalNominalSampleRate), &originalNominalSampleRate);
        if (std::isfinite(originalNominalSampleRate)
            && originalNominalSampleRate > 0.0
            && originalNominalSampleRate <= static_cast<double>(std::numeric_limits<int>::max()))
        {
            waitForRate(device, static_cast<int>(std::llround(originalNominalSampleRate)));
        }
    }
    if (stream != kAudioObjectUnknown && physicalFormatSaved)
    {
        const AudioObjectPropertyAddress address {
            kAudioStreamPropertyPhysicalFormat,
            kAudioObjectPropertyScopeGlobal,
            kAudioObjectPropertyElementMain,
        };
        AudioObjectSetPropertyData(
            stream, &address, 0, nullptr, sizeof(originalPhysicalFormat), &originalPhysicalFormat);
        waitForRestoredFormat(stream, address, originalPhysicalFormat);
    }
    if (stream != kAudioObjectUnknown && virtualFormatSaved)
    {
        const AudioObjectPropertyAddress address {
            kAudioStreamPropertyVirtualFormat,
            kAudioObjectPropertyScopeGlobal,
            kAudioObjectPropertyElementMain,
        };
        AudioObjectSetPropertyData(
            stream, &address, 0, nullptr, sizeof(originalVirtualFormat), &originalVirtualFormat);
        waitForRestoredFormat(stream, address, originalVirtualFormat);
    }
    if (device != kAudioObjectUnknown && bufferFramesSaved)
    {
        const AudioObjectPropertyAddress address {
            kAudioDevicePropertyBufferFrameSize,
            kAudioObjectPropertyScopeGlobal,
            kAudioObjectPropertyElementMain,
        };
        AudioObjectSetPropertyData(
            device, &address, 0, nullptr, sizeof(originalBufferFrames), &originalBufferFrames);
    }
    physicalFormatSaved = false;
    virtualFormatSaved = false;
    nominalSampleRateSaved = false;
    bufferFramesSaved = false;
}

OSStatus MacCoreAudioDopOutput::audioIoProc(
    AudioObjectID,
    const AudioTimeStamp*,
    const AudioBufferList*,
    const AudioTimeStamp*,
    AudioBufferList* outputData,
    const AudioTimeStamp*,
    void* clientData)
{
    auto* output = static_cast<MacCoreAudioDopOutput*>(clientData);
    return output != nullptr ? output->render(outputData) : noErr;
}

OSStatus MacCoreAudioDopOutput::render(AudioBufferList* outputData) noexcept
{
    if (outputData == nullptr)
        return noErr;
    for (UInt32 buffer = 0; buffer < outputData->mNumberBuffers; ++buffer)
    {
        auto& destination = outputData->mBuffers[buffer];
        if (destination.mData != nullptr && destination.mDataByteSize > 0)
            std::memset(destination.mData, 0, destination.mDataByteSize);
    }
    if (stopping.load(std::memory_order_acquire) || ringSource == nullptr)
        return noErr;

    const auto containerBytes = bytesPerSample(activeFormat);
    const bool nonInterleaved = (activeFormat.mFormatFlags & kAudioFormatFlagIsNonInterleaved) != 0;
    const bool bigEndian = (activeFormat.mFormatFlags & kAudioFormatFlagIsBigEndian) != 0;
    const bool alignedHigh = containerBytes == 4
        && ((activeFormat.mFormatFlags & kAudioFormatFlagIsAlignedHigh) != 0
            || activeFormat.mBitsPerChannel == 32);
    if (containerBytes < 3 || activeFormat.mChannelsPerFrame != 2 || outputData->mNumberBuffers == 0)
        return noErr;

    UInt32 frameCount = 0;
    if (nonInterleaved)
    {
        if (outputData->mNumberBuffers < 2
            || outputData->mBuffers[0].mNumberChannels < 1
            || outputData->mBuffers[1].mNumberChannels < 1
            || activeFormat.mBytesPerFrame == 0)
            return noErr;
        frameCount = std::min(
            outputData->mBuffers[0].mDataByteSize,
            outputData->mBuffers[1].mDataByteSize) / activeFormat.mBytesPerFrame;
    }
    else
    {
        if (outputData->mBuffers[0].mNumberChannels < 2 || activeFormat.mBytesPerFrame == 0)
            return noErr;
        frameCount = outputData->mBuffers[0].mDataByteSize / activeFormat.mBytesPerFrame;
    }
    if (frameCount == 0 || static_cast<size_t>(frameCount) * 2u > renderScratch.size())
        return noErr;

    ringSource->renderInterleaved(renderScratch.data(), frameCount, 2);
    for (UInt32 frame = 0; frame < frameCount; ++frame)
    {
        for (UInt32 channel = 0; channel < 2; ++channel)
        {
            std::uint8_t* destination = nullptr;
            if (nonInterleaved)
            {
                auto& buffer = outputData->mBuffers[channel];
                destination = static_cast<std::uint8_t*>(buffer.mData)
                    + static_cast<size_t>(frame) * activeFormat.mBytesPerFrame;
            }
            else
            {
                auto& buffer = outputData->mBuffers[0];
                destination = static_cast<std::uint8_t*>(buffer.mData)
                    + static_cast<size_t>(frame) * activeFormat.mBytesPerFrame
                    + static_cast<size_t>(channel) * containerBytes;
            }
            echo_audio_host::writeCoreAudioDopSample(
                destination,
                renderScratch[static_cast<size_t>(frame) * 2u + channel],
                containerBytes,
                bigEndian,
                alignedHigh);
        }
    }
    return noErr;
}

#endif
