#pragma once
#include "SdmCalibration.h"
#include "../../audio-engine/EqProcessor.h"
#include "../../audio-engine/ChannelBalanceProcessor.h"

namespace echo_audio_host {
// Lifetime order: the pipeline is destroyed before its rings and controllers.
// The parked PCM route never renders; the SDM stage owns a full isolated DSP
// graph restored from the immutable product snapshot, including IR ownership.
class SdmCalibrationGraph final {
private:
    echo::EqProcessor parkedEq_;
    echo::ChannelBalanceProcessor parkedBalance_;
    PcmRingAudioSource pcm_;
    DopRingSource dop_;
    NativeDsdRingSource native_;
    const int channels_;
    const bool nativeDsd_;
    std::vector<uint32_t> dopScratch_;
    std::vector<uint8_t> nativeScratch_;
public:
    SdmCalibrationGraph(int channels, int carrierCapacityFrames, bool nativeDsd);
    bool configure(const NativePlaybackPipeline::ProcessingConfig&,
        const PcmDomainDspStage::ConfigurationSnapshot&, float gain, std::string& error);
    uint64_t reportedDspTailFrames() const;
    void resetObservation();
    uint64_t readyBits() const;
    void drain();
    NativePlaybackPipeline pipeline;
};
}
