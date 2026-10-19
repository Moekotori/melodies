#pragma once
#include "NativePlaybackPipeline.h"
#include <stdexcept>

struct NativePlaybackPipelineTestAccess
{
    static void failNextFirBlock(NativePlaybackPipeline& pipeline)
    {
        pipeline.sdmOversampler_.cuda_.failNextBlockForTesting();
    }
    static echo::SdmClampCounts clamps(const NativePlaybackPipeline& pipeline) { return pipeline.sdm_.clampCounts(); }
    static PcmDomainDspStage& pcmDsp(NativePlaybackPipeline& pipeline) { return *pipeline.sdmDsp_; }
    static std::unique_lock<std::mutex> holdProcessing(NativePlaybackPipeline& pipeline)
    {
        return std::unique_lock<std::mutex>(pipeline.processingMutex_);
    }
    static uint64_t configurationOwnerRevision(const NativePlaybackPipeline& pipeline)
    {
        return pipeline.configurationOwnerRevision_.load(std::memory_order_acquire);
    }
    static std::vector<double> preparePcm(NativePlaybackPipeline& pipeline, const std::vector<float>& input, int channels)
    {
        std::lock_guard<std::mutex> lock(pipeline.processingMutex_);
        std::vector<double> result;
        std::string error;
        if (!pipeline.prepareSdmPcm(input.data(), static_cast<int>(input.size()) / channels, result, error))
            throw std::runtime_error(error);
        return result;
    }
};
