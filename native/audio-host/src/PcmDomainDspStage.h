#pragma once

#include "../../audio-engine/DspChain.h"

#include <string>
#include <vector>

struct PcmDomainDspProcessors;
namespace echo { class DspPreparedHandoff; }

// Runs the host's PCM-domain DSP graph before a non-PCM transport encoder
// (for example PCM -> SDM -> DoP). It deliberately has no output callback
// responsibilities: NativePlaybackPipeline invokes it from the serialized
// producer/decode side so the stateful DSP processors are never driven by
// both a decode thread and a device callback.
class PcmDomainDspStage final
{
public:
    ~PcmDomainDspStage();
    PcmDomainDspStage(
        echo::EqProcessor& eq,
        echo::ConvolutionProcessor& convolution,
        echo::ChannelBalanceProcessor& channelBalance,
        echo::DspHeadroomProcessor& headroom,
        echo::ReplayGainProcessor& replayGain,
        echo::CompressorProcessor& compressor,
        echo::SpatialDspProcessor& spatialDsp,
        echo::PlaybackRateProcessor& playbackRate,
        echo::LevelMeterProcessor& meter,
        echo::DspRackOrder* rackOrder = nullptr,
        echo::WorkshopAudioEffectController* workshopAudioEffectController = nullptr,
        echo::MultibandCompressorProcessor* multiband = nullptr,
        echo::DeEsserProcessor* deEsser = nullptr);

    void prepare(double sampleRate, int maximumBlockFrames, int channels);
    using ConfigurationSnapshot = echo::DspChain::ConfigurationSnapshot;
    std::shared_ptr<const ConfigurationSnapshot> configurationSnapshot() const;
    bool configurationMatches(const ConfigurationSnapshot& snapshot) const;
    bool beginPreparedHandoff(PcmDomainDspStage& prepared, const ConfigurationSnapshot& expected,
        echo::DspPreparedHandoff& handoff, std::string& error);
    void activatePrepared(PcmDomainDspStage& prepared, echo::DspPreparedHandoff& handoff) noexcept;
    static std::unique_ptr<PcmDomainDspStage> createAdmissionCandidate(
        const ConfigurationSnapshot& snapshot, std::string& error);
    void reset();
    template <typename Sample>
    bool process(std::vector<Sample>& interleaved, std::string& error);
    int64_t tailFramesRemaining() const noexcept { return dspChain_.tailFramesRemaining(); }

    bool prepared() const noexcept { return prepared_; }
    int sampleRate() const noexcept { return sampleRate_; }
    int maximumBlockFrames() const noexcept { return maximumBlockFrames_; }

private:
    explicit PcmDomainDspStage(std::unique_ptr<PcmDomainDspProcessors> processors);
    std::unique_ptr<PcmDomainDspProcessors> ownedProcessors_;
    echo::ConvolutionProcessor& convolution_;
    echo::DspChain dspChain_;
    echo::DoubleAudioBuffer buffer_;
    int channels_ = 2;
    int sampleRate_ = 0;
    int maximumBlockFrames_ = 0;
    bool prepared_ = false;
};
