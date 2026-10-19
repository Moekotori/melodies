#include "HostCommon.h"
#include "SdmRealtimeAdmission.h"

#include <algorithm>
#include <cstddef>

int NativePlaybackPipeline::replacePreparedOutput(
    const PreparedOutput& output,
    int inputFrames,
    bool pausedAfterReplace)
{
    switch (output.format)
    {
        case OutputFormat::Pcm:
            return pcm_.replaceBufferedAudio(
                output.pcm.data(), static_cast<int>(output.pcm.size() / static_cast<size_t>(channels_)), pausedAfterReplace) > 0
                ? inputFrames : 0;
        case OutputFormat::Dop:
            return dop_.replaceBufferedAudio(
                output.dop.data(), static_cast<int>(output.dop.size() / static_cast<size_t>(channels_)), pausedAfterReplace) > 0
                ? inputFrames : 0;
        case OutputFormat::NativeDsd:
            return nativeDsd_.replaceBufferedAudio(
                output.nativeDsd.data(), static_cast<int>(output.nativeDsd.size() / static_cast<size_t>(channels_)), pausedAfterReplace) > 0
                ? inputFrames : 0;
    }
    return 0;
}

int NativePlaybackPipeline::replaceBufferedAudio(const float* samples, int frames, bool pausedAfterReplace)
{
    return replaceBufferedAudioWithPreroll(samples, frames, 0, pausedAfterReplace);
}

int NativePlaybackPipeline::replaceBufferedAudioWithPreroll(
    const float* samples,
    int frames,
    int prerollFrames,
    bool pausedAfterReplace)
{
    // Empty replacement is a lifecycle operation used by local mix seek.
    // It must reopen admission and discard the previous session's FIFO.
    if (frames == 0)
    {
        beginSession(pausedAfterReplace);
        return 0;
    }
    const int discardedInputFrames = std::clamp(prerollFrames, 0, std::max(0, frames));
    if (samples == nullptr || frames <= discardedInputFrames)
        return 0;

    std::lock_guard<std::mutex> lock(processingMutex_);
    // The source generation guards decoder ownership even when the actual
    // samples live in a derived DoP/native DSD ring. Advance it for seek too.
    if (outputFormat_ != OutputFormat::Pcm)
        pcm_.beginSession(pausedAfterReplace);
    resetProcessorsLocked();

    std::string error;
    for (int offset = 0; offset < discardedInputFrames;)
    {
        const int blockFrames = std::min(echo_audio_host::sdmRealtimeAdmissionSourceFrames, discardedInputFrames - offset);
        PreparedOutput warmup;
        if (!prepareOutputLocked(samples + static_cast<size_t>(offset) * channels_, blockFrames, warmup, error))
            return 0;
        offset += blockFrames;
    }

    // Warm the complete FIR/SDM state in bounded blocks, but never enqueue
    // pre-target samples or count them towards the new media clock.
    PreparedOutput output;
    output.format = outputFormat_;
    for (int offset = discardedInputFrames; offset < frames;)
    {
        const int blockFrames = std::min(echo_audio_host::sdmRealtimeAdmissionSourceFrames, frames - offset);
        PreparedOutput block;
        if (!prepareOutputLocked(samples + static_cast<size_t>(offset) * channels_, blockFrames, block, error))
            return 0;
        if (offset == discardedInputFrames)
            output = std::move(block);
        else switch (output.format)
        {
            case OutputFormat::Pcm: output.pcm.insert(output.pcm.end(), block.pcm.begin(), block.pcm.end()); break;
            case OutputFormat::Dop: output.dop.insert(output.dop.end(), block.dop.begin(), block.dop.end()); break;
            case OutputFormat::NativeDsd: output.nativeDsd.insert(output.nativeDsd.end(), block.nativeDsd.begin(), block.nativeDsd.end()); break;
        }
        offset += blockFrames;
    }
    const int replaced = replacePreparedOutput(output, frames - discardedInputFrames, pausedAfterReplace);
    if (replaced > 0) resetSdmRealtimeLocked();
    return replaced;
}

int NativePlaybackPipeline::seekPrerollFrames() const
{
    std::lock_guard<std::mutex> lock(processingMutex_);
    if (outputFormat_ == OutputFormat::Pcm)
        return echoSrc_.active() ? echoSrc_.prerollInputFrames() : 0;
    const int factor = std::max(1, sdmOversampler_.outputFactor());
    return (sdmOversampler_.active() ? sdmOversampler_.prerollInputFrames() : 0)
        + (sdm_.preModulatorTailTransportFrames() + factor - 1) / factor;
}
