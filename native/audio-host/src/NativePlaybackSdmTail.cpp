#include "HostCommon.h"
#include "SdmRealtimeAdmission.h"

#include <algorithm>
#include <cstdio>

bool NativePlaybackPipeline::drainSdmDspTail(
    OutputFormat format, uint64_t sourceGeneration,
    std::optional<std::chrono::steady_clock::time_point> deadline)
{
    int64_t remainingBudget = 0;
    {
        std::lock_guard<std::mutex> lock(processingMutex_);
        if (sourceGeneration != generation() || outputFormat_ != format)
            return false;
        if (format == OutputFormat::Pcm || sdmDsp_ == nullptr)
            return true;
        // Cover the maximum supported convolution plus IIR decay/cascaded
        // delays. Generative effects can continually excite a downstream
        // tail; they must not create an endless track after decoder EOF.
        remainingBudget = echo::roomCorrectionMaxTaps
            + static_cast<int64_t>(sdmDsp_->sampleRate()) * 5;
    }
    for (;;)
    {
        PreparedOutput block;
        {
            std::lock_guard<std::mutex> lock(processingMutex_);
            if (sourceGeneration != generation() || outputFormat_ != format)
                return false;
            const auto remaining = sdmDsp_->tailFramesRemaining();
            if (remaining <= 0)
                return true;
            if (deadline && std::chrono::steady_clock::now() >= *deadline)
                return false;
            if (remainingBudget <= 0)
            {
                std::fprintf(stderr, "[echo-audio-host] sdm_dsp_tail_limit_reached\n");
                sdmDsp_->reset();
                return true;
            }
            const int frames = static_cast<int>(std::min<int64_t>(remainingBudget,
                std::clamp<int64_t>(remaining, 256, echo_audio_host::sdmRealtimeAdmissionSourceFrames)));
            std::vector<float> silence(static_cast<size_t>(frames) * channels_, 0.0f);
            std::string error;
            if (!prepareOutputLocked(silence.data(), frames, block, error))
                return false;
            remainingBudget -= frames;
        }
        // Ring admission can wait while paused. Cancellation must be able to
        // acquire processingMutex_ and close all transport writers meanwhile.
        if (!pushPreparedOutput(block, deadline))
            return false;
    }
}
