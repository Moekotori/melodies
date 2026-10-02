#pragma once

#include "NativePrimitives.h"

#include <atomic>
#include <chrono>
#include <cstdint>
#include <functional>
#include <mutex>
#include <vector>

class NativeDsdRingSource final
{
public:
    NativeDsdRingSource(
        int channelCount,
        int capacityByteFrames,
        int startupPrebufferByteFramesToUse,
        int startupPrebufferTimeoutMsToUse);

    uint32_t renderInterleaved(uint8_t* output, uint32_t byteFrameCount, uint32_t outputChannels);
    bool push(const uint8_t* samples, int byteFrameCount);
    bool pushForGeneration(const uint8_t* samples, int byteFrameCount, uint64_t generation);
    int replaceBufferedAudio(const uint8_t* samples, int byteFrameCount, bool pausedAfterReplace);
    void beginSession();
    void reconfigureCapacity(int capacityByteFrames);
    int getCapacityByteFrames() const;
    void markInputEnded();
    void requestStop();
    void allowTailDrainAfterStop();
    void setPaused(bool paused);
    bool isDrained() const;
    bool hasInputEnded() const;
    int getReadyByteFrames() const;
    uint64_t getReadyFrames() const;
    uint64_t getFramesPlayed() const;
    uint64_t getUnderrunCallbacks() const;
    uint64_t getUnderrunFrames() const;
    uint64_t generation() const noexcept { return sessionGeneration.load(std::memory_order_acquire); }

#if defined(ECHO_AUDIO_ENGINE_TESTS) && ECHO_AUDIO_ENGINE_TESTS
    void setReadAdmissionHookForTests(std::function<void()> hook) { readAdmissionHookForTests = std::move(hook); }
    void setPostFifoReadHookForTests(std::function<void()> hook) { postFifoReadHookForTests = std::move(hook); }
#endif

private:
    void copyFromInput(const uint8_t* source, int startByteFrame, int byteFrameCount);
    void copyToInterleaved(int startByteFrame, int byteFrameCount, uint8_t* output, uint32_t outputChannels) const;
    bool shouldHoldForStartupPrebuffer();

    const int channels;
    const int startupPrebufferByteFrames;
    const int startupPrebufferTimeoutMs;
    echo_audio_host::NativeFifo fifo;
    echo_audio_host::RenderEpoch renderEpoch;
    std::vector<uint8_t> buffer;
    mutable std::mutex fifoMutex;
    std::atomic<bool> inputEnded { false };
    std::atomic<bool> sessionHasAudio { false };
    std::atomic<bool> prebuffering { false };
    std::atomic<bool> stopRequested { false };
    std::atomic<bool> paused { false };
    std::atomic<uint64_t> sessionGeneration { 0 };
    std::atomic<uint64_t> framesPlayed { 0 };
    std::atomic<uint64_t> underrunCallbacks { 0 };
    std::atomic<uint64_t> underrunFrames { 0 };
    std::atomic<int64_t> prebufferDeadlineNs { 0 };
#if defined(ECHO_AUDIO_ENGINE_TESTS) && ECHO_AUDIO_ENGINE_TESTS
    std::function<void()> readAdmissionHookForTests;
    std::function<void()> postFifoReadHookForTests;
#endif
};

void pushNativeDsdPayload(NativeDsdRingSource& source, int channels, std::vector<char>& pending, const std::vector<char>& payload);
