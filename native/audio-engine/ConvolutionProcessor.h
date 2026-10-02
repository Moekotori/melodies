#pragma once

#include "PartitionedConvolutionEngine.h"
#include "buffer.h"

#include <array>
#include <atomic>
#include <cstdint>
#include <memory>
#include <mutex>
#include <string>
#include <vector>

namespace echo
{
constexpr int roomCorrectionMaxTaps = 8192;
constexpr float roomCorrectionMinTrimDb = -24.0f;
constexpr float roomCorrectionMaxTrimDb = 6.0f;

struct RoomCorrectionState
{
    bool enabled = false;
    std::string status = "empty";
    std::string irId;
    std::string irName;
    std::string channelMode = "none";
    double sampleRate = 0.0;
    int tapCount = 0;
    float trimDb = 0.0f;
    int latencySamples = 0;
    bool clippingRisk = false;
    std::string error;
};

float clampRoomCorrectionTrimDb(float value);

class ConvolutionProcessor
{
public:
    ConvolutionProcessor();

    void prepare(double sampleRate, int maximumBlockSize, int channelCount);
    void reset();
    void resetForHardReplace();
    void continueForGapless() noexcept;
    void processBlock(echo::FloatAudioBuffer& buffer, int startSample, int numSamples);
    int64_t tailFramesRemaining() const noexcept;
    bool isTailDrained() const noexcept { return tailFramesRemaining() == 0; }

    void setEnabled(bool shouldBeEnabled);
    void setTrimDb(float value);
    bool loadImpulseResponse(
        const std::string& path,
        const std::string& id,
        const std::string& name,
        const std::string& identityToken = {},
        bool forceReload = false);
    void clearImpulseResponse();

    RoomCorrectionState getState() const;
    bool isEnabled() const;
    bool hasClippingRisk() const;

#if defined(ECHO_AUDIO_ENGINE_TESTS) && ECHO_AUDIO_ENGINE_TESTS
    bool loadImpulseResponseForTests(const std::vector<std::vector<float>>& taps, double sourceSampleRate, const std::string& id, const std::string& name);
    uint64_t impulseDecodeCountForTests() const noexcept { return impulseDecodeCount.load(std::memory_order_acquire); }
#endif

private:
    struct PreparedImpulse
    {
        std::vector<std::vector<float>> taps;
        std::vector<std::vector<float>> sourceTaps;
        std::string id;
        std::string name;
        std::string channelMode = "none";
        double sampleRate = 0.0;
        double sourceSampleRate = 0.0;
        int tapCount = 0;
        std::string sourcePath;
        uint64_t sourceSize = 0;
        int64_t sourceMtime = 0;
        std::string externalIdentity;
    };

    struct GraphSlot
    {
        GraphSlot(PreparedImpulse preparedImpulse, int processingChannels)
            : impulse(std::move(preparedImpulse)),
              engine(impulse.taps, processingChannels)
        {
        }

        PreparedImpulse impulse;
        PartitionedConvolutionEngine engine;
    };

    class GraphLease final
    {
    public:
        GraphLease() = default;
        GraphLease(const GraphLease&) = delete;
        GraphLease& operator=(const GraphLease&) = delete;
        GraphLease(GraphLease&& other) noexcept;
        GraphLease& operator=(GraphLease&& other) noexcept;
        ~GraphLease();
        explicit operator bool() const noexcept { return slot != nullptr; }
        GraphSlot* operator->() const noexcept { return slot; }

    private:
        friend class ConvolutionProcessor;
        GraphLease(const ConvolutionProcessor* ownerToUse, GraphSlot* slotToUse, uint32_t readerEpochToUse) noexcept;
        void release() noexcept;
        const ConvolutionProcessor* owner = nullptr;
        GraphSlot* slot = nullptr;
        uint32_t readerEpoch = 0;
    };

    static std::unique_ptr<PreparedImpulse> createPreparedImpulse(
        const echo::FloatAudioBuffer& source,
        double sourceSampleRate,
        double targetSampleRate,
        const std::string& id,
        const std::string& name);
    static std::unique_ptr<PreparedImpulse> createPreparedImpulseFromTaps(
        const std::vector<std::vector<float>>& sourceTaps,
        double sourceSampleRate,
        double targetSampleRate,
        const std::string& id,
        const std::string& name);
    static float sanitize(float value);
    static float protectClippingSample(float sample, bool shouldProtect, bool& risk);
    GraphLease acquireGraph() const noexcept;
    bool publishPrepared(std::unique_ptr<PreparedImpulse> impulse);
    void reclaimRetiredGraphsLocked();

    double currentSampleRate = 44100.0;
    int preparedChannels = 0;
    int preparedBlockSize = 0;
    mutable std::mutex graphMutex;
    std::vector<std::unique_ptr<GraphSlot>> graphArchive;
    std::atomic<GraphSlot*> activeGraph { nullptr };
    std::atomic<uint32_t> activeReaderEpoch { 0 };
    mutable std::array<std::atomic<uint32_t>, 2> graphReaders {};

    std::atomic<bool> targetEnabled { false };
    std::atomic<float> atomicTrimDb { 0.0f };
    std::atomic<bool> clippingRisk { false };
    std::atomic<bool> hasError { false };
    std::atomic<uint64_t> impulseDecodeCount { 0 };
    std::string errorMessage;
};
} // namespace echo
