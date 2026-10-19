#include "NativePlaybackPipeline.h"
#include <chrono>

namespace {
using Clock = std::chrono::steady_clock;
double milliseconds(Clock::time_point start, Clock::time_point end) {
    return std::chrono::duration<double, std::milli>(end - start).count();
}
}
echo_audio_host::SdmTransportTelemetry NativePlaybackPipeline::sdmTransportTelemetryLocked() const noexcept
{
    if (outputFormat_ == OutputFormat::Dop) return dop_.realtimeTelemetry();
    if (outputFormat_ == OutputFormat::NativeDsd) return nativeDsd_.realtimeTelemetry();
    return {};
}

void NativePlaybackPipeline::resetSdmRealtimeLocked() noexcept
{
    realtimeBackendIdentity_ = sdmBackendIdentityLocked();
    const auto& status = processingStatus_.sdm;
    realtimeHealth_.reset(status.active, status.sourceSampleRate > 0 ? status.sourceSampleRate : status.targetSampleRate,
        status.targetSampleRate, sdmTransportTelemetryLocked());
}

uint8_t NativePlaybackPipeline::sdmBackendIdentityLocked() const noexcept
{
    return (sdmOversampler_.backend() == NativeFirProcessor::Backend::Cuda ? 1u : 0u)
        | (sdm_.backend() == NativeSdmProcessor::Backend::Cuda ? 2u : 0u);
}

bool NativePlaybackPipeline::pushSourceBlock(const float* samples, int frames, std::optional<uint64_t> expectedGeneration)
{
    if (samples == nullptr || frames <= 0)
        return frames == 0 && (!expectedGeneration || *expectedGeneration == generation());
    PreparedOutput output;
    std::string error;
    const auto requested = Clock::now();
    Clock::time_point preparedAt, lockedAt;
    uint64_t epoch = 0, ownerGeneration = 0, controlRevision = 0;
    echo_audio_host::SdmTransportTelemetry before;
    {
        std::lock_guard<std::mutex> lock(processingMutex_);
        if (expectedGeneration && *expectedGeneration != generation()) return false;
        lockedAt = Clock::now();
        epoch = realtimeHealth_.epoch(); ownerGeneration = generation();
        controlRevision = playbackControlRevision_.load(std::memory_order_acquire);
        const auto initialTransport = sdmTransportTelemetryLocked();
        if (!prepareOutputLocked(samples, frames, output, error)) {
            if (processingStatus_.sdm.active) realtimeHealth_.fail();
            return false;
        }
        preparedAt = Clock::now();
        if (realtimeBackendIdentity_ != sdmBackendIdentityLocked()) {
            realtimeBackendIdentity_ = sdmBackendIdentityLocked();
            const auto& status = processingStatus_.sdm;
            realtimeHealth_.reset(status.active, status.sourceSampleRate > 0 ? status.sourceSampleRate : status.targetSampleRate,
                status.targetSampleRate, initialTransport);
            epoch = realtimeHealth_.epoch();
        }
        before = sdmTransportTelemetryLocked();
    }
    // Backpressure (including pause) must not retain the DSP lock. Observe the
    // entire enqueue wall interval separately; it is not pure DSP service time.
    const bool accepted = (!expectedGeneration || *expectedGeneration == generation()) && pushPreparedOutput(output);
    const auto enqueuedAt = Clock::now();
    if (output.format != OutputFormat::Pcm) {
        std::lock_guard<std::mutex> lock(processingMutex_);
        if (epoch == realtimeHealth_.epoch() && ownerGeneration == generation()) {
            const auto after = sdmTransportTelemetryLocked();
            if (accepted && after.generation == output.sourceGeneration) {
                const uint64_t bits = output.format == OutputFormat::Dop
                    ? static_cast<uint64_t>(output.dop.size() / channels_) * 16u
                    : static_cast<uint64_t>(output.nativeDsd.size() / channels_) * 8u;
                realtimeHealth_.record(frames, bits, milliseconds(lockedAt, preparedAt),
                    milliseconds(preparedAt, enqueuedAt), milliseconds(requested, lockedAt), before, after,
                    playbackControlRevision_.load(std::memory_order_acquire) != controlRevision);
            }
            else if (!after.stopped && after.generation == output.sourceGeneration) realtimeHealth_.fail();
        }
    }
    return accepted;
}
