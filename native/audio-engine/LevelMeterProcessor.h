#pragma once

#include "buffer.h"
#include "DspMeasurement.h"

#include <array>
#include <atomic>
#include <condition_variable>
#include <cstdint>
#include <functional>
#include <mutex>
#include <stop_token>
#include <thread>
#include <vector>

namespace echo
{
constexpr int levelMeterMaxIntervalMs = 5000;
constexpr float levelMeterMinDb = -60.0f;
constexpr size_t levelMeterMaxChannels = 32;
constexpr size_t levelMeterVisualSpectrumBucketCount = 128;
constexpr size_t levelMeterSpectrumWindowSampleCount = 2048;
constexpr size_t levelMeterVisualWaveformSampleCount = levelMeterSpectrumWindowSampleCount;

struct LevelMeterSnapshot
{
    std::vector<float> peakDb;
    std::vector<float> rmsDb;
    std::vector<float> visualSpectrum;
    bool visualSpectrumReady = false;
    std::vector<float> visualWaveform;
    double visualWaveformSampleRate = 0.0;
    double timestampMs = 0.0;
    DspMeasurement dsp;
};

class LevelMeterProcessor
{
public:
    using Callback = std::function<void(const LevelMeterSnapshot&)>;

    LevelMeterProcessor();
    ~LevelMeterProcessor();

    LevelMeterProcessor(const LevelMeterProcessor&) = delete;
    LevelMeterProcessor& operator=(const LevelMeterProcessor&) = delete;
    LevelMeterProcessor(LevelMeterProcessor&&) = delete;
    LevelMeterProcessor& operator=(LevelMeterProcessor&&) = delete;

    void prepare(double sampleRate, int maximumBlockSize, int channelCount);
    void reset();
    template <typename Sample>
    void processBlock(echo::PlanarAudioBuffer<Sample>& buffer, int startSample, int numSamples) noexcept;

    void setIntervalMs(int ms);
    void setVisualSpectrumEnabled(bool enabled);
    void setCallback(Callback callback);
    int getIntervalMs() const;
    bool isVisualSpectrumEnabled() const;
    bool isEnabled() const;
    DspMeasurementStore dspMeasurements;

#if defined(ECHO_AUDIO_ENGINE_TESTS) && ECHO_AUDIO_ENGINE_TESTS
    uint64_t getDroppedSnapshotCountForTests() const noexcept
    {
        return droppedSnapshotCount.load(std::memory_order_acquire);
    }

    uint64_t getSpectrumComputationCountForTests() const noexcept
    {
        return spectrumComputationCount.load(std::memory_order_acquire);
    }

    size_t getMailboxOccupancyForTests() const noexcept;
    double getSpectrumSampleRateForTests() const noexcept { return spectrumSampleRate; }
    void setWorkerStallMsForTests(int ms) noexcept
    {
        workerStallMs.store(ms, std::memory_order_release);
    }
#endif

private:
    friend struct DspPreparedRuntime;
    friend class DspPreparedHandoff;
    static constexpr size_t mailboxSlotCount = 3;
    static constexpr uint64_t realtimeEntryOpenBit = uint64_t { 1 } << 63;
    static constexpr uint64_t realtimeEntryCountMask = realtimeEntryOpenBit - 1;
    static constexpr uint32_t mailboxSlotEmpty = 0;
    static constexpr uint32_t mailboxSlotWriting = 1;
    static constexpr uint32_t mailboxSlotReady = 2;
    static constexpr uint32_t mailboxSlotReading = 3;

    struct RealtimeFrame
    {
        std::array<float, levelMeterMaxChannels> peakSquares {};
        std::array<float, levelMeterMaxChannels> rmsSquares {};
        std::array<float, levelMeterSpectrumWindowSampleCount> spectrumSamples {};
        double sampleRate = 44100.0;
        double reportSamples = 0.0;
        double timestampMs = 0.0;
        int channelCount = 0;
        uint64_t generation = 0;
        size_t spectrumSampleCount = 0;
        bool visualSpectrumEnabled = false;
    };

    struct MailboxSlot
    {
        std::atomic<uint32_t> state { mailboxSlotEmpty };
        uint64_t sequence = 0;
        RealtimeFrame frame;
    };

    static_assert(std::atomic<uint32_t>::is_always_lock_free,
        "Level meter mailbox publication must be lock-free");
    static_assert(std::atomic<uint64_t>::is_always_lock_free,
        "Level meter generation publication must be lock-free");

    static float sanitizeDb(float db);
    static float sanitize(float value);
    template <typename Sample>
    void processBlockRealtime(echo::PlanarAudioBuffer<Sample>& buffer, int startSample, int numSamples) noexcept;
    bool tryEnterRealtime() noexcept;
    void leaveRealtime() noexcept;
    void closeRealtimeEntry() noexcept;
    void openRealtimeEntry() noexcept;
    void clearRealtimeState(uint64_t generation) noexcept;
    bool publishReport(int channels, bool spectrumEnabled, uint64_t generation) noexcept;
    MailboxSlot* claimLatestReport() noexcept;
    bool hasReadyReport() const noexcept;
    void workerLoop(std::stop_token stopToken);
    void processReport(MailboxSlot& slot);
    bool computeVisualSpectrum(const RealtimeFrame& frame, uint64_t generation);
    void waitForRealtimeQuiescence() const;
    void rebuildSpectrumCoefficients();

    std::atomic<int> intervalMs { 0 };
    std::atomic<bool> visualSpectrumEnabled { false };
    std::atomic<bool> callbackPresent { false };
    std::atomic<uint64_t> realtimeEntryState { realtimeEntryOpenBit };
    std::atomic<uint64_t> telemetryGeneration { 1 };
    std::atomic<uint64_t> reportSequence { 0 };
    std::atomic<uint64_t> droppedSnapshotCount { 0 };
    std::atomic<uint64_t> spectrumComputationCount { 0 };
#if defined(ECHO_AUDIO_ENGINE_TESTS) && ECHO_AUDIO_ENGINE_TESTS
    std::atomic<int> workerStallMs { 0 };
#endif

    Callback callback;
    std::mutex controlMutex;
    std::mutex workerWaitMutex;
    std::condition_variable workerWaitCondition;

    std::array<float, levelMeterMaxChannels> peakSquares {};
    std::array<float, levelMeterMaxChannels> rmsSquares {};
    std::array<MailboxSlot, mailboxSlotCount> mailbox {};
    LevelMeterSnapshot workerSnapshot;
    std::array<float, levelMeterSpectrumWindowSampleCount> spectrumSamples {};
    std::array<float, levelMeterSpectrumWindowSampleCount> spectrumWindow {};
    std::array<float, levelMeterVisualSpectrumBucketCount> spectrumCoefficients {};
    std::array<float, levelMeterVisualSpectrumBucketCount> smoothedSpectrum {};
    size_t spectrumWriteIndex = 0;
    size_t spectrumSampleCount = 0;
    int spectrumDecimationFactor = 1;
    int spectrumDecimationCountdown = 0;
    double spectrumSampleRate = 44100.0;
    float spectrumWindowNormalization = 1.0f;
    bool realtimeSpectrumEnabled = false;
    double samplesSinceReport = 0.0;
    double sampleRate = 44100.0;
    int channelCount = 0;
    uint64_t realtimeGeneration = 0;
    uint64_t workerGeneration = 0;

    std::jthread worker;
};
} // namespace echo
