#pragma once
#include "SdmCalibration.h"
#include <atomic>
#include <optional>
#include <thread>

namespace echo_audio_host {
struct SdmCalibrationRequest {
    NativePlaybackPipeline::ProcessingConfig processing;
    std::shared_ptr<const PcmDomainDspStage::ConfigurationSnapshot> dsp;
    SdmCalibrationOptions options;
    int channels = 2;
    float gain = 1;
};
// One control-thread owner. No job queue, per-track thread, or PCM retained.
// start/cancel/take/destruction must stay outside the producer mutex/callback.
// Completed output is retained once until taken. Repeated start never drops
// an in-flight job or an unread result; cancellation does not block the caller.
class SdmCalibrationJob final {
public:
    ~SdmCalibrationJob();
    bool start(SdmCalibrationRequest);
    void cancel() noexcept;
    bool running() const noexcept { return running_.load(std::memory_order_acquire); }
    SdmCalibrationPhase phase() const noexcept { return progress_.phase.load(std::memory_order_acquire); }
    uint64_t measuredBlocks() const noexcept { return progress_.measuredBlocks.load(std::memory_order_acquire); }
    std::optional<SdmCalibrationResult> takeResult();
private:
    std::optional<SdmCalibrationResult> result_;
    SdmCalibrationProgress progress_;
    std::atomic<bool> running_ {false};
    std::jthread worker_;
};
}
