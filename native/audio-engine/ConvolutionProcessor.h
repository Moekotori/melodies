#pragma once

#include "PartitionedConvolutionEngine.h"
#include "ConvolutionFilterBank.h"
#include "buffer.h"

#include <array>
#include <atomic>
#include <cstdint>
#include <memory>
#include <mutex>
#include <optional>
#include <string>
#include <vector>

namespace echo
{
constexpr int roomCorrectionMaxTaps = convolutionMaxTaps;
constexpr float roomCorrectionMinTrimDb = -24.0f;
constexpr float roomCorrectionMaxTrimDb = 6.0f;

struct RoomCorrectionState
{
    nlohmann::json filterBank;
    nlohmann::json selectedFilter;
    bool enabled = false;
    std::string status = "empty";
    std::string irId;
    std::string irName;
    std::string channelMode = "none";
    double sampleRate = 0.0;
    int inputChannels = 0;
    int outputChannels = 0;
    int deviceOutputChannels = 0;
    int tapCount = 0;
    float trimDb = 0.0f;
    int latencySamples = 0;
    bool clippingRisk = false;
    std::string error;
};

float clampRoomCorrectionTrimDb(float value);

class ConvolutionProcessor
{
    struct PreparedImpulse;
public:
    struct ConfigurationSnapshot
    {
        std::shared_ptr<const PreparedImpulse> impulse;
        std::optional<ConvolutionFilterBank> filterBank;
        std::string filterBankId, filterBankName;
        nlohmann::json selectedFilter;
        double sampleRate = 44100.0;
        int channels = 0, blockSize = 0, deviceChannels = 0;
        bool enabled = false, hasError = false;
        float trimDb = 0.0f;
        std::string error;
        uint64_t revision = 0;
        bool stable = true;
        bool operator==(const ConfigurationSnapshot& other) const noexcept
        {
            return stable && other.stable && revision == other.revision && impulse == other.impulse
                && enabled == other.enabled && trimDb == other.trimDb && deviceChannels == other.deviceChannels;
        }
    };
    ConvolutionProcessor();

    void prepare(double sampleRate, int maximumBlockSize, int channelCount);
    void reset();
    void resetForHardReplace();
    void continueForGapless() noexcept;
    template <typename Sample>
    void processBlock(echo::PlanarAudioBuffer<Sample>& buffer, int startSample, int numSamples);
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
    // Control snapshots retain immutable IR coefficients, never engine history.
    ConfigurationSnapshot configurationSnapshot() const;
    bool applyConfiguration(const ConfigurationSnapshot& snapshot);
    bool isEnabled() const;
    int requestedOutputChannels(int inputChannels = 0, double sampleRate = 0) const noexcept;
    void setDeviceOutputChannels(int channels) noexcept { deviceOutputChannels.store(channels, std::memory_order_release); }
    bool hasClippingRisk() const;
    int algorithmLatencySamples() const noexcept { return runtimeLatency.load(std::memory_order_acquire); }

#if defined(ECHO_AUDIO_ENGINE_TESTS) && ECHO_AUDIO_ENGINE_TESTS
    bool loadImpulseResponseForTests(const std::vector<std::vector<float>>& taps, double sourceSampleRate, const std::string& id, const std::string& name);
    uint64_t impulseDecodeCountForTests() const noexcept { return impulseDecodeCount.load(std::memory_order_acquire); }
#endif

private:
    friend struct DspPreparedRuntime;
    friend class DspPreparedHandoff;
    std::atomic<int> runtimeLatency { 0 };
    bool loadSingleImpulseResponse(const std::string& path, const std::string& id, const std::string& name,
        const std::string& identityToken, bool forceReload);
    bool loadSelectedBankFilter(bool forceReload);
    std::optional<ConvolutionFilterBank> filterBank;
    std::string filterBankId;
    std::string filterBankName;
    nlohmann::json selectedFilter;
    struct PreparedImpulse
    {
        std::vector<std::vector<double>> taps;
        std::vector<std::vector<double>> sourceTaps;
        std::vector<ConvolutionRoute> routes;
        int outputChannels = 0;
        std::string id;
        std::string name;
        std::string channelMode = "none";
        double sampleRate = 0.0;
        double sourceSampleRate = 0.0;
        int tapCount = 0;
        int latencySamples = 0;
        std::string sourcePath;
        uint64_t sourceSize = 0;
        int64_t sourceMtime = 0;
        std::string externalIdentity;
    };

    struct GraphSlot
    {
        GraphSlot(PreparedImpulse preparedImpulse, int processingChannels)
            : GraphSlot(std::make_shared<const PreparedImpulse>(std::move(preparedImpulse)), processingChannels)
        {
        }
        GraphSlot(std::shared_ptr<const PreparedImpulse> preparedImpulse, int processingChannels)
            : impulse(std::move(preparedImpulse)),
              channels(processingChannels),
              engine(impulse->taps, processingChannels, impulse->routes, impulse->outputChannels)
        {
        }

        std::shared_ptr<const PreparedImpulse> impulse;
        int channels;
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

    std::unique_ptr<PreparedImpulse> createPreparedImpulse(
        const echo::FloatAudioBuffer& source,
        double sourceSampleRate,
        double targetSampleRate,
        const std::string& id,
        const std::string& name);
    std::unique_ptr<PreparedImpulse> createPreparedImpulseFromTaps(
        const std::vector<std::vector<double>>& sourceTaps,
        double sourceSampleRate,
        double targetSampleRate,
        const std::string& id,
        const std::string& name,
        const std::vector<ConvolutionRoute>& routes = {}, int outputChannels = 0);
    static double sanitize(double value);
    static double protectClippingSample(double sample, bool shouldProtect, bool& risk);
    GraphLease acquireGraph() const noexcept;
    bool publishPrepared(std::unique_ptr<PreparedImpulse> impulse);
    void reclaimRetiredGraphsLocked();

    double currentSampleRate = 44100.0;
    std::atomic<int> deviceOutputChannels { 0 };
    int preparedChannels = 0;
    int preparedBlockSize = 0;
    mutable std::mutex graphMutex;
    mutable std::mutex configurationMutex;
    std::vector<std::unique_ptr<GraphSlot>> graphArchive;
    std::atomic<GraphSlot*> activeGraph { nullptr };
    std::atomic<uint32_t> activeReaderEpoch { 0 };
    mutable std::array<std::atomic<uint32_t>, 2> graphReaders {};

    std::atomic<bool> targetEnabled { false };
    std::atomic<float> atomicTrimDb { 0.0f };
    std::atomic<bool> clippingRisk { false };
    std::atomic<bool> hasError { false };
    std::atomic<uint64_t> impulseDecodeCount { 0 };
    std::atomic<uint64_t> configurationRevision { 0 };
    std::string errorMessage;
};
} // namespace echo
