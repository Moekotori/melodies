#pragma once

#if defined(__linux__)

#include "NativeDsdRingSource.h"
#include "LinuxOutputFailure.h"

#include <cstdint>
#include <string>
#include <thread>
#include <vector>

void packAlsaDsdU32Be(
    const std::uint8_t* interleavedByteFrames,
    int channels,
    int byteFrameCount,
    std::uint8_t* packedU32Be);

std::vector<int> probeLinuxAlsaNativeDsdBitRates(const char* alsaId);

class LinuxAlsaNativeDsdOutput final
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
        std::string pcmName;
    };

    LinuxAlsaNativeDsdOutput() = default;
    ~LinuxAlsaNativeDsdOutput();

    LinuxAlsaNativeDsdOutput(const LinuxAlsaNativeDsdOutput&) = delete;
    LinuxAlsaNativeDsdOutput& operator=(const LinuxAlsaNativeDsdOutput&) = delete;

    bool open(
        NativeDsdRingSource& source,
        const char* requestedDevice,
        int deviceIndex,
        const char* deviceId,
        int nativeBitRate,
        int channels,
        int bufferFrames,
        std::string& error);
    void close() noexcept;
    int takeFailure() noexcept { return failure.take(); }
    const ReadyInfo& readyInfo() const noexcept { return ready; }

private:
    void renderLoop();

    NativeDsdRingSource* ringSource = nullptr;
    ReadyInfo ready;
    struct _snd_pcm* pcm = nullptr;
    std::vector<std::uint8_t> byteScratch;
    std::vector<std::uint8_t> packedScratch;
    int channels = 0;
    int periodFrames = 0;
    std::atomic<bool> running { false };
    std::thread renderThread;
    LinuxOutputFailure failure;
};

#endif
