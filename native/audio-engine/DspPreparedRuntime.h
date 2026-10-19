#pragma once
#include "DspChainConfiguration.h"

namespace echo
{
// Short control-side lease acquired before closing output. No worker, mutex,
// controller or audio history is copied. Activate requires quiescent rendering.
class DspPreparedHandoff final
{
public:
    bool acquire(DspChain& live, DspChain& prepared, const DspChain::ConfigurationSnapshot& expected, std::string& error);
    void activate() noexcept;
private:
    DspChain* live_ = nullptr;
    DspChain* prepared_ = nullptr;
    std::unique_lock<std::mutex> convolution_, convolutionGraph_, filters_, meter_, procedural_;
};

// Preparation-derived storage/geometry only. Existing reset methods initialize
// the hard replacement, preserving their coefficient, smoothing and RNG rules.
struct DspPreparedRuntime final
{
    static void geometry(EqProcessor&, EqProcessor&) noexcept;
    static void geometry(ChannelBalanceProcessor&, ChannelBalanceProcessor&) noexcept;
    static void geometry(DspHeadroomProcessor&, DspHeadroomProcessor&) noexcept;
    static void geometry(ReplayGainProcessor&, ReplayGainProcessor&) noexcept;
    static void geometry(CompressorProcessor&, CompressorProcessor&) noexcept;
    static void geometry(MultibandCompressorProcessor&, MultibandCompressorProcessor&) noexcept;
    static void geometry(DeEsserProcessor&, DeEsserProcessor&) noexcept;
    static void geometry(SpatialDspProcessor&, SpatialDspProcessor&) noexcept;
    static void geometry(PlaybackRateProcessor&, PlaybackRateProcessor&) noexcept;
    static void geometry(TruePeakLimiterProcessor&, TruePeakLimiterProcessor&) noexcept;
    static void geometry(WorkshopAudioEffectProcessor&, WorkshopAudioEffectProcessor&) noexcept;
    static void geometry(ProceduralDspProcessor&, ProceduralDspProcessor&) noexcept;
    static void convolution(ConvolutionProcessor&, ConvolutionProcessor&) noexcept;
    static void filters(DspFilterRack&, DspFilterRack&) noexcept;
    static void meter(LevelMeterProcessor&, const LevelMeterProcessor&) noexcept;
};
}
