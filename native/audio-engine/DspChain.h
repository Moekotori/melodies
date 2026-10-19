#pragma once

#include "ChannelBalanceProcessor.h"
#include "ConvolutionProcessor.h"
#include "CompressorProcessor.h"
#include "DeEsserProcessor.h"
#include "DspHeadroomProcessor.h"
#include "DspRackOrder.h"
#include "EqProcessor.h"
#include "LevelMeterProcessor.h"
#include "MultibandCompressorProcessor.h"
#include "PlaybackRateProcessor.h"
#include "ReplayGainProcessor.h"
#include "SpatialDspProcessor.h"
#include "TruePeakLimiterProcessor.h"
#include "WorkshopAudioEffectProcessor.h"

#include "buffer.h"

#include <atomic>
#include <cstdint>
#include <memory>
#include <optional>

namespace echo
{
enum class DspSessionTransition
{
    HardReplace,
    GaplessContinuation,
    AutomixPromotion,
};

class DspChain
{
public:
    struct ConfigurationSnapshot;
    static constexpr int processingPrecisionBits = 64;
    DspChain(
        EqProcessor& eqProcessorToUse,
        ConvolutionProcessor& convolutionProcessorToUse,
        ChannelBalanceProcessor& channelBalanceProcessorToUse,
        DspHeadroomProcessor& headroomProcessorToUse,
        ReplayGainProcessor& replayGainProcessorToUse,
        CompressorProcessor& compressorProcessorToUse,
        SpatialDspProcessor& spatialDspProcessorToUse,
        PlaybackRateProcessor& rateProcessorToUse,
        LevelMeterProcessor& meterProcessorToUse,
        DspRackOrder* rackOrderToUse = nullptr,
        WorkshopAudioEffectController* workshopAudioEffectControllerToUse = nullptr,
        MultibandCompressorProcessor* multibandProcessorToUse = nullptr,
        DeEsserProcessor* deEsserProcessorToUse = nullptr);

    void prepare(double sampleRate, int maximumBlockSize, int channelCount);
    std::shared_ptr<const ConfigurationSnapshot> configurationSnapshot() const;
    bool configurationMatches(const ConfigurationSnapshot& snapshot) const;
    bool applyConfiguration(const ConfigurationSnapshot& snapshot, std::string& error);
    void reset();
    // Only after the output callback/producer has been quiesced.
    void releaseRackLease() noexcept { playingRack.reset(); }
    void resetForHardReplace();
    void continueForGapless();
    void continueForAutomixPromotion() noexcept;
    void applySessionTransition(DspSessionTransition transition);
    int64_t tailFramesRemaining() const noexcept;
    bool isTailDrained() const noexcept { return tailFramesRemaining() == 0; }
    template <typename Sample>
    void processBlock(
        echo::PlanarAudioBuffer<Sample>& buffer,
        int startSample,
        int numSamples,
        bool processReplayGain = true,
        bool forceSafetyProcessing = false);

    bool isActive() const;
    bool hasClippingRisk() const;
    bool isSafetyLimiterProtecting() const;
    float safetyLimiterGainReductionDb() const;
    float safetyLimiterCeilingDb() const;
    float safetyLimiterTruePeakDb() const;
    void setUpstreamPcmProcessingActive(bool active);
    static void setSafetyLimiterEnabled(bool enabled);
    static bool isSafetyLimiterEnabled();
    static void setLimiterCeilingDb(float ceilingDb);
    static void setLimiterReleaseMs(float releaseMs);

private:
    friend struct DspPreparedRuntime;
    friend class DspPreparedHandoff;
    static constexpr int bypassTailBlocks = 16;

    template <typename Sample>
    void processSafetyLimiter(echo::PlanarAudioBuffer<Sample>& buffer, int startSample, int numSamples);

    EqProcessor& eqProcessor;
    ConvolutionProcessor& convolutionProcessor;
    ChannelBalanceProcessor& channelBalanceProcessor;
    DspHeadroomProcessor& headroomProcessor;
    ReplayGainProcessor& replayGainProcessor;
    CompressorProcessor& compressorProcessor;
    std::unique_ptr<MultibandCompressorProcessor> ownedMultibandProcessor;
    std::unique_ptr<DeEsserProcessor> ownedDeEsserProcessor;
    MultibandCompressorProcessor* multibandProcessor = nullptr;
    DeEsserProcessor* deEsserProcessor = nullptr;
    SpatialDspProcessor& spatialDspProcessor;
    PlaybackRateProcessor& rateProcessor;
    LevelMeterProcessor& meterProcessor;
    TruePeakLimiterProcessor truePeakLimiter;
    std::unique_ptr<DspRackOrder> ownedRackOrder;
    DspRackOrder* rackOrder = nullptr;
    std::unique_ptr<ProceduralDspProcessor> proceduralProcessor;
    std::unique_ptr<WorkshopAudioEffectController> ownedWorkshopAudioEffectController;
    WorkshopAudioEffectController* workshopController = nullptr;
    std::unique_ptr<WorkshopAudioEffectProcessor> workshopAudioEffectProcessor;

    std::optional<DspFilterRack::Lease> playingRack;
    int rackFadeRemaining = 0;
    std::atomic<int> rackTransitionTail { 0 };
    bool rackFadingOut = false;
    bool wasActive = false;
    int bypassTailBlocksRemaining = 0;
    std::atomic<bool> upstreamPcmProcessingActive { false };
    DspLoudnessMeter loudnessMeter;
    double processingSampleRate = 48000;
    double timingCostMs = 0;
    int64_t timingFrames = 0;
    double measuredRatio = 0;
    uint64_t observedEqRevision = 0;
};
} // namespace echo
