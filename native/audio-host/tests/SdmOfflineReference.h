#pragma once

#include "NativePlaybackPipeline.h"
#include <algorithm>
#include <stdexcept>

// Offline numerical reference only. Uses the real CPU processors and transport
// rings, without configuring a playback pipeline or claiming realtime admission.
class SdmOfflineReference final
{
public:
    explicit SdmOfflineReference(int channels)
        : dop(channels, 262144, 0, 0), dsd(channels, 524288, 0, 0), channels_(channels) {}

    bool configure(const NativePlaybackPipeline::ProcessingConfig& config, std::string& error)
    {
        format_ = config.outputFormat;
        fir_.setDoubleProcessing(true);
        sdm_.setDoubleProcessing(true);
        if (!fir_.configure(channels_, config.sdm.oversampling.stages, false, 4096, error)) return false;
        return sdm_.configure(channels_, config.sdm.qualityProfile, config.sdm.targetSampleRate,
            false, 4096 * fir_.outputFactor(), error, config.sdm.decisionMode,
            config.sdm.heavyPreMod, config.sdm.preModulatorProfile);
    }
    void beginSession()
    {
        fir_.reset(); sdm_.reset(); ended_ = false;
        dop.beginSession(); dsd.beginSession();
    }
    bool push(const float* input, int frames)
    {
        return encodeAndQueue(fir_.processDouble(input, frames));
    }
    void markInputEnded()
    {
        if (ended_) return;
        if (!encodeAndQueue(fir_.flushDouble())) throw std::runtime_error("offline_fir_tail_queue_failed");
        if (format_ == NativePlaybackPipeline::OutputFormat::Dop)
        {
            const auto tail = sdm_.flushDop();
            if (!tail.empty() && !dop.push(tail.data(), static_cast<int>(tail.size() / channels_)))
                throw std::runtime_error("offline_dop_tail_queue_failed");
            dop.markInputEnded();
        }
        else
        {
            const auto tail = sdm_.flushNativeDsd();
            if (!tail.empty() && !dsd.push(tail.data(), static_cast<int>(tail.size() / channels_)))
                throw std::runtime_error("offline_native_tail_queue_failed");
            dsd.markInputEnded();
        }
        ended_ = true;
    }
    void setPaused(bool paused) { dop.setPaused(paused); dsd.setPaused(paused); }
    bool isDrained() const
    {
        return format_ == NativePlaybackPipeline::OutputFormat::Dop ? dop.isDrained() : dsd.isDrained();
    }
    int replaceBufferedAudioWithPreroll(const float* input, int frames, int preroll, bool paused)
    {
        fir_.reset(); sdm_.reset(); ended_ = false;
        for (int offset = 0; offset < preroll;)
        {
            const int block = std::min(4096, preroll - offset);
            const auto carrier = fir_.processDouble(input + static_cast<size_t>(offset) * channels_, block);
            sdm_.setTargetGain(1.0);
            if (format_ == NativePlaybackPipeline::OutputFormat::Dop)
                (void)sdm_.processDop(carrier.data(), static_cast<int>(carrier.size() / channels_));
            else (void)sdm_.processNativeDsd(carrier.data(), static_cast<int>(carrier.size() / channels_));
            offset += block;
        }
        const auto carrier = fir_.processDouble(input + static_cast<size_t>(preroll) * channels_, frames - preroll);
        sdm_.setTargetGain(1.0);
        if (format_ == NativePlaybackPipeline::OutputFormat::Dop)
        {
            const auto output = sdm_.processDop(carrier.data(), static_cast<int>(carrier.size() / channels_));
            return dop.replaceBufferedAudio(output.data(), static_cast<int>(output.size() / channels_), paused);
        }
        const auto output = sdm_.processNativeDsd(carrier.data(), static_cast<int>(carrier.size() / channels_));
        return dsd.replaceBufferedAudio(output.data(), static_cast<int>(output.size() / channels_), paused);
    }
    echo::SdmClampCounts clampCounts() const { return sdm_.clampCounts(); }
    NativePlaybackPipeline::ProcessingStatus processingStatus() const
    {
        NativePlaybackPipeline::ProcessingStatus result;
        result.sdm.oversamplingProcessedBlocks = fir_.processedBlocks();
        result.sdm.oversamplingAverageProcessMilliseconds = fir_.averageProcessMilliseconds();
        result.sdm.processedBlocks = sdm_.processedBlocks();
        result.sdm.averageProcessMilliseconds = sdm_.averageProcessMilliseconds();
        result.sdm.stabilityRecoveries = sdm_.stabilityRecoveryCount();
        return result;
    }
    DopRingSource dop;
    NativeDsdRingSource dsd;

private:
    bool encodeAndQueue(const std::vector<double>& carrier)
    {
        if (carrier.empty()) return fir_.processingError().empty();
        sdm_.setTargetGain(1.0);
        if (format_ == NativePlaybackPipeline::OutputFormat::Dop)
        {
            const auto output = sdm_.processDop(carrier.data(), static_cast<int>(carrier.size() / channels_));
            return sdm_.processingError().empty() && dop.push(output.data(), static_cast<int>(output.size() / channels_));
        }
        const auto output = sdm_.processNativeDsd(carrier.data(), static_cast<int>(carrier.size() / channels_));
        return sdm_.processingError().empty() && dsd.push(output.data(), static_cast<int>(output.size() / channels_));
    }
    NativeFirProcessor fir_;
    NativeSdmProcessor sdm_;
    NativePlaybackPipeline::OutputFormat format_ = NativePlaybackPipeline::OutputFormat::Dop;
    int channels_;
    bool ended_ = false;
};
