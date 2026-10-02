#pragma once

#if defined(__linux__)

#include "PcmRingAudioSource.h"

#include <cstdint>
#include <memory>
#include <string>
#include <vector>

struct LinuxPipeWireCallbackState;

struct LinuxPipeWireSinkInfo
{
    uint32_t nodeId = 0;
    std::string name;
    std::string description;
    std::string serial;
    int sampleRate = 0;
    int priority = 0;
    bool isDefault = false;
};

bool linuxPipeWireRuntimeAvailable();
int linuxPipeWireDefaultClockRate();
std::vector<LinuxPipeWireSinkInfo> enumerateLinuxPipeWireSinks(std::string* error = nullptr);

class LinuxPipeWireSharedOutput final
{
public:
    struct ReadyInfo
    {
        int sampleRate = 0;
        int hardwareSampleRate = 0;
        int channels = 0;
        int bufferFrameCount = 0;
        std::string format;
        std::string deviceName;
        std::string nodeName;
    };

    LinuxPipeWireSharedOutput();
    ~LinuxPipeWireSharedOutput();

    LinuxPipeWireSharedOutput(const LinuxPipeWireSharedOutput&) = delete;
    LinuxPipeWireSharedOutput& operator=(const LinuxPipeWireSharedOutput&) = delete;

    bool open(
        PcmRingAudioSource& source,
        const char* requestedDevice,
        int deviceIndex,
        const char* deviceId,
        int sampleRate,
        int channels,
        int bufferFrames,
        std::string& error);
    void close() noexcept;
    int takeFailure() noexcept;
    const ReadyInfo& readyInfo() const noexcept { return ready; }

private:
    struct pw_thread_loop* loop = nullptr;
    struct pw_stream* stream = nullptr;
    std::unique_ptr<LinuxPipeWireCallbackState> callbackState;
    PcmRingAudioSource* ringSource = nullptr;
    ReadyInfo ready;
    int channels = 0;
    int configuredRate = 0;
    bool running = false;
};

#endif
