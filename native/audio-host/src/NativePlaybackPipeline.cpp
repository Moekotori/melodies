#include "NativePlaybackPipeline.h"
#include "NativeProcessingLimits.h"

#include <algorithm>
#include <chrono>
#include <cmath>
#include <iomanip>
#include <limits>
#include <sstream>
#include <utility>

namespace
{
constexpr int producerDecodeChunkFrames = echo_audio_host::sdmRealtimeAdmissionSourceFrames;

const char* mqaPassthroughStateReason(PcmRingAudioSource::MqaPassthroughState state) noexcept
{
    switch (state)
    {
        case PcmRingAudioSource::MqaPassthroughState::Active: return "active";
        case PcmRingAudioSource::MqaPassthroughState::VolumeNotUnity: return "volume_not_unity";
        case PcmRingAudioSource::MqaPassthroughState::DspActive: return "dsp_active";
        case PcmRingAudioSource::MqaPassthroughState::PlaybackRateActive: return "playback_rate_active";
        case PcmRingAudioSource::MqaPassthroughState::TransitionActive: return "transition_active";
        case PcmRingAudioSource::MqaPassthroughState::StopRequested: return "stop_requested";
        case PcmRingAudioSource::MqaPassthroughState::Inactive: return "inactive";
    }
    return "inactive";
}

bool isValidUpsampleFactor(int factor) noexcept
{
    return factor == 1 || factor == 2 || factor == 4 || factor == 8;
}

const char* sdmDecisionModeName(echo::SdmDecisionMode mode) noexcept
{
    switch (mode)
    {
        case echo::SdmDecisionMode::Horizon: return "horizon";
        case echo::SdmDecisionMode::Extra: return "extra";
        case echo::SdmDecisionMode::Greedy:
        default: return "off";
    }
}

const char* sdmPreModulatorProfileName(echo::SdmPreModulatorProfile profile) noexcept
{
    switch (profile)
    {
        case echo::SdmPreModulatorProfile::Heavy: return "heavy";
        case echo::SdmPreModulatorProfile::UltimateReference: return "ultimate-reference";
        case echo::SdmPreModulatorProfile::UltimateTransient: return "ultimate-transient";
        case echo::SdmPreModulatorProfile::Standard:
        default: return "standard";
    }
}

template <typename Status>
void applyFirPerformanceStatus(Status& status, const NativeFirProcessor& processor)
{
    status.processedBlocks = processor.processedBlocks();
    status.lastInputFrames = processor.lastInputFrames();
    status.lastOutputFrames = processor.lastOutputFrames();
    status.lastProcessMilliseconds = processor.lastProcessMilliseconds();
    status.averageProcessMilliseconds = processor.averageProcessMilliseconds();
    status.peakProcessMilliseconds = processor.peakProcessMilliseconds();
    status.warmupMilliseconds = processor.warmupMilliseconds();
    status.runtimeFallbacks = processor.runtimeFallbacks();
}

template <typename Status>
void applySdmPerformanceStatus(Status& status, const NativeSdmProcessor& processor)
{
    const auto clamps = processor.clampCounts();
    status.inputClamps = clamps.input;
    status.loopInputClamps = clamps.loopInput;
    status.quantizationErrorClamps = clamps.quantizationError;
    status.processedBlocks = processor.processedBlocks();
    status.lastInputFrames = processor.lastInputFrames();
    status.lastOutputFrames = processor.lastOutputFrames();
    status.lastProcessMilliseconds = processor.lastProcessMilliseconds();
    status.averageProcessMilliseconds = processor.averageProcessMilliseconds();
    status.peakProcessMilliseconds = processor.peakProcessMilliseconds();
    status.warmupMilliseconds = processor.warmupMilliseconds();
    status.runtimeFallbacks = processor.runtimeFallbacks();
}

void applySdmOversamplingPerformanceStatus(
    NativePlaybackPipeline::SdmStatus& status,
    const NativeFirProcessor& processor)
{
    status.oversamplingProcessedBlocks = processor.processedBlocks();
    status.oversamplingLastInputFrames = processor.lastInputFrames();
    status.oversamplingLastOutputFrames = processor.lastOutputFrames();
    status.oversamplingLastProcessMilliseconds = processor.lastProcessMilliseconds();
    status.oversamplingAverageProcessMilliseconds = processor.averageProcessMilliseconds();
    status.oversamplingPeakProcessMilliseconds = processor.peakProcessMilliseconds();
    status.oversamplingWarmupMilliseconds = processor.warmupMilliseconds();
    status.oversamplingRuntimeFallbacks = processor.runtimeFallbacks();
}

} // namespace

NativePlaybackPipeline::NativePlaybackPipeline(
    PcmRingAudioSource& pcm,
    DopRingSource& dop,
    NativeDsdRingSource& nativeDsd,
    int channels)
    : pcm_(pcm), dop_(dop), nativeDsd_(nativeDsd), channels_(std::max(1, channels))
{
    echoSrc_.setDoubleProcessing(true);
    automixEchoSrc_.setDoubleProcessing(true);
    automixEchoSrcAlternate_.setDoubleProcessing(true);
    sdmOversampler_.setDoubleProcessing(true);
    sdm_.setDoubleProcessing(true);
    std::string error;
    sdm_.configure(
        channels_,
        echo::SdmQualityProfile::Safe,
        176400,
        false,
        producerDecodeChunkFrames,
        error);
}

NativePlaybackPipeline::NativePlaybackPipeline(
    PcmRingAudioSource& pcm,
    DopRingSource& dop,
    NativeDsdRingSource& nativeDsd,
    int channels,
    echo::EqProcessor& eq,
    echo::ConvolutionProcessor& convolution,
    echo::ChannelBalanceProcessor& channelBalance,
    echo::DspHeadroomProcessor& headroom,
    echo::ReplayGainProcessor& replayGain,
    echo::CompressorProcessor& compressor,
    echo::SpatialDspProcessor& spatialDsp,
    echo::PlaybackRateProcessor& playbackRate,
    echo::LevelMeterProcessor& meter,
    echo::DspRackOrder* rackOrder,
    echo::WorkshopAudioEffectController* workshopAudioEffectController,
    echo::MultibandCompressorProcessor* multiband,
    echo::DeEsserProcessor* deEsser)
    : NativePlaybackPipeline(pcm, dop, nativeDsd, channels)
{
    workshopAudioEffectController_ = workshopAudioEffectController;
    sdmDsp_ = std::make_unique<PcmDomainDspStage>(
        eq, convolution, channelBalance, headroom, replayGain, compressor, spatialDsp, playbackRate, meter,
        rackOrder, workshopAudioEffectController, multiband, deEsser);
}

bool NativePlaybackPipeline::configureFirProcessor(
    NativeFirProcessor& processor,
    int channels,
    const FirConfig& config,
    const char* errorPrefix,
    int maximumInputFrames,
    int maximumUpsampleFactor,
    std::string& error)
{
    // Every playback consumer uses processDouble(). Reconfiguration builds
    // fresh processors, so retain FP64 processing across reconfiguration too.
    processor.setDoubleProcessing(true);
    if (config.stages.empty())
        return processor.configure(channels, {}, false, maximumInputFrames, error);

    if (config.sourceSampleRate <= 0 || config.targetSampleRate <= 0)
    {
        error = std::string(errorPrefix) + "_invalid_sample_rate";
        return false;
    }

    const int factor = calculateFirOutputFactor(config.stages, maximumUpsampleFactor, error);
    if (factor <= 0)
    {
        error = std::string(errorPrefix) + "_" + error;
        return false;
    }
    if (config.sourceSampleRate > std::numeric_limits<int>::max() / factor
        || config.sourceSampleRate * factor != config.targetSampleRate)
    {
        error = std::string(errorPrefix) + "_sample_rate_mismatch";
        return false;
    }

    if (!processor.configure(
            channels,
            config.stages,
            config.requestedBackend == ComputeBackend::Cuda,
            maximumInputFrames,
            error))
    {
        error = std::string(errorPrefix) + "_" + error;
        return false;
    }
    return true;
}

int NativePlaybackPipeline::calculateFirOutputFactor(
    const std::vector<echo::EchoSrcStageConfig>& stages,
    int maximumUpsampleFactor,
    std::string& error)
{
    int factor = 1;
    for (const auto& stage : stages)
    {
        if (!isValidUpsampleFactor(stage.upsampleFactor))
        {
            error = "invalid_upsample_factor";
            return 0;
        }
        if (stage.taps.empty())
        {
            error = "empty_taps";
            return 0;
        }
        if (factor > maximumUpsampleFactor / stage.upsampleFactor)
        {
            error = "upsample_factor_limit_exceeded";
            return 0;
        }
        factor *= stage.upsampleFactor;
    }
    error.clear();
    return factor;
}

NativePlaybackPipeline::FirStatus NativePlaybackPipeline::makeFirStatus(
    const FirConfig& config,
    const NativeFirProcessor& processor,
    bool active,
    uint64_t estimatedMacsPerSecond)
{
    FirStatus status;
    status.active = active;
    status.sourceSampleRate = config.sourceSampleRate;
    status.targetSampleRate = config.targetSampleRate;
    status.stageCount = static_cast<int>(config.stages.size());
    status.requestedBackend = config.requestedBackend;
    status.activeBackend = processor.backend() == NativeFirProcessor::Backend::Cuda
        ? ComputeBackend::Cuda
        : ComputeBackend::Cpu;
    status.estimatedMacsPerSecond = active ? estimatedMacsPerSecond : 0;
    status.nominalLatencyFrames = active
        ? processor.nominalLatencyOutputFrames()
        : 0;
    status.nominalLatencyMilliseconds =
        active && config.targetSampleRate > 0
        ? static_cast<double>(status.nominalLatencyFrames)
            * 1'000.0
            / static_cast<double>(config.targetSampleRate)
        : 0.0;
    if (active)
    {
        status.fallbackReason = processor.fallbackReason();
        status.deviceName = processor.deviceName();
        applyFirPerformanceStatus(status, processor);
    }
    return status;
}

NativePlaybackPipeline::SdmStatus NativePlaybackPipeline::makeSdmStatus(
    const SdmConfig& config,
    const NativeFirProcessor& oversampler,
    const NativeSdmProcessor& modulator,
    bool active,
    bool pcmDspRouted,
    uint64_t estimatedMacsPerSecond)
{
    SdmStatus status;
    status.active = active;
    status.sourceSampleRate = config.sourceSampleRate;
    status.targetSampleRate = config.targetSampleRate;
    status.stageCount = static_cast<int>(config.oversampling.stages.size());
    status.requestedBackend = config.requestedBackend;
    status.modulatorBackend =
        modulator.backend() == NativeSdmProcessor::Backend::Cuda
        ? ComputeBackend::Cuda
        : ComputeBackend::Cpu;
    status.oversamplingBackend =
        oversampler.backend() == NativeFirProcessor::Backend::Cuda
        ? ComputeBackend::Cuda
        : ComputeBackend::Cpu;
    status.activeBackend = status.modulatorBackend;
    status.estimatedMacsPerSecond = active ? estimatedMacsPerSecond : 0;
    status.decisionMode = sdmDecisionModeName(modulator.decisionMode());
    status.preModulatorProfile = sdmPreModulatorProfileName(config.preModulatorProfile);
    status.ultimateEnabled = config.ultimateEnabled;
    status.sampleRateFamily = config.sampleRateFamily;
    status.rationalConversion = config.rationalConversion;
    status.pcmDspRouted = pcmDspRouted;
    if (active)
    {
        status.fallbackReason = modulator.fallbackReason();
        status.oversamplingFallbackReason = oversampler.fallbackReason();
        status.oversamplingCudaProcessedBlocks = oversampler.cudaProcessedBlocks();
        status.oversamplingCudaTimings = oversampler.cudaTimings();
        status.deviceName = modulator.backend() == NativeSdmProcessor::Backend::Cuda
            ? modulator.deviceName()
            : oversampler.deviceName();
        applySdmPerformanceStatus(status, modulator);
        applySdmOversamplingPerformanceStatus(status, oversampler);
    }
    return status;
}

bool NativePlaybackPipeline::configurePassthroughOutput(OutputFormat format, std::string& error)
{
    configurationRevision_.fetch_add(1, std::memory_order_acq_rel);
    if (format == OutputFormat::Pcm)
    {
        error = "native_passthrough_requires_dsd_output";
        return false;
    }

    std::lock_guard<std::mutex> lock(processingMutex_);
    pcm_.requestStop();
    dop_.requestStop();
    nativeDsd_.requestStop();
    outputFormat_ = format;
    pcm_.setMqaPassthroughActive(false);
    pcm_.setUpstreamPcmProcessingActive(false);
    decoderSampleRate_.store(0, std::memory_order_release);
    pcm_.configureDither(echo::PcmDitherMode::Off, 16);
    processingStatus_ = {};
    processingStatus_.outputFormat = format;
    resetProcessorsLocked();
    error.clear();
    return true;
}

NativePlaybackPipeline::OutputFormat NativePlaybackPipeline::outputFormat() const
{
    std::lock_guard<std::mutex> lock(processingMutex_);
    return outputFormat_;
}

NativePlaybackPipeline::ProcessingStatus NativePlaybackPipeline::processingStatus() const
{
    std::lock_guard<std::mutex> lock(processingMutex_);
    auto status = processingStatus_;
    status.limiter.active =
        outputFormat_ == OutputFormat::Pcm
        && status.echoSrc.active
        && pcm_.isDspLimiterEnabled();
    status.limiter.protecting =
        status.limiter.active
        && pcm_.isDspLimiterProtecting();
    status.limiter.ceilingDb =
        pcm_.getDspLimiterCeilingDb();
    status.limiter.gainReductionDb =
        status.limiter.active
        ? pcm_.getDspLimiterGainReductionDb()
        : 0.0f;
    if (status.mqaPassthrough.active && ! pcm_.isMqaPassthroughActive())
    {
        status.mqaPassthrough.active = false;
        status.mqaPassthrough.reason = mqaPassthroughStateReason(pcm_.mqaPassthroughState());
    }
    if (status.echoSrc.active)
    {
        status.echoSrc.activeBackend = echoSrc_.backend() == NativeFirProcessor::Backend::Cuda
            ? ComputeBackend::Cuda
            : ComputeBackend::Cpu;
        status.echoSrc.fallbackReason = echoSrc_.fallbackReason();
        status.echoSrc.deviceName = echoSrc_.deviceName();
        applyFirPerformanceStatus(status.echoSrc, echoSrc_);
    }
    if (status.sdm.active)
    {
        status.sdm.modulatorBackend =
            sdm_.backend() == NativeSdmProcessor::Backend::Cuda
            ? ComputeBackend::Cuda
            : ComputeBackend::Cpu;
        status.sdm.oversamplingBackend =
            sdmOversampler_.backend() == NativeFirProcessor::Backend::Cuda
            ? ComputeBackend::Cuda
            : ComputeBackend::Cpu;
        status.sdm.activeBackend = status.sdm.modulatorBackend;
        status.sdm.fallbackReason = sdm_.fallbackReason();
        status.sdm.oversamplingFallbackReason =
            sdmOversampler_.fallbackReason();
        status.sdm.oversamplingCudaProcessedBlocks = sdmOversampler_.cudaProcessedBlocks();
        status.sdm.oversamplingCudaTimings = sdmOversampler_.cudaTimings();
        status.sdm.deviceName =
            sdm_.backend() == NativeSdmProcessor::Backend::Cuda
            ? sdm_.deviceName()
            : sdmOversampler_.deviceName();
        applySdmPerformanceStatus(status.sdm, sdm_);
        applySdmOversamplingPerformanceStatus(status.sdm, sdmOversampler_);
        status.sdm.modulatorOrder = sdm_.modulatorOrder();
        status.sdm.decisionMode = sdmDecisionModeName(sdm_.decisionMode());
        status.sdm.ntfPeakGain = sdm_.ntfPeakGain();
        status.sdm.peakFeedbackState = sdm_.peakFeedbackState();
        status.sdm.stabilityRecoveries = sdm_.stabilityRecoveryCount();
        status.sdm.realtime = realtimeHealth_.snapshot(sdmTransportTelemetryLocked());
        if (realtimeBackendIdentity_ != sdmBackendIdentityLocked())
        {
            status.sdm.realtime.executionMatchesObservation = false;
            if (status.sdm.realtime.health != echo_audio_host::SdmHealthState::Failed)
                status.sdm.realtime.health = echo_audio_host::SdmHealthState::Provisional;
        }
    }
    if (workshopAudioEffectController_ != nullptr)
    {
        const auto workshopState = workshopAudioEffectController_->getState();
        auto& workshopStatus = status.workshopAudioEffect;
        workshopStatus.configured = workshopState.enabled;
        workshopStatus.effect = workshopState.effect;
        workshopStatus.bitDepth = workshopState.bitDepth;
        workshopStatus.sampleRateHz = workshopState.sampleRateHz;
        workshopStatus.mix = workshopState.mix;
        workshopStatus.outputGainDb = workshopState.outputGainDb;
        workshopStatus.pulseMix = workshopState.pulseMix;
        workshopStatus.triangleMix = workshopState.triangleMix;
        workshopStatus.noiseMix = workshopState.noiseMix;
        workshopStatus.drive = workshopState.drive;
        workshopStatus.vocalCutStrength = workshopState.vocalCutStrength;
        workshopStatus.vocalCutBassPreserveHz = workshopState.vocalCutBassPreserveHz;
        const bool pcmDomainRouted = outputFormat_ == OutputFormat::Pcm
            || (status.sdm.active && status.sdm.pcmDspRouted);
        workshopStatus.active = workshopState.enabled
            && workshopState.mix > 0.00001f
            && pcmDomainRouted;
        if (! workshopState.enabled)
            workshopStatus.bypassReason = "disabled";
        else if (workshopState.mix <= 0.00001f)
            workshopStatus.bypassReason = "dry_mix";
        else if (! pcmDomainRouted)
            workshopStatus.bypassReason = "dsd_direct_passthrough";
        else
            workshopStatus.bypassReason.clear();
    }
    return status;
}

void NativePlaybackPipeline::setMqaPassthroughSourceStatus(
    bool active,
    std::string reason,
    std::string endpointFormat)
{
    std::lock_guard<std::mutex> lock(processingMutex_);
    const bool activated = pcm_.setMqaPassthroughActive(active);
    processingStatus_.mqaPassthrough.active = activated;
    processingStatus_.mqaPassthrough.reason = activated
        ? "active"
        : active
            ? mqaPassthroughStateReason(pcm_.mqaPassthroughState())
            : std::move(reason);
    processingStatus_.mqaPassthrough.endpointFormat = std::move(endpointFormat);
}

int NativePlaybackPipeline::decoderSampleRate(int outputSampleRate) const noexcept
{
    const int configured = decoderSampleRate_.load(std::memory_order_acquire);
    return configured > 0 ? configured : outputSampleRate;
}

void NativePlaybackPipeline::resetProcessorsLocked()
{
    resetSdmRealtimeLocked();
    gaplessEchoSrcActive_ = false;
    gaplessEchoSrcReady_ = false;
    gaplessEchoSrcCancelled_ = true;
    gaplessEchoSrcGeneration_ = 0;
    gaplessPendingInput_.clear();
    gaplessStateChanged_.notify_all();
    echoSrc_.reset();
    automixEchoSrc_.reset();
    automixEchoSrcAlternate_.reset();
    latestAutomixDeckLease_.store(0, std::memory_order_release);
    sdmOversampler_.reset();
    pcm_.resetDither();
    sdm_.setTargetGain(gain_.load(std::memory_order_acquire));
    sdm_.reset();
    if (sdmDsp_ != nullptr)
        sdmDsp_->reset();
}

void NativePlaybackPipeline::beginSession(bool startPaused)
{
    std::lock_guard<std::mutex> lock(processingMutex_);
    resetProcessorsLocked();
    pcm_.beginSession(startPaused);
    dop_.beginSession();
    nativeDsd_.beginSession();
    dop_.setPaused(startPaused);
    nativeDsd_.setPaused(startPaused);
    resetSdmRealtimeLocked();
}

void NativePlaybackPipeline::continueSessionAfterDrain()
{
    std::lock_guard<std::mutex> lock(processingMutex_);
    resetProcessorsLocked();
    pcm_.continueSessionAfterDrain();
    dop_.beginSession();
    nativeDsd_.beginSession();
    resetSdmRealtimeLocked();
}

void NativePlaybackPipeline::markInputEnded()
{
    (void)finishInput(std::nullopt);
}

bool NativePlaybackPipeline::markInputEndedUntil(std::chrono::steady_clock::time_point deadline)
{
    return finishInput(deadline);
}

bool NativePlaybackPipeline::finishInput(std::optional<std::chrono::steady_clock::time_point> deadline)
{
    PreparedOutput tail;
    bool hasPreparedTail = false;
    std::vector<double> gaplessPrime;
    OutputFormat endedFormat = OutputFormat::Pcm;
    uint64_t endedGeneration = 0;
    uint64_t finishingGeneration = 0;
    bool gaplessStateChanged = false;
    {
        std::lock_guard<std::mutex> lock(processingMutex_);
        endedFormat = outputFormat_;
        finishingGeneration = generation();
    }
    if (!drainSdmDspTail(endedFormat, finishingGeneration, deadline))
        return false;
    {
        std::lock_guard<std::mutex> lock(processingMutex_);
        if (outputFormat_ != endedFormat || generation() != finishingGeneration)
            return false;
        switch (endedFormat)
        {
            case OutputFormat::Pcm:
                endedGeneration = pcm_.session_.generation();
                if (echoSrc_.active())
                {
                    if (gaplessEchoSrcActive_
                        && !gaplessEchoSrcCancelled_
                        && gaplessEchoSrcGeneration_ == endedGeneration)
                    {
                        std::vector<std::vector<double>> histories;
                        automixEchoSrc_.reset();
                        gaplessEchoSrcReady_ =
                            echoSrc_.copyLatestHistory(histories)
                            && automixEchoSrc_.restoreHistory(histories);
                        if (!gaplessEchoSrcReady_)
                        {
                            gaplessEchoSrcCancelled_ = true;
                            tail.format = OutputFormat::Pcm;
                            tail.sourceGeneration = endedGeneration;
                            tail.pcm = echoSrc_.flushDouble();
                        }
                        else if (!gaplessPendingInput_.empty())
                        {
                            gaplessPrime = automixEchoSrc_.processDouble(
                                gaplessPendingInput_.data(),
                                static_cast<int>(
                                    gaplessPendingInput_.size()
                                    / static_cast<size_t>(channels_)));
                            gaplessPendingInput_.clear();
                        }
                        gaplessStateChanged = true;
                    }
                    else
                    {
                        tail.format = OutputFormat::Pcm;
                        tail.sourceGeneration = endedGeneration;
                        tail.pcm = echoSrc_.flushDouble();
                    }
                }
                break;
            case OutputFormat::Dop:
                endedGeneration = dop_.generation();
                tail.format = OutputFormat::Dop;
                tail.sourceGeneration = endedGeneration;
                if (sdmOversampler_.active())
                {
                    auto pcmTail = sdmOversampler_.flushDouble();
                    if (!sdmOversampler_.processingError().empty()) return false;
                    if (!pcmTail.empty())
                    {
                        tail.dop = sdm_.processDop(
                            pcmTail.data(),
                            static_cast<int>(pcmTail.size() / static_cast<size_t>(channels_)));
                    }
                }
                {
                    auto preModulatorTail = sdm_.flushDop();
                    tail.dop.insert(
                        tail.dop.end(),
                        preModulatorTail.begin(),
                        preModulatorTail.end());
                }
                if (!sdm_.processingError().empty()) return false;
                hasPreparedTail = !tail.dop.empty();
                break;
            case OutputFormat::NativeDsd:
                endedGeneration = nativeDsd_.generation();
                tail.format = OutputFormat::NativeDsd;
                tail.sourceGeneration = endedGeneration;
                if (sdmOversampler_.active())
                {
                    auto pcmTail = sdmOversampler_.flushDouble();
                    if (!sdmOversampler_.processingError().empty()) return false;
                    if (!pcmTail.empty())
                    {
                        tail.nativeDsd = sdm_.processNativeDsd(
                            pcmTail.data(),
                            static_cast<int>(pcmTail.size() / static_cast<size_t>(channels_)));
                    }
                }
                {
                    auto preModulatorTail = sdm_.flushNativeDsd();
                    tail.nativeDsd.insert(
                        tail.nativeDsd.end(),
                        preModulatorTail.begin(),
                        preModulatorTail.end());
                }
                if (!sdm_.processingError().empty()) return false;
                hasPreparedTail = !tail.nativeDsd.empty();
                break;
        }
    }

    // A ring push may wait for a paused device to drain, so never hold the
    // stateful processing lock while committing the FIR tail.
    if ((!tail.pcm.empty() || hasPreparedTail) && !pushPreparedOutput(tail, deadline))
        return false;
    if (!gaplessPrime.empty()
        && (endedGeneration != pcm_.session_.generation()
            || !pcm_.pushAutomixNext(
                gaplessPrime.data(),
                static_cast<int>(
                    gaplessPrime.size()
                    / static_cast<size_t>(channels_)))))
        return false;
    if (gaplessStateChanged)
        gaplessStateChanged_.notify_all();

    std::lock_guard<std::mutex> lock(processingMutex_);
    if (outputFormat_ != endedFormat)
        return false;
    const uint64_t currentGeneration =
        endedFormat == OutputFormat::Pcm
        ? pcm_.session_.generation()
        : endedFormat == OutputFormat::Dop
            ? dop_.generation()
            : nativeDsd_.generation();
    if (currentGeneration != endedGeneration)
        return false;

    // Keep the legacy all-source markers in sync, while the generation check
    // prevents a stale producer from ending a replacement/seek session.
    pcm_.markInputEnded();
    dop_.markInputEnded();
    nativeDsd_.markInputEnded();
    return true;
}

void NativePlaybackPipeline::requestStop()
{
    configurationRevision_.fetch_add(1, std::memory_order_acq_rel);
    pcm_.requestStop();
    dop_.requestStop();
    nativeDsd_.requestStop();
}

bool NativePlaybackPipeline::requestStopForGeneration(uint64_t generation)
{
    // The decoder feeds PCM, but its push can be waiting on the derived DSD
    // transport while output is paused. Cancel that wait before joining the
    // producer, and serialize with session replacement so an old teardown
    // cannot stop the replacement transport.
    std::lock_guard<std::mutex> lock(processingMutex_);
    if (!pcm_.requestStopForGeneration(generation))
        return false;
    configurationRevision_.fetch_add(1, std::memory_order_acq_rel);
    dop_.requestStop();
    nativeDsd_.requestStop();
    return true;
}

void NativePlaybackPipeline::allowTailDrainAfterStop()
{
    std::lock_guard<std::mutex> lock(processingMutex_);
    if (outputFormat_ == OutputFormat::NativeDsd)
        nativeDsd_.allowTailDrainAfterStop();
}

void NativePlaybackPipeline::setPaused(bool paused)
{
    playbackControlRevision_.fetch_add(1, std::memory_order_acq_rel);
    pcm_.setPaused(paused);
    dop_.setPaused(paused);
    nativeDsd_.setPaused(paused);
}

void NativePlaybackPipeline::setGain(float gain)
{
    const float normalized = std::max(0.0f, std::min(1.0f, gain));
    gain_.store(normalized, std::memory_order_release);
    pcm_.setGain(normalized);
}

bool NativePlaybackPipeline::prepareGapless()
{
    std::lock_guard<std::mutex> lock(processingMutex_);
    if (outputFormat_ != OutputFormat::Pcm
        || processingStatus_.sdm.active)
        return false;
    if (processingStatus_.echoSrc.active && pcm_.hasInputEnded())
        return false;

    gaplessEchoSrcActive_ = processingStatus_.echoSrc.active;
    gaplessEchoSrcReady_ = false;
    gaplessEchoSrcCancelled_ = false;
    gaplessEchoSrcGeneration_ = pcm_.session_.generation();
    gaplessPendingInput_.clear();
    pcm_.prepareGapless();
    return true;
}

bool NativePlaybackPipeline::pushGaplessNext(const float* samples, int frames)
{
    if (samples == nullptr || frames <= 0)
        return frames == 0;

    std::vector<double> prepared;
    uint64_t sourceGeneration = 0;
    {
        std::unique_lock<std::mutex> lock(processingMutex_);
        if (!gaplessEchoSrcActive_)
        {
            lock.unlock();
            return pcm_.pushAutomixNext(samples, frames);
        }

        sourceGeneration = gaplessEchoSrcGeneration_;
        if (!gaplessEchoSrcReady_)
        {
            if (gaplessPendingInput_.empty())
            {
                gaplessPendingInput_.assign(
                    samples,
                    samples
                        + static_cast<size_t>(frames)
                            * static_cast<size_t>(channels_));
                return true;
            }
            gaplessStateChanged_.wait(lock, [this, sourceGeneration] {
                return gaplessEchoSrcReady_
                    || gaplessEchoSrcCancelled_
                    || gaplessEchoSrcGeneration_ != sourceGeneration
                    || pcm_.session_.generation() != sourceGeneration;
            });
        }
        if (gaplessEchoSrcCancelled_
            || !gaplessEchoSrcReady_
            || gaplessEchoSrcGeneration_ != sourceGeneration
            || pcm_.session_.generation() != sourceGeneration)
            return false;

        if (!gaplessPendingInput_.empty())
        {
            prepared = automixEchoSrc_.processDouble(
                gaplessPendingInput_.data(),
                static_cast<int>(
                    gaplessPendingInput_.size()
                    / static_cast<size_t>(channels_)));
            gaplessPendingInput_.clear();
        }
        auto block = automixEchoSrc_.processDouble(samples, frames);
        prepared.insert(prepared.end(), block.begin(), block.end());
    }
    return sourceGeneration == pcm_.session_.generation()
        && pcm_.pushAutomixNext(
            prepared.data(),
            static_cast<int>(
                prepared.size() / static_cast<size_t>(channels_)));
}

void NativePlaybackPipeline::markGaplessNextEnded()
{
    std::vector<double> prepared;
    uint64_t sourceGeneration = 0;
    {
        std::unique_lock<std::mutex> lock(processingMutex_);
        if (!gaplessEchoSrcActive_)
        {
            lock.unlock();
            pcm_.markAutomixNextEnded();
            return;
        }

        sourceGeneration = gaplessEchoSrcGeneration_;
        if (!gaplessEchoSrcReady_)
        {
            gaplessStateChanged_.wait(lock, [this, sourceGeneration] {
                return gaplessEchoSrcReady_
                    || gaplessEchoSrcCancelled_
                    || gaplessEchoSrcGeneration_ != sourceGeneration
                    || pcm_.session_.generation() != sourceGeneration;
            });
        }
        if (gaplessEchoSrcCancelled_
            || !gaplessEchoSrcReady_
            || gaplessEchoSrcGeneration_ != sourceGeneration
            || pcm_.session_.generation() != sourceGeneration)
            return;

        if (!gaplessPendingInput_.empty())
        {
            prepared = automixEchoSrc_.processDouble(
                gaplessPendingInput_.data(),
                static_cast<int>(
                    gaplessPendingInput_.size()
                    / static_cast<size_t>(channels_)));
            gaplessPendingInput_.clear();
        }
        auto tail = automixEchoSrc_.flushDouble();
        prepared.insert(prepared.end(), tail.begin(), tail.end());
    }
    if (!prepared.empty()
        && (sourceGeneration != pcm_.session_.generation()
            || !pcm_.pushAutomixNext(
                prepared.data(),
                static_cast<int>(
                    prepared.size() / static_cast<size_t>(channels_)))))
        return;
    if (sourceGeneration == pcm_.session_.generation())
        pcm_.markAutomixNextEnded();
}

void NativePlaybackPipeline::cancelGapless()
{
    {
        std::lock_guard<std::mutex> lock(processingMutex_);
        gaplessEchoSrcActive_ = false;
        gaplessEchoSrcReady_ = false;
        gaplessEchoSrcCancelled_ = true;
        gaplessEchoSrcGeneration_ = 0;
        gaplessPendingInput_.clear();
    }
    gaplessStateChanged_.notify_all();
    pcm_.cancelAutomix();
}

uint64_t NativePlaybackPipeline::getGaplessBoundaryFrame() const
{
    return pcm_.getGaplessBoundaryFrame();
}

bool NativePlaybackPipeline::prepareAutomixFrames(
    uint64_t fadeStartFrame,
    uint64_t overlapFrames,
    double currentGainDb,
    double nextGainDb,
    double currentReplayGainDb,
    double nextReplayGainDb,
    bool rhythmicBassSwap,
    bool tailRide,
    bool echoThrow,
    double echoDelaySeconds,
    double echoSend,
    double echoFeedback,
    double vocalExitStartProgress,
    double vocalEntryStartProgress,
    AutomixHandoffSchedule handoffSchedule,
    echo::automix_alpha::Parameters alpha)
{
    return prepareAutomixDeckFrames(
        fadeStartFrame,
        overlapFrames,
        currentGainDb,
        nextGainDb,
        currentReplayGainDb,
        nextReplayGainDb,
        rhythmicBassSwap,
        tailRide,
        echoThrow,
        echoDelaySeconds,
        echoSend,
        echoFeedback,
        vocalExitStartProgress,
        vocalEntryStartProgress,
        handoffSchedule, alpha) != 0;
}

uint64_t NativePlaybackPipeline::prepareAutomixDeckFrames(
    uint64_t fadeStartFrame,
    uint64_t overlapFrames,
    double currentGainDb,
    double nextGainDb,
    double currentReplayGainDb,
    double nextReplayGainDb,
    bool rhythmicBassSwap,
    bool tailRide,
    bool echoThrow,
    double echoDelaySeconds,
    double echoSend,
    double echoFeedback,
    double vocalExitStartProgress,
    double vocalEntryStartProgress,
    AutomixHandoffSchedule handoffSchedule,
    echo::automix_alpha::Parameters alpha)
{
    std::lock_guard<std::mutex> lock(processingMutex_);
    if (outputFormat_ != OutputFormat::Pcm
        || processingStatus_.sdm.active)
        return 0;
    const uint64_t deckLease = pcm_.prepareAutomixDeck(
        fadeStartFrame,
        overlapFrames,
        currentGainDb,
        nextGainDb,
        currentReplayGainDb,
        nextReplayGainDb,
        rhythmicBassSwap,
        tailRide,
        echoThrow,
        echoDelaySeconds,
        echoSend,
        echoFeedback,
        vocalExitStartProgress,
        vocalEntryStartProgress,
        handoffSchedule, alpha);
    if (deckLease == 0)
        return 0;
    auto& deckProcessor = (deckLease & 1u) != 0
        ? automixEchoSrc_
        : automixEchoSrcAlternate_;
    deckProcessor.reset();
    latestAutomixDeckLease_.store(deckLease, std::memory_order_release);
    return deckLease;
}

bool NativePlaybackPipeline::pushAutomixNext(const float* samples, int frames)
{
    return pushAutomixDeck(
        latestAutomixDeckLease_.load(std::memory_order_acquire),
        samples,
        frames);
}

bool NativePlaybackPipeline::pushAutomixDeck(uint64_t deckLease, const float* samples, int frames)
{
    if (samples == nullptr || frames <= 0)
        return frames == 0 && deckLease != 0;
    if (deckLease == 0)
        return false;

    std::vector<double> prepared;
    {
        std::lock_guard<std::mutex> lock(processingMutex_);
        if (outputFormat_ != OutputFormat::Pcm)
            return false;
        auto& deckProcessor = (deckLease & 1u) != 0
            ? automixEchoSrc_
            : automixEchoSrcAlternate_;
        prepared = deckProcessor.processDouble(samples, frames);
    }
    if (prepared.empty())
        return true;
    return pcm_.pushAutomixDeck(
        deckLease,
        prepared.data(),
        static_cast<int>(prepared.size() / static_cast<size_t>(channels_)));
}

void NativePlaybackPipeline::markAutomixNextEnded()
{
    markAutomixDeckEnded(latestAutomixDeckLease_.load(std::memory_order_acquire));
}

void NativePlaybackPipeline::markAutomixDeckEnded(uint64_t deckLease)
{
    if (deckLease == 0)
        return;
    std::vector<double> tail;
    uint64_t sourceGeneration = 0;
    {
        std::lock_guard<std::mutex> lock(processingMutex_);
        sourceGeneration = pcm_.session_.generation();
        if (outputFormat_ == OutputFormat::Pcm
            && processingStatus_.echoSrc.active)
        {
            auto& deckProcessor = (deckLease & 1u) != 0
                ? automixEchoSrc_
                : automixEchoSrcAlternate_;
            tail = deckProcessor.flushDouble();
        }
    }
    if (!tail.empty()
        && (sourceGeneration != pcm_.session_.generation()
            || !pcm_.pushAutomixDeck(
                deckLease,
                tail.data(),
                static_cast<int>(
                    tail.size() / static_cast<size_t>(channels_)))))
        return;
    if (sourceGeneration != pcm_.session_.generation())
        return;
    pcm_.markAutomixDeckEnded(deckLease);
}

void NativePlaybackPipeline::failAutomixNext(uint64_t fadeFrames)
{
    failAutomixDeck(
        latestAutomixDeckLease_.load(std::memory_order_acquire),
        fadeFrames);
}

void NativePlaybackPipeline::failAutomixDeck(uint64_t deckLease, uint64_t fadeFrames)
{
    pcm_.failAutomixDeck(deckLease, fadeFrames);
}

bool NativePlaybackPipeline::commitAutomixDeck(uint64_t deckLease)
{
    const bool committed = pcm_.commitAutomixDeck(deckLease);
    if (committed)
    {
        uint64_t expectedLease = deckLease;
        latestAutomixDeckLease_.compare_exchange_strong(
            expectedLease,
            0,
            std::memory_order_acq_rel,
            std::memory_order_acquire);
    }
    return committed;
}

void NativePlaybackPipeline::cancelAutomix()
{
    latestAutomixDeckLease_.store(0, std::memory_order_release);
    pcm_.cancelAutomix();
}

void NativePlaybackPipeline::cancelAutomixWithFade()
{
    latestAutomixDeckLease_.store(0, std::memory_order_release);
    pcm_.cancelAutomixWithFade();
}

uint64_t NativePlaybackPipeline::getAutomixFadeStartFrame() const
{
    return pcm_.getAutomixFadeStartFrame();
}

uint64_t NativePlaybackPipeline::getAutomixFadeEndFrame() const
{
    return pcm_.getAutomixFadeEndFrame();
}

bool NativePlaybackPipeline::isAutomixActive() const
{
    return outputFormat_ == OutputFormat::Pcm && pcm_.isAutomixActive();
}

std::vector<double> NativePlaybackPipeline::preparePcm(const float* samples, int frames)
{
    return echoSrc_.processDouble(samples, frames);
}

bool NativePlaybackPipeline::prepareSdmPcm(
    const float* samples,
    int frames,
    std::vector<double>& output,
    std::string& error)
{
    output.assign(samples, samples + static_cast<size_t>(frames * channels_));
    for (auto& sample : output)
        if (!std::isfinite(sample)) sample = 0.0f;
    // The shared DSP graph is PCM-domain work. Run it at the decoder/source
    // rate before the potentially 32x SDM FIR expansion; processing it at the
    // carrier transport rate wastes realtime budget and changes time-based DSP
    // semantics such as convolution and compressor envelopes.
    if (sdmDsp_ != nullptr && !sdmDsp_->process(output, error))
        return false;
    for (auto& sample : output)
        if (!std::isfinite(sample)) sample = 0.0f;
    if (sdmOversampler_.active())
    {
        output = sdmOversampler_.processDouble(output.data(), frames);
        if (output.empty())
        {
            error = sdmOversampler_.processingError().empty()
                ? "native_sdm_oversampling_failed" : sdmOversampler_.processingError();
            return false;
        }
    }
    sdm_.setTargetGain(gain_.load(std::memory_order_acquire));
    error.clear();
    return true;
}

bool NativePlaybackPipeline::prepareOutputLocked(
    const float* samples,
    int frames,
    PreparedOutput& output,
    std::string& error)
{
    if (samples == nullptr || frames <= 0)
        return frames == 0;

    output = {};
    output.format = outputFormat_;
    switch (output.format)
    {
        case OutputFormat::Pcm: output.sourceGeneration = pcm_.session_.generation(); break;
        case OutputFormat::Dop: output.sourceGeneration = dop_.generation(); break;
        case OutputFormat::NativeDsd: output.sourceGeneration = nativeDsd_.generation(); break;
    }
    if (output.format == OutputFormat::Pcm)
    {
        output.pcm = preparePcm(samples, frames);
        error.clear();
        return true;
    }

    std::vector<double> pcm;
    if (!prepareSdmPcm(samples, frames, pcm, error))
        return false;
    const int outputFrames = static_cast<int>(pcm.size() / static_cast<size_t>(channels_));
    if (output.format == OutputFormat::Dop)
    {
        output.dop = sdm_.processDop(pcm.data(), outputFrames);
    }
    else
        output.nativeDsd = sdm_.processNativeDsd(pcm.data(), outputFrames);
    error = sdm_.processingError();
    return error.empty();
}

bool NativePlaybackPipeline::pushPreparedOutput(const PreparedOutput& output,
    std::optional<std::chrono::steady_clock::time_point> deadline)
{
    switch (output.format)
    {
        case OutputFormat::Pcm:
            return pcm_.pushForGeneration(
                output.pcm.data(), static_cast<int>(output.pcm.size() / static_cast<size_t>(channels_)), output.sourceGeneration);
        case OutputFormat::Dop:
            return dop_.pushForGeneration(
                output.dop.data(), static_cast<int>(output.dop.size() / static_cast<size_t>(channels_)), output.sourceGeneration, deadline);
        case OutputFormat::NativeDsd:
            return nativeDsd_.pushForGeneration(
                output.nativeDsd.data(), static_cast<int>(output.nativeDsd.size() / static_cast<size_t>(channels_)), output.sourceGeneration, deadline);
    }
    return false;
}

bool NativePlaybackPipeline::push(const float* samples, int frames)
{
    return pushSourceBlock(samples, frames, std::nullopt);
}

bool NativePlaybackPipeline::pushForGeneration(const float* samples, int frames, uint64_t generation)
{
    return pushSourceBlock(samples, frames, generation);
}

bool NativePlaybackPipeline::isDrained() const
{
    switch (outputFormat())
    {
        case OutputFormat::Dop: return dop_.isDrained();
        case OutputFormat::NativeDsd: return nativeDsd_.isDrained();
        case OutputFormat::Pcm: return pcm_.isDrained();
    }
    return true;
}

bool NativePlaybackPipeline::hasInputEnded() const
{
    switch (outputFormat())
    {
        case OutputFormat::Dop: return dop_.hasInputEnded();
        case OutputFormat::NativeDsd: return nativeDsd_.hasInputEnded();
        case OutputFormat::Pcm: return pcm_.hasInputEnded();
    }
    return true;
}

int NativePlaybackPipeline::getReadyFrames() const
{
    switch (outputFormat())
    {
        case OutputFormat::Dop: return dop_.getReadyFrames();
        case OutputFormat::NativeDsd: return static_cast<int>(nativeDsd_.getReadyFrames());
        case OutputFormat::Pcm: return pcm_.getReadyFrames();
    }
    return 0;
}

uint64_t NativePlaybackPipeline::getFramesPlayed() const
{
    return getSourceTrackFrames();
}

uint64_t NativePlaybackPipeline::getSourceTrackFrames() const
{
    switch (outputFormat())
    {
        case OutputFormat::Dop: return dop_.getFramesPlayed();
        case OutputFormat::NativeDsd: return nativeDsd_.getFramesPlayed();
        case OutputFormat::Pcm: return pcm_.getSourceTrackFrames();
    }
    return 0;
}

uint64_t NativePlaybackPipeline::getDeviceOutputFrames() const
{
    switch (outputFormat())
    {
        case OutputFormat::Dop: return dop_.getFramesPlayed();
        case OutputFormat::NativeDsd: return nativeDsd_.getFramesPlayed();
        case OutputFormat::Pcm: return pcm_.getDeviceOutputFrames();
    }
    return 0;
}

uint64_t NativePlaybackPipeline::getUnderrunCallbacks() const
{
    switch (outputFormat())
    {
        case OutputFormat::Dop: return dop_.getUnderrunCallbacks();
        case OutputFormat::NativeDsd: return nativeDsd_.getUnderrunCallbacks();
        case OutputFormat::Pcm: return pcm_.getUnderrunCallbacks();
    }
    return 0;
}

uint64_t NativePlaybackPipeline::getUnderrunFrames() const
{
    switch (outputFormat())
    {
        case OutputFormat::Dop: return dop_.getUnderrunFrames();
        case OutputFormat::NativeDsd: return nativeDsd_.getUnderrunFrames();
        case OutputFormat::Pcm: return pcm_.getUnderrunFrames();
    }
    return 0;
}
