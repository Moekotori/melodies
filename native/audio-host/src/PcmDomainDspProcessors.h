#pragma once
#include "../../audio-engine/DspChain.h"

// One bounded admission candidate. No references to the live runtimes,
// meter callbacks, worker threads, mutexes or CUDA handles are copied.
struct PcmDomainDspProcessors final
{
    echo::EqProcessor eq;
    echo::ConvolutionProcessor convolution;
    echo::ChannelBalanceProcessor balance;
    echo::DspHeadroomProcessor headroom;
    echo::ReplayGainProcessor replayGain;
    echo::CompressorProcessor compressor;
    echo::SpatialDspProcessor spatial;
    echo::PlaybackRateProcessor rate;
    echo::LevelMeterProcessor meter;
    echo::DspRackOrder rack;
    echo::WorkshopAudioEffectController workshop;
    echo::MultibandCompressorProcessor multiband;
    echo::DeEsserProcessor deEsser;
};
