#pragma once

#include "NativePlaybackPipeline.h"
#include "../../audio-engine/libav_decoder.h"

#include <string>

struct MqaPassthroughEndpoint
{
    std::string backend;
    std::string format;
    int sampleRate = 0;
    int hardwareSampleRate = 0;
};

struct MqaPassthroughDecision
{
    bool active = false;
    std::string reason;
};

MqaPassthroughDecision evaluateMqaPassthrough(
    const NativePlaybackPipeline::ProcessingStatus& processing,
    const echo::AudioProbe& probe,
    const MqaPassthroughEndpoint& endpoint);
