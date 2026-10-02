#pragma once

#include "ChannelBalanceProcessor.h"
#include "ConvolutionProcessor.h"
#include "CompressorProcessor.h"
#include "DspHeadroomProcessor.h"
#include "DspRackOrder.h"
#include "EqProcessor.h"
#include "LevelMeterProcessor.h"
#include "PlaybackRateProcessor.h"
#include "ReplayGainProcessor.h"
#include "SpatialDspProcessor.h"
#include "TruePeakLimiterProcessor.h"
#include "WorkshopAudioEffectProcessor.h"

#include "buffer.h"

#include <atomic>
#include <cstdint>
#include <memory>

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
        WorkshopAudioEffectController* workshopAudioEffectControllerToUse = nullptr);

    void prepare(double sampleRate, int maximumBlockSize, int channelCount);
    void reset();
    void resetForHardReplace();
    void continueForGapless();
    void continueForAutomixPromotion() noexcept;
    void applySessionTransition(DspSessionTransition transition);
    int64_t tailFramesRemaining() const noexcept;
    bool isTailDrained() const noexcept { return tailFramesRemaining() == 0; }
    void processBlock(
        echo::FloatAudioBuffer& buffer,
        int startSample,
        int numSamples,
        bool processReplayGain = true,
        bool forceSafetyProcessing = false);

    bool isActive() const;
    bool hasClippingRisk() const;
    bool isSafetyLimiterProtecting() const;
    float safetyLimiterGainReductionDb() const;
    float safetyLimiterCeilingDb() const;
    void setUpstreamPcmProcessingActive(bool active);
    static void setSafetyLimiterEnabled(bool enabled);
    static bool isSafetyLimiterEnabled();

private:
    static constexpr int bypassTailBlocks = 16;
    static constexpr float upstreamTruePeakCeilingDb = -1.0f;

    void processSafetyLimiter(echo::FloatAudioBuffer& buffer, int startSample, int numSamples);

    EqProcessor& eqProcessor;
    ConvolutionProcessor& convolutionProcessor;
    ChannelBalanceProcessor& channelBalanceProcessor;
    DspHeadroomProcessor& headroomProcessor;
    ReplayGainProcessor& replayGainProcessor;
    CompressorProcessor& compressorProcessor;
    SpatialDspProcessor& spatialDspProcessor;
    PlaybackRateProcessor& rateProcessor;
    LevelMeterProcessor& meterProcessor;
    TruePeakLimiterProcessor truePeakLimiter;
    std::unique_ptr<DspRackOrder> ownedRackOrder;
    DspRackOrder* rackOrder = nullptr;
    std::unique_ptr<WorkshopAudioEffectController> ownedWorkshopAudioEffectController;
    std::unique_ptr<WorkshopAudioEffectProcessor> workshopAudioEffectProcessor;

    bool wasActive = false;
    int bypassTailBlocksRemaining = 0;
    std::atomic<bool> upstreamPcmProcessingActive { false };
};
} // namespace echo
