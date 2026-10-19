#include "LevelMeterProcessor.h"

#include <algorithm>
#include <chrono>
#include <cmath>
#include <utility>

namespace echo
{
namespace
{
constexpr float silenceDb = -60.0f;
constexpr double pi = 3.14159265358979323846;
constexpr double spectrumTargetSampleRate = 48000.0;
constexpr double spectrumMinFrequencyHz = 40.0;
constexpr double spectrumMaxFrequencyHz = 18000.0;
constexpr float spectrumFloorDb = -72.0f;
constexpr float spectrumCeilingDb = -8.0f;
} // namespace

float LevelMeterProcessor::sanitizeDb(float db)
{
    if (! std::isfinite(db))
        return silenceDb;

    return std::max(silenceDb, std::min(0.0f, db));
}

float LevelMeterProcessor::sanitize(float value)
{
    return std::isfinite(value) ? value : 0.0f;
}

LevelMeterProcessor::LevelMeterProcessor()
{
    workerSnapshot.peakDb.assign(levelMeterMaxChannels, silenceDb);
    workerSnapshot.rmsDb.assign(levelMeterMaxChannels, silenceDb);
    workerSnapshot.visualSpectrum.assign(levelMeterVisualSpectrumBucketCount, 0.0f);
    worker = std::jthread([this](std::stop_token stopToken) { workerLoop(stopToken); });
}

LevelMeterProcessor::~LevelMeterProcessor()
{
    closeRealtimeEntry();
    waitForRealtimeQuiescence();
    {
        std::lock_guard<std::mutex> lock(controlMutex);
        callback = {};
        callbackPresent.store(false, std::memory_order_release);
    }
    telemetryGeneration.fetch_add(1, std::memory_order_acq_rel);
    if (worker.joinable())
        worker.request_stop();
    workerWaitCondition.notify_all();
}

void LevelMeterProcessor::rebuildSpectrumCoefficients()
{
    spectrumDecimationFactor = std::max(1, static_cast<int>(std::floor(sampleRate / spectrumTargetSampleRate)));
    spectrumSampleRate = sampleRate / static_cast<double>(spectrumDecimationFactor);
    spectrumWindowNormalization = 0.0f;
    for (size_t sample = 0; sample < spectrumWindow.size(); ++sample)
    {
        const double phase = spectrumWindow.size() > 1
            ? static_cast<double>(sample) / static_cast<double>(spectrumWindow.size() - 1)
            : 0.0;
        spectrumWindow[sample] = static_cast<float>(0.5 - 0.5 * std::cos(2.0 * pi * phase));
        spectrumWindowNormalization += spectrumWindow[sample];
    }
    spectrumWindowNormalization = std::max(1.0f, spectrumWindowNormalization);

    const double maxFrequency = std::max(
        spectrumMinFrequencyHz,
        std::min(spectrumMaxFrequencyHz, spectrumSampleRate * 0.45));
    const double frequencyRatio = maxFrequency / spectrumMinFrequencyHz;
    for (size_t bucket = 0; bucket < spectrumCoefficients.size(); ++bucket)
    {
        const double position = spectrumCoefficients.size() > 1
            ? static_cast<double>(bucket) / static_cast<double>(spectrumCoefficients.size() - 1)
            : 0.0;
        const double requestedFrequency = spectrumMinFrequencyHz * std::pow(frequencyRatio, position);
        // Probe each logarithmic frequency directly. Rounding to FFT bins would
        // make many of the denser low-frequency buckets measure the same tone.
        spectrumCoefficients[bucket] = static_cast<float>(
            2.0 * std::cos(2.0 * pi * requestedFrequency / spectrumSampleRate));
    }
}

void LevelMeterProcessor::prepare(double sampleRateIn, int maximumBlockSize, int channelCountIn)
{
    (void) maximumBlockSize;

    closeRealtimeEntry();
    waitForRealtimeQuiescence();
    std::lock_guard<std::mutex> lock(controlMutex);

    sampleRate = sampleRateIn > 0.0 ? sampleRateIn : 44100.0;
    channelCount = std::max(1, std::min(channelCountIn, static_cast<int>(levelMeterMaxChannels)));
    workerSnapshot.peakDb.assign(static_cast<size_t>(channelCount), silenceDb);
    workerSnapshot.rmsDb.assign(static_cast<size_t>(channelCount), silenceDb);
    workerSnapshot.visualSpectrum.assign(levelMeterVisualSpectrumBucketCount, 0.0f);
    workerSnapshot.visualSpectrumReady = false;

    rebuildSpectrumCoefficients();

    for (auto& slot : mailbox)
        slot.state.store(mailboxSlotEmpty, std::memory_order_release);
    reportSequence.store(0, std::memory_order_release);
    droppedSnapshotCount.store(0, std::memory_order_release);
    spectrumComputationCount.store(0, std::memory_order_release);
    workerGeneration = 0;
    std::fill(smoothedSpectrum.begin(), smoothedSpectrum.end(), 0.0f);

    const uint64_t generation = telemetryGeneration.fetch_add(1, std::memory_order_acq_rel) + 1;
    clearRealtimeState(generation);
    openRealtimeEntry();
}

void LevelMeterProcessor::reset()
{
    telemetryGeneration.fetch_add(1, std::memory_order_acq_rel);
    workerWaitCondition.notify_one();
    std::lock_guard<std::mutex> lock(controlMutex);
}

void LevelMeterProcessor::clearRealtimeState(uint64_t generation) noexcept
{
    peakSquares.fill(0.0f);
    rmsSquares.fill(0.0f);
    spectrumWriteIndex = 0;
    spectrumSampleCount = 0;
    spectrumDecimationCountdown = 0;
    samplesSinceReport = 0.0;
    realtimeSpectrumEnabled = visualSpectrumEnabled.load(std::memory_order_acquire);
    realtimeGeneration = generation;
}

template <typename Sample>
void LevelMeterProcessor::processBlock(echo::PlanarAudioBuffer<Sample>& buffer, int startSample, int numSamples) noexcept
{
    if (! tryEnterRealtime())
        return;

    processBlockRealtime(buffer, startSample, numSamples);
    leaveRealtime();
}

bool LevelMeterProcessor::tryEnterRealtime() noexcept
{
    uint64_t state = realtimeEntryState.load(std::memory_order_acquire);
    while ((state & realtimeEntryOpenBit) != 0)
    {
        if ((state & realtimeEntryCountMask) == realtimeEntryCountMask)
            return false;

        if (realtimeEntryState.compare_exchange_weak(
                state,
                state + 1,
                std::memory_order_acq_rel,
                std::memory_order_acquire))
            return true;
    }
    return false;
}

void LevelMeterProcessor::leaveRealtime() noexcept
{
    realtimeEntryState.fetch_sub(1, std::memory_order_release);
}

void LevelMeterProcessor::closeRealtimeEntry() noexcept
{
    realtimeEntryState.fetch_and(realtimeEntryCountMask, std::memory_order_acq_rel);
}

void LevelMeterProcessor::openRealtimeEntry() noexcept
{
    realtimeEntryState.store(realtimeEntryOpenBit, std::memory_order_release);
}

template <typename Sample>
void LevelMeterProcessor::processBlockRealtime(
    echo::PlanarAudioBuffer<Sample>& buffer,
    int startSample,
    int numSamples) noexcept
{
    const int interval = intervalMs.load(std::memory_order_acquire);
    if (interval <= 0 || numSamples <= 0)
        return;

    const int channels = std::min(buffer.getNumChannels(), channelCount);
    if (channels <= 0)
        return;

    const uint64_t generation = telemetryGeneration.load(std::memory_order_acquire);
    if (generation != realtimeGeneration)
        clearRealtimeState(generation);

    const bool spectrumEnabled = visualSpectrumEnabled.load(std::memory_order_acquire);
    if (spectrumEnabled != realtimeSpectrumEnabled)
    {
        spectrumWriteIndex = 0;
        spectrumSampleCount = 0;
        spectrumDecimationCountdown = 0;
        realtimeSpectrumEnabled = spectrumEnabled;
    }

    const double intervalSamples = sampleRate * static_cast<double>(interval) / 1000.0;
    if (intervalSamples <= 0.0)
        return;

    for (int channel = 0; channel < channels; ++channel)
    {
        const auto* src = buffer.getReadPointer(channel, startSample);
        if (src == nullptr)
            continue;

        const size_t channelIndex = static_cast<size_t>(channel);
        float peak = peakSquares[channelIndex];
        float sum = rmsSquares[channelIndex];
        for (int sample = 0; sample < numSamples; ++sample)
        {
            const float value = src[sample];
            const float square = sanitize(value * value);
            peak = std::max(peak, square);
            sum += square;
        }
        peakSquares[channelIndex] = peak;
        rmsSquares[channelIndex] = sum;
    }

    if (spectrumEnabled)
    {
        for (int sample = 0; sample < numSamples; ++sample)
        {
            if (spectrumDecimationCountdown > 0)
            {
                --spectrumDecimationCountdown;
                continue;
            }

            float mixedSample = 0.0f;
            int mixedChannels = 0;
            for (int channel = 0; channel < channels; ++channel)
            {
                const auto* src = buffer.getReadPointer(channel, startSample);
                if (src == nullptr)
                    continue;
                mixedSample += sanitize(src[sample]);
                ++mixedChannels;
            }
            if (mixedChannels > 0)
                mixedSample /= static_cast<float>(mixedChannels);

            spectrumSamples[spectrumWriteIndex] = mixedSample;
            spectrumWriteIndex = (spectrumWriteIndex + 1) % spectrumSamples.size();
            spectrumSampleCount = std::min(spectrumSampleCount + 1, spectrumSamples.size());
            spectrumDecimationCountdown = spectrumDecimationFactor - 1;
        }
    }

    samplesSinceReport += static_cast<double>(numSamples);
    if (samplesSinceReport < intervalSamples || ! callbackPresent.load(std::memory_order_acquire))
        return;

    publishReport(channels, spectrumEnabled, generation);
    std::fill_n(peakSquares.begin(), static_cast<size_t>(channels), 0.0f);
    std::fill_n(rmsSquares.begin(), static_cast<size_t>(channels), 0.0f);
    samplesSinceReport = 0.0;
}

bool LevelMeterProcessor::publishReport(int channels, bool spectrumEnabled, uint64_t generation) noexcept
{
    MailboxSlot* slot = nullptr;
    for (auto& candidate : mailbox)
    {
        uint32_t expected = mailboxSlotEmpty;
        if (candidate.state.compare_exchange_strong(
                expected,
                mailboxSlotWriting,
                std::memory_order_acq_rel,
                std::memory_order_relaxed))
        {
            slot = &candidate;
            break;
        }
    }

    if (slot == nullptr)
    {
        MailboxSlot* victim = nullptr;
        for (auto& candidate : mailbox)
        {
            if (candidate.state.load(std::memory_order_acquire) != mailboxSlotReady)
                continue;
            if (victim == nullptr || candidate.sequence < victim->sequence)
                victim = &candidate;
        }

        if (victim != nullptr)
        {
            uint32_t expected = mailboxSlotReady;
            if (victim->state.compare_exchange_strong(
                    expected,
                    mailboxSlotWriting,
                    std::memory_order_acq_rel,
                    std::memory_order_relaxed))
            {
                slot = victim;
                droppedSnapshotCount.fetch_add(1, std::memory_order_relaxed);
            }
        }
    }

    if (slot == nullptr)
    {
        droppedSnapshotCount.fetch_add(1, std::memory_order_relaxed);
        return false;
    }

    auto& frame = slot->frame;
    frame.channelCount = channels;
    frame.sampleRate = sampleRate;
    frame.reportSamples = samplesSinceReport;
    frame.timestampMs = samplesSinceReport * 1000.0 / sampleRate;
    frame.generation = generation;
    frame.visualSpectrumEnabled = spectrumEnabled;
    std::copy_n(peakSquares.begin(), static_cast<size_t>(channels), frame.peakSquares.begin());
    std::copy_n(rmsSquares.begin(), static_cast<size_t>(channels), frame.rmsSquares.begin());

    if (spectrumEnabled && spectrumSampleCount == spectrumSamples.size())
    {
        const size_t firstPart = spectrumSamples.size() - spectrumWriteIndex;
        std::copy_n(
            spectrumSamples.begin() + static_cast<std::ptrdiff_t>(spectrumWriteIndex),
            firstPart,
            frame.spectrumSamples.begin());
        std::copy_n(
            spectrumSamples.begin(),
            spectrumWriteIndex,
            frame.spectrumSamples.begin() + static_cast<std::ptrdiff_t>(firstPart));
        frame.spectrumSampleCount = spectrumSamples.size();
    }
    else
    {
        frame.spectrumSampleCount = spectrumEnabled ? spectrumSampleCount : 0;
    }

    slot->sequence = reportSequence.fetch_add(1, std::memory_order_relaxed) + 1;
    slot->state.store(mailboxSlotReady, std::memory_order_release);
    workerWaitCondition.notify_one();
    return true;
}

LevelMeterProcessor::MailboxSlot* LevelMeterProcessor::claimLatestReport() noexcept
{
    MailboxSlot* latest = nullptr;
    for (auto& slot : mailbox)
    {
        uint32_t expected = mailboxSlotReady;
        if (! slot.state.compare_exchange_strong(
                expected,
                mailboxSlotReading,
                std::memory_order_acq_rel,
                std::memory_order_relaxed))
            continue;

        if (latest == nullptr || slot.sequence > latest->sequence)
        {
            if (latest != nullptr)
            {
                latest->state.store(mailboxSlotEmpty, std::memory_order_release);
                droppedSnapshotCount.fetch_add(1, std::memory_order_relaxed);
            }
            latest = &slot;
        }
        else
        {
            slot.state.store(mailboxSlotEmpty, std::memory_order_release);
            droppedSnapshotCount.fetch_add(1, std::memory_order_relaxed);
        }
    }
    return latest;
}

bool LevelMeterProcessor::hasReadyReport() const noexcept
{
    return std::any_of(mailbox.begin(), mailbox.end(), [](const MailboxSlot& slot) {
        return slot.state.load(std::memory_order_acquire) == mailboxSlotReady;
    });
}

#if defined(ECHO_AUDIO_ENGINE_TESTS) && ECHO_AUDIO_ENGINE_TESTS
size_t LevelMeterProcessor::getMailboxOccupancyForTests() const noexcept
{
    size_t occupied = 0;
    for (const auto& slot : mailbox)
    {
        const uint32_t state = slot.state.load(std::memory_order_acquire);
        if (state != mailboxSlotEmpty)
            ++occupied;
    }
    return occupied;
}
#endif

void LevelMeterProcessor::workerLoop(std::stop_token stopToken)
{
    while (! stopToken.stop_requested())
    {
        MailboxSlot* slot = claimLatestReport();
        if (slot == nullptr)
        {
            std::unique_lock<std::mutex> lock(workerWaitMutex);
            workerWaitCondition.wait_for(lock, std::chrono::milliseconds(5), [&] {
                return stopToken.stop_requested() || hasReadyReport();
            });
            continue;
        }

#if defined(ECHO_AUDIO_ENGINE_TESTS) && ECHO_AUDIO_ENGINE_TESTS
        const int stallMs = workerStallMs.load(std::memory_order_acquire);
        if (stallMs > 0)
            std::this_thread::sleep_for(std::chrono::milliseconds(stallMs));
#endif

        try
        {
            processReport(*slot);
        }
        catch (...)
        {
        }
        slot->state.store(mailboxSlotEmpty, std::memory_order_release);
    }
}

void LevelMeterProcessor::processReport(MailboxSlot& slot)
{
    std::lock_guard<std::mutex> lock(controlMutex);
    const uint64_t currentGeneration = telemetryGeneration.load(std::memory_order_acquire);
    if (slot.frame.generation != currentGeneration || ! callback)
        return;

    if (workerGeneration != slot.frame.generation)
    {
        std::fill(smoothedSpectrum.begin(), smoothedSpectrum.end(), 0.0f);
        std::fill(workerSnapshot.visualSpectrum.begin(), workerSnapshot.visualSpectrum.end(), 0.0f);
        workerSnapshot.visualSpectrumReady = false;
        workerGeneration = slot.frame.generation;
    }

    const int channels = std::clamp(slot.frame.channelCount, 0, channelCount);
    workerSnapshot.peakDb.resize(static_cast<size_t>(channels));
    workerSnapshot.rmsDb.resize(static_cast<size_t>(channels));
    workerSnapshot.timestampMs = slot.frame.timestampMs;
    workerSnapshot.dsp = dspMeasurements.read();
    for (int channel = 0; channel < channels; ++channel)
    {
        const size_t channelIndex = static_cast<size_t>(channel);
        const float peakDb = slot.frame.peakSquares[channelIndex] > 0.0f
            ? 10.0f * std::log10(slot.frame.peakSquares[channelIndex])
            : silenceDb;
        workerSnapshot.peakDb[channelIndex] = sanitizeDb(peakDb);

        const float meanSquare = slot.frame.reportSamples > 0.0
            ? slot.frame.rmsSquares[channelIndex] / static_cast<float>(slot.frame.reportSamples)
            : 0.0f;
        const float rmsDb = meanSquare > 0.0f
            ? 10.0f * std::log10(meanSquare)
            : silenceDb;
        workerSnapshot.rmsDb[channelIndex] = sanitizeDb(rmsDb);
    }

    workerSnapshot.visualSpectrumReady = false;
    if (slot.frame.visualSpectrumEnabled
        && slot.frame.spectrumSampleCount == levelMeterSpectrumWindowSampleCount
        && visualSpectrumEnabled.load(std::memory_order_acquire))
    {
        workerSnapshot.visualSpectrumReady = computeVisualSpectrum(slot.frame, slot.frame.generation);
    }

    // Reuse the chronological mono analysis window on the worker. No extra
    // sampling, allocation or copies are added to the realtime callback.
    workerSnapshot.visualWaveform.clear();
    workerSnapshot.visualWaveformSampleRate = 0.0;
    if (workerSnapshot.visualSpectrumReady)
    {
        const auto start = slot.frame.spectrumSamples.end() - levelMeterVisualWaveformSampleCount;
        workerSnapshot.visualWaveform.assign(start, slot.frame.spectrumSamples.end());
        workerSnapshot.visualWaveformSampleRate = spectrumSampleRate;
    }

    if (slot.frame.generation != telemetryGeneration.load(std::memory_order_acquire) || ! callback)
        return;

    try
    {
        callback(workerSnapshot);
    }
    catch (...)
    {
    }
}

bool LevelMeterProcessor::computeVisualSpectrum(const RealtimeFrame& frame, uint64_t generation)
{
    spectrumComputationCount.fetch_add(1, std::memory_order_relaxed);
    for (size_t bucket = 0; bucket < spectrumCoefficients.size(); ++bucket)
    {
        if (generation != telemetryGeneration.load(std::memory_order_acquire)
            || ! visualSpectrumEnabled.load(std::memory_order_acquire))
            return false;

        const float coefficient = spectrumCoefficients[bucket];
        float q1 = 0.0f;
        float q2 = 0.0f;
        for (size_t sample = 0; sample < spectrumWindow.size(); ++sample)
        {
            const float value = frame.spectrumSamples[sample] * spectrumWindow[sample];
            const float q0 = value + coefficient * q1 - q2;
            q2 = q1;
            q1 = q0;
        }

        const float power = std::max(0.0f, q1 * q1 + q2 * q2 - coefficient * q1 * q2);
        const float magnitude = 2.0f * std::sqrt(power) / spectrumWindowNormalization;
        const float magnitudeDb = magnitude > 0.0f ? 20.0f * std::log10(magnitude) : spectrumFloorDb;
        const float unit = std::max(0.0f, std::min(
            1.0f,
            (magnitudeDb - spectrumFloorDb) / (spectrumCeilingDb - spectrumFloorDb)));
        const float shaped = std::pow(unit, 1.25f);
        const float smoothing = shaped > smoothedSpectrum[bucket] ? 0.55f : 0.2f;
        smoothedSpectrum[bucket] += (shaped - smoothedSpectrum[bucket]) * smoothing;
        workerSnapshot.visualSpectrum[bucket] = sanitize(smoothedSpectrum[bucket]);
    }
    return true;
}

void LevelMeterProcessor::waitForRealtimeQuiescence() const
{
    while ((realtimeEntryState.load(std::memory_order_acquire) & realtimeEntryCountMask) != 0)
        std::this_thread::yield();
}

void LevelMeterProcessor::setIntervalMs(int ms)
{
    const int clamped = std::max(0, std::min(levelMeterMaxIntervalMs, ms));
    intervalMs.store(clamped, std::memory_order_release);
    workerWaitCondition.notify_one();
}

void LevelMeterProcessor::setVisualSpectrumEnabled(bool enabled)
{
    visualSpectrumEnabled.store(enabled, std::memory_order_release);
}

void LevelMeterProcessor::setCallback(Callback callbackIn)
{
    {
        std::lock_guard<std::mutex> lock(controlMutex);
        callback = std::move(callbackIn);
        callbackPresent.store(static_cast<bool>(callback), std::memory_order_release);
    }
    workerWaitCondition.notify_one();
}

int LevelMeterProcessor::getIntervalMs() const
{
    return intervalMs.load(std::memory_order_acquire);
}

bool LevelMeterProcessor::isVisualSpectrumEnabled() const
{
    return visualSpectrumEnabled.load(std::memory_order_acquire);
}

bool LevelMeterProcessor::isEnabled() const
{
    return intervalMs.load(std::memory_order_acquire) > 0;
}

template void LevelMeterProcessor::processBlockRealtime<float>(echo::PlanarAudioBuffer<float>&, int, int) noexcept;
template void LevelMeterProcessor::processBlockRealtime<double>(echo::PlanarAudioBuffer<double>&, int, int) noexcept;
template void LevelMeterProcessor::processBlock<float>(echo::PlanarAudioBuffer<float>&, int, int) noexcept;
template void LevelMeterProcessor::processBlock<double>(echo::PlanarAudioBuffer<double>&, int, int) noexcept;
} // namespace echo
