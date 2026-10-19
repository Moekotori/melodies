#include "SdmCalibrationGraph.h"
#include <stdexcept>

namespace echo_audio_host {
SdmCalibrationGraph::SdmCalibrationGraph(int channels, int capacity, bool nativeDsd)
    : pcm_(channels, 1, 0, 0, 1, parkedEq_, parkedBalance_),
      dop_(channels, nativeDsd ? 1 : capacity, 0, 0),
      native_(channels, nativeDsd ? capacity * 2 : 1, 0, 0),
      channels_(channels), nativeDsd_(nativeDsd),
      dopScratch_(nativeDsd ? 0 : static_cast<size_t>(capacity) * channels),
      nativeScratch_(nativeDsd ? static_cast<size_t>(capacity) * channels * 2 : 0),
      pipeline(pcm_, dop_, native_, channels) {}

bool SdmCalibrationGraph::configure(const NativePlaybackPipeline::ProcessingConfig& config,
    const PcmDomainDspStage::ConfigurationSnapshot& snapshot, float gain, std::string& error) {
    pipeline.sdmDsp_ = PcmDomainDspStage::createAdmissionCandidate(snapshot, error);
    if (!pipeline.sdmDsp_ || !pipeline.configure(config, error)) return false;
    pipeline.setGain(gain);
    pipeline.beginSession();
    return true;
}
uint64_t SdmCalibrationGraph::reportedDspTailFrames() const {
    // Evaluate DSP support after excitation. Continuing excitation refreshes
    // tails, so waiting for tailFramesRemaining()==0 would never terminate.
    const auto dsp = pipeline.sdmDsp_->tailFramesRemaining();
    return static_cast<uint64_t>(std::max<int64_t>(0, dsp));
}
void SdmCalibrationGraph::resetObservation() {
    std::lock_guard<std::mutex> lock(pipeline.processingMutex_);
    pipeline.resetSdmRealtimeLocked(); // Preserve FIR, SDM, gains and PCM DSP histories.
}
uint64_t SdmCalibrationGraph::readyBits() const {
    return nativeDsd_ ? native_.realtimeTelemetry().readyDsdBits : dop_.realtimeTelemetry().readyDsdBits;
}
void SdmCalibrationGraph::drain() {
    if (nativeDsd_) {
        const auto frames = native_.getReadyByteFrames();
        native_.renderInterleaved(nativeScratch_.data(), frames, channels_);
    } else {
        const auto frames = dop_.getReadyFrames();
        dop_.renderInterleaved(dopScratch_.data(), frames, channels_);
    }
}
}
