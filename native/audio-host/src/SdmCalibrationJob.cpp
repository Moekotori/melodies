#include "SdmCalibrationJob.h"
#include <exception>

namespace echo_audio_host {
SdmCalibrationJob::~SdmCalibrationJob() { cancel(); } // jthread joins before result_ destruction.
bool SdmCalibrationJob::start(SdmCalibrationRequest request) {
    if (worker_.joinable() || !request.dsp) return false;
    progress_.phase.store(SdmCalibrationPhase::NotStarted, std::memory_order_release);
    progress_.measuredBlocks.store(0, std::memory_order_release);
    running_.store(true, std::memory_order_release);
    try {
        worker_ = std::jthread([this, request = std::move(request)](std::stop_token stop) {
            try {
                result_ = SdmCalibration::run(request.processing, *request.dsp, request.channels, request.gain, request.options, stop, &progress_);
            } catch (const std::exception& error) {
                SdmCalibrationResult failed; failed.outcome = SdmCalibrationOutcome::ProcessingFailed;
                failed.reason = std::string("sdm_calibration_exception:") + error.what(); result_ = std::move(failed);
                progress_.phase.store(SdmCalibrationPhase::Finished, std::memory_order_release);
            }
            running_.store(false, std::memory_order_release);
        });
    } catch (...) {
        running_.store(false, std::memory_order_release);
        throw;
    }
    return true;
}
void SdmCalibrationJob::cancel() noexcept { if (worker_.joinable()) worker_.request_stop(); }
std::optional<SdmCalibrationResult> SdmCalibrationJob::takeResult() {
    if (!worker_.joinable() || running()) return std::nullopt;
    worker_.join(); // Publication is complete; no join under an audio/DSP mutex.
    auto result = std::move(result_); result_.reset();
    return result;
}
}
