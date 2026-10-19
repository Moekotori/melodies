#pragma once
#include "DspChain.h"

namespace echo
{
// Control-plane data only. IR coefficients are immutable and shared; runtime
// histories, PCM, meters, worker threads and GPU resources are never retained.
struct DspChain::ConfigurationSnapshot final
{
    EqState eq, headphoneCorrection;
    uint64_t eqRevision = 0, headphoneRevision = 0;
    bool eqLoudness = false, headphoneLoudness = false, chainLoudness = false;
    ConvolutionProcessor::ConfigurationSnapshot convolution;
    ChannelBalanceState balance;
    float headroomDb = 0;
    ReplayGainProcessor::ConfigurationSnapshot replayGain;
    CompressorState compressor;
    MultibandCompressorState multiband;
    DeEsserState deEsser;
    bool deEsserListen = false;
    CrossfeedState crossfeed;
    VirtualStageState virtualStage;
    StereoFieldState stereoField;
    ChannelMatrixState channelMatrix;
    PlaybackRateProcessor::Configuration rate;
    int meterIntervalMs = 0;
    bool spectrumEnabled = false;
    bool limiterEnabled = true, upstreamPcmProcessingActive = false;
    std::optional<float> limiterCeilingDb;
    float limiterReleaseMs = 0;
    DspRackOrder::Snapshot order;
    nlohmann::json filters, sequence, procedural;
    uint64_t filterRevision = 0;
    uint64_t proceduralRevision = 0;
    WorkshopAudioEffectState workshop;
    uint64_t workshopRevision = 0;
    bool stable = true;
    bool operator==(const ConfigurationSnapshot&) const = default;
};
}
