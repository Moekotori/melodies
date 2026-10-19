#include "NativePlaybackPipeline.h"
#include "NativeProcessingLimits.h"
#include "SdmRealtimeAdmission.h"
#include "../../audio-engine/DspPreparedRuntime.h"
#include <type_traits>
#include <utility>

namespace
{
using echo_audio_host::maximumNativeEchoSrcUpsampleFactor;
using echo_audio_host::maximumNativeSdmUpsampleFactor;
constexpr int producerDecodeChunkFrames = echo_audio_host::sdmRealtimeAdmissionSourceFrames;
static_assert(std::is_nothrow_swappable_v<NativeFirProcessor>);
static_assert(std::is_nothrow_swappable_v<NativeSdmProcessor>);
static_assert(std::is_nothrow_swappable_v<NativePlaybackPipeline::ProcessingStatus>);
}

struct NativePlaybackPipeline::PreparedConfiguration::State final
{
    struct Lease final
    {
        explicit Lease(std::shared_ptr<ConfigurationOwner> requested) : owner(std::move(requested))
        {
            bool expected = false;
            if (!owner->occupied.compare_exchange_strong(expected, true, std::memory_order_acq_rel)) owner.reset();
        }
        Lease(Lease&&) noexcept = default;
        ~Lease() { if (owner) owner->occupied.store(false, std::memory_order_release); }
        std::shared_ptr<ConfigurationOwner> owner;
    };
    State(Lease leaseToUse,
        const ProcessingConfig& configToUse, uint64_t revisionToUse, uint64_t generationToUse)
        : lease(std::move(leaseToUse)), config(configToUse),
          revision(revisionToUse), generation(generationToUse) {}
    // Release occupancy only after all retired resources are destroyed. This
    // lease can cross threads and outlive the originating pipeline safely.
    Lease lease;
    const ProcessingConfig config;
    NativeFirProcessor echoSrc, automixEchoSrc, automixEchoSrcAlternate, sdmOversampler;
    NativeSdmProcessor sdm;
    std::unique_ptr<PcmDomainDspStage> candidateDsp;
    std::shared_ptr<const PcmDomainDspStage::ConfigurationSnapshot> dspConfiguration;
    ProcessingStatus status;
    uint64_t revision, generation;
};

NativePlaybackPipeline::PreparedConfiguration::PreparedConfiguration(std::unique_ptr<State> state) noexcept
    : state_(std::move(state)) {}
NativePlaybackPipeline::PreparedConfiguration::~PreparedConfiguration() = default;

bool NativePlaybackPipeline::configure(const ProcessingConfig& config, std::string& error,
    const std::function<void()>& beforeCommit)
{
    auto prepared = prepareConfiguration(config, error);
    return prepared && commitConfiguration(std::move(prepared), error, beforeCommit);
}

std::unique_ptr<NativePlaybackPipeline::PreparedConfiguration>
NativePlaybackPipeline::prepareConfiguration(const ProcessingConfig& requested, std::string& error)
{
    const auto revision = configurationRevision_.fetch_add(1, std::memory_order_acq_rel) + 1;
    PreparedConfiguration::State::Lease candidateLease(configurationOwner_);
    if (!candidateLease.owner)
    {
        error = "native_processing_configuration_busy";
        return nullptr;
    }
    configurationOwnerRevision_.store(revision, std::memory_order_release);
    auto state = std::make_unique<PreparedConfiguration::State>(std::move(candidateLease),
        requested, revision, generation());
    const auto& config = state->config;
    const bool echoSrcActive = !config.echoSrc.stages.empty();
    const bool sdmOversamplingActive = !config.sdm.oversampling.stages.empty();
    const bool sdmActive = config.outputFormat != OutputFormat::Pcm;

    if (!sdmActive && sdmOversamplingActive)
    {
        error = "native_sdm_oversampling_requires_sdm_output";
        return nullptr;
    }
    if (sdmActive && echoSrcActive)
    {
        error = "native_sdm_cannot_combine_echo_src";
        return nullptr;
    }
    if (sdmActive && config.sdm.targetSampleRate <= 0)
    {
        error = "native_sdm_invalid_target_sample_rate";
        return nullptr;
    }
    const bool ultimatePreModulatorRequested =
        config.sdm.preModulatorProfile == echo::SdmPreModulatorProfile::UltimateReference
        || config.sdm.preModulatorProfile == echo::SdmPreModulatorProfile::UltimateTransient;
    if (sdmActive && ultimatePreModulatorRequested && !config.sdm.ultimateEnabled)
    {
        error = "native_sdm_ultimate_feature_flag_required";
        return nullptr;
    }
    if (sdmActive)
    {
        const bool family44 = config.sdm.sampleRateFamily == "44.1k";
        const bool family48 = config.sdm.sampleRateFamily == "48k";
        const bool targetMatches44 = config.sdm.targetSampleRate == 176'400
            || config.sdm.targetSampleRate == 352'800
            || config.sdm.targetSampleRate == 705'600
            || config.sdm.targetSampleRate == 1'411'200;
        const bool targetMatches48 = config.sdm.targetSampleRate == 192'000
            || config.sdm.targetSampleRate == 384'000
            || config.sdm.targetSampleRate == 768'000
            || config.sdm.targetSampleRate == 1'536'000;
        if ((!family44 && !family48) || (family44 && !targetMatches44) || (family48 && !targetMatches48))
        {
            error = "native_sdm_sample_rate_family_mismatch";
            return nullptr;
        }
    }
    if (sdmOversamplingActive
        && (config.sdm.sourceSampleRate <= 0 || config.sdm.oversampling.sourceSampleRate <= 0
            || config.sdm.oversampling.targetSampleRate <= 0
            || config.sdm.sourceSampleRate != config.sdm.oversampling.sourceSampleRate
            || config.sdm.targetSampleRate != config.sdm.oversampling.targetSampleRate))
    {
        error = "native_sdm_oversampling_sample_rate_mismatch";
        return nullptr;
    }

    auto& nextEchoSrc = state->echoSrc;
    if (!configureFirProcessor(
            nextEchoSrc,
            channels_,
            config.echoSrc,
            "native_echo_src",
            producerDecodeChunkFrames,
            maximumNativeEchoSrcUpsampleFactor,
            error))
        return nullptr;
    auto& nextAutomixEchoSrc = state->automixEchoSrc;
    if (!configureFirProcessor(
            nextAutomixEchoSrc,
            channels_,
            config.echoSrc,
            "native_automix_echo_src",
            producerDecodeChunkFrames,
            maximumNativeEchoSrcUpsampleFactor,
            error))
        return nullptr;
    auto& nextAutomixEchoSrcAlternate = state->automixEchoSrcAlternate;
    if (!configureFirProcessor(
            nextAutomixEchoSrcAlternate,
            channels_,
            config.echoSrc,
            "native_automix_echo_src_alternate",
            producerDecodeChunkFrames,
            maximumNativeEchoSrcUpsampleFactor,
            error))
        return nullptr;

    auto& nextSdmOversampler = state->sdmOversampler;
    auto sdmOversamplingConfig = config.sdm.oversampling;
    // The user preference admits upstream FIR independently. Terminal FIR and
    // modulation retain the CPU FP64 implementation, including Ultimate.
    sdmOversamplingConfig.requestedBackend = config.sdm.requestedBackend;
    if (!configureFirProcessor(
            nextSdmOversampler,
            channels_,
            sdmOversamplingConfig,
            "native_sdm_oversampling",
            producerDecodeChunkFrames,
            maximumNativeSdmUpsampleFactor,
            error))
        return nullptr;

    int sdmOversamplingFactor = 1;
    int sdmModulatorMaximumFrames = producerDecodeChunkFrames;
    if (sdmOversamplingActive)
    {
        sdmOversamplingFactor = calculateFirOutputFactor(
            config.sdm.oversampling.stages,
            maximumNativeSdmUpsampleFactor,
            error);
        if (sdmOversamplingFactor <= 0)
        {
            error = "native_sdm_oversampling_" + error;
            return nullptr;
        }
        sdmModulatorMaximumFrames =
            producerDecodeChunkFrames * sdmOversamplingFactor;
    }

    auto& nextSdm = state->sdm;
    nextSdm.setDoubleProcessing(true);
    const auto sdmDecisionMode = config.sdm.decisionMode != echo::SdmDecisionMode::Greedy
        ? config.sdm.decisionMode
        : config.sdm.extraLoop
            ? echo::SdmDecisionMode::Extra
            : echo::SdmDecisionMode::Greedy;
    if (!nextSdm.configure(
            channels_,
            config.sdm.qualityProfile,
            std::max(1, config.sdm.targetSampleRate),
            false,
            sdmModulatorMaximumFrames,
            error,
            sdmDecisionMode,
            config.sdm.heavyPreMod,
            sdmActive
                ? config.sdm.preModulatorProfile
                : echo::SdmPreModulatorProfile::Standard))
    {
        error = "native_sdm_modulator_" + error;
        return nullptr;
    }
    double sdmRealtimeAdmissionRatio = 0.0;
    std::string sdmRealtimeAdmissionWarning;
    auto& candidateDsp = state->candidateDsp;
    auto& candidateDspConfiguration = state->dspConfiguration;
    const int pcmDspSampleRate = config.sdm.sourceSampleRate > 0
        ? config.sdm.sourceSampleRate : config.sdm.targetSampleRate;
    echo_audio_host::SdmRealtimeAdmissionPcmDsp admissionDsp;
    if (sdmActive && sdmDsp_ != nullptr)
    {
        // The stage/controller references are lifetime-stable. Capture only
        // synchronized configuration data, without excluding the producer.
        candidateDspConfiguration = sdmDsp_->configurationSnapshot();
        if (!sdmDsp_->configurationMatches(*candidateDspConfiguration))
        {
            error = "native_processing_dsp_configuration_stale";
            return nullptr;
        }
        candidateDsp = PcmDomainDspStage::createAdmissionCandidate(*candidateDspConfiguration, error);
        if (!candidateDsp) return nullptr;
        candidateDsp->prepare(pcmDspSampleRate, producerDecodeChunkFrames, channels_);
        admissionDsp.process = [&candidateDsp](std::vector<double>& samples, std::string& processError) {
            return candidateDsp->process(samples, processError);
        };
        admissionDsp.reset = [&candidateDsp] { candidateDsp->reset(); };
    }
    if (sdmActive && !echo_audio_host::admitSdmRealtime(
            nextSdmOversampler,
            nextSdm,
            channels_,
            config.sdm.sourceSampleRate,
            config.sdm.targetSampleRate,
            sdmOversamplingFactor,
            sdmRealtimeAdmissionRatio,
            sdmRealtimeAdmissionWarning,
            error,
            admissionDsp,
            config.outputFormat == OutputFormat::NativeDsd))
    {
        return nullptr;
    }
    const uint64_t echoSrcEstimatedMacsPerSecond =
        nextEchoSrc.estimatedMacsPerInputFrame()
        * static_cast<uint64_t>(std::max(0, config.echoSrc.sourceSampleRate));
    const uint64_t sdmEstimatedMacsPerSecond =
        nextSdmOversampler.estimatedMacsPerInputFrame()
        * static_cast<uint64_t>(std::max(0, config.sdm.sourceSampleRate));

    ProcessingStatus nextStatus;
    nextStatus.outputFormat = config.outputFormat;
    nextStatus.ditherMode = config.ditherMode;
    nextStatus.ditherBitDepth = config.ditherBitDepth;
    nextStatus.ditherActive = config.outputFormat == OutputFormat::Pcm && config.ditherMode != echo::PcmDitherMode::Off;
    nextStatus.echoSrc = makeFirStatus(
        config.echoSrc,
        nextEchoSrc,
        echoSrcActive,
        echoSrcEstimatedMacsPerSecond);
    nextStatus.sdm = makeSdmStatus(
        config.sdm,
        nextSdmOversampler,
        nextSdm,
        sdmActive,
        sdmActive && candidateDsp != nullptr,
        sdmEstimatedMacsPerSecond);
    nextStatus.sdm.realtimeAdmissionRatio = sdmRealtimeAdmissionRatio;
    nextStatus.sdm.realtimeAdmissionWithinBudget = sdmRealtimeAdmissionWarning.empty();
    nextStatus.sdm.realtimeAdmissionWarning = std::move(sdmRealtimeAdmissionWarning);
    nextStatus.mqaPassthrough.requested = config.mqaPassthrough.requested;
    nextStatus.mqaPassthrough.sourceSampleRate = config.mqaPassthrough.sourceSampleRate;
    nextStatus.mqaPassthrough.sourceBitDepth = config.mqaPassthrough.sourceBitDepth;
    nextStatus.mqaPassthrough.reason = config.mqaPassthrough.requested ? "awaiting_source_probe" : "not_requested";
    if (sdmActive && candidateDsp != nullptr)
    {
        nextStatus.sdm.pcmDspSampleRate = candidateDsp->sampleRate();
        nextStatus.sdm.pcmDspMaximumBlockFrames = candidateDsp->maximumBlockFrames();
    }
    state->status = std::move(nextStatus);
    error.clear();
    return std::unique_ptr<PreparedConfiguration>(new PreparedConfiguration(std::move(state)));
}

bool NativePlaybackPipeline::commitConfiguration(std::unique_ptr<PreparedConfiguration> prepared, std::string& error,
    const std::function<void()>& beforeCommit)
{
    if (!prepared)
    {
        error = "native_processing_configuration_missing";
        return false;
    }
    auto& state = *prepared->state_;
    if (state.lease.owner != configurationOwner_)
    {
        error = "native_processing_configuration_owner_mismatch";
        return false;
    }
    const auto& config = state.config;
    const bool echoSrcActive = !config.echoSrc.stages.empty();
    const bool sdmOversamplingActive = !config.sdm.oversampling.stages.empty();
    const bool sdmActive = config.outputFormat != OutputFormat::Pcm;
    const auto revision = state.revision;
    const auto sourceGeneration = state.generation;
    const auto& candidateDspConfiguration = state.dspConfiguration;
    std::lock_guard<std::mutex> lock(processingMutex_);
    if (configurationRevision_.load(std::memory_order_acquire) != revision
        || generation() != sourceGeneration)
    {
        error = "native_processing_configuration_stale";
        return false;
    }
    if (candidateDspConfiguration && !sdmDsp_->configurationMatches(*candidateDspConfiguration))
    {
        error = "native_processing_dsp_configuration_stale";
        return false;
    }
    echo::DspPreparedHandoff handoff;
    if (state.candidateDsp && !sdmDsp_->beginPreparedHandoff(
            *state.candidateDsp, *candidateDspConfiguration, handoff, error))
        return false;
    // Validation is complete. JSON snapshot destruction uses temporary storage
    // in nlohmann; release it while failure can still leave output untouched.
    state.dspConfiguration.reset();
    if (beforeCommit) beforeCommit();
    if (state.candidateDsp)
    {
        pcm_.releaseDspRackLeaseForTransition();
        sdmDsp_->activatePrepared(*state.candidateDsp, handoff);
    }
    // A successful reconfiguration invalidates any prepared block that was
    // produced against the old FIR/SDM route. requestStop() is non-blocking;
    // the generation-aware ring push rejects that stale block after we release
    // this DSP-state lock.
    pcm_.requestStop();
    dop_.requestStop();
    nativeDsd_.requestStop();
    using std::swap;
    swap(echoSrc_, state.echoSrc);
    swap(automixEchoSrc_, state.automixEchoSrc);
    swap(automixEchoSrcAlternate_, state.automixEchoSrcAlternate);
    swap(sdmOversampler_, state.sdmOversampler);
    swap(sdm_, state.sdm);
    outputFormat_ = config.outputFormat;
    pcm_.setMqaPassthroughActive(false);
    pcm_.setUpstreamPcmProcessingActive(
        outputFormat_ == OutputFormat::Pcm && echoSrcActive);
    pcm_.configureDither(outputFormat_ == OutputFormat::Pcm ? config.ditherMode : echo::PcmDitherMode::Off,
                         config.ditherBitDepth);

    const int decoderRate = outputFormat_ == OutputFormat::Pcm && echoSrcActive
        ? config.echoSrc.sourceSampleRate
        : sdmActive && sdmOversamplingActive
            ? config.sdm.sourceSampleRate
            : 0;
    decoderSampleRate_.store(decoderRate, std::memory_order_release);

    swap(processingStatus_, state.status);
    resetProcessorsLocked();
    error.clear();
    // The producer lock is released before prepared destroys retired FIR/SDM
    // resources: CUDA cleanup and worker joins do not hold that lock.
    return true;
}
