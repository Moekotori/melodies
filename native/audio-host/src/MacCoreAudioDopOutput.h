#pragma once

#if defined(__APPLE__)

#include "MacCoreAudioDeviceWatcher.h"
#include "MacCoreAudioHogMode.h"

#include <CoreAudio/CoreAudio.h>

#include <atomic>
#include <cstdint>
#include <string>
#include <vector>

class DopRingSource;

class MacCoreAudioDopOutput final
{
public:
    using NotificationCallback = void (*)(
        void* userData,
        const char* event,
        const std::string& deviceUid,
        const char* reason,
        std::uint32_t code,
        bool currentDevice,
        bool followsDefaultDevice);

    struct ReadyInfo
    {
        int sampleRate = 0;
        int hardwareSampleRate = 0;
        int channels = 0;
        int bufferFrameCount = 0;
        std::string format;
        std::string deviceName;
        std::string deviceUid;
    };

    MacCoreAudioDopOutput() = default;
    ~MacCoreAudioDopOutput();

    MacCoreAudioDopOutput(const MacCoreAudioDopOutput&) = delete;
    MacCoreAudioDopOutput& operator=(const MacCoreAudioDopOutput&) = delete;

    bool open(
        DopRingSource& source,
        const char* requestedDevice,
        int deviceIndex,
        int sampleRate,
        int channels,
        int bufferFrames,
        NotificationCallback notificationCallback,
        void* notificationUserData,
        std::string& error);
    void close() noexcept;
    const ReadyInfo& readyInfo() const noexcept { return ready; }

private:
    static OSStatus audioIoProc(
        AudioObjectID device,
        const AudioTimeStamp* now,
        const AudioBufferList* inputData,
        const AudioTimeStamp* inputTime,
        AudioBufferList* outputData,
        const AudioTimeStamp* outputTime,
        void* clientData);
    OSStatus render(AudioBufferList* outputData) noexcept;
    void restoreDeviceState() noexcept;

    DopRingSource* ringSource = nullptr;
    AudioDeviceID device = kAudioObjectUnknown;
    AudioStreamID stream = kAudioObjectUnknown;
    AudioDeviceIOProcID ioProcId = nullptr;
    AudioStreamBasicDescription activeFormat {};
    AudioStreamBasicDescription originalPhysicalFormat {};
    AudioStreamBasicDescription originalVirtualFormat {};
    Float64 originalNominalSampleRate = 0.0;
    UInt32 originalBufferFrames = 0;
    bool physicalFormatSaved = false;
    bool virtualFormatSaved = false;
    bool nominalSampleRateSaved = false;
    bool bufferFramesSaved = false;
    bool ioStarted = false;
    bool followsDefaultDevice = false;
    std::atomic<bool> stopping { false };
    std::vector<std::uint32_t> renderScratch;
    MacCoreAudioDeviceWatcher deviceWatcher;
    MacCoreAudioHogMode hogMode;
    ReadyInfo ready;
};

#endif
