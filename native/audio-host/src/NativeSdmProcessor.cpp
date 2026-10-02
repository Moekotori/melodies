#include "NativeSdmProcessor.h"
#include "CudaFirProcessor.h"

#include <algorithm>
#include <chrono>
#include <cmath>
#include <iomanip>
#include <sstream>
#include <utility>

bool NativeSdmProcessor::configure(
    int channels,
    echo::SdmQualityProfile profile,
    int transportSampleRate,
    bool requestCuda,
    int maximumInputFrames,
    std::string& error,
    echo::SdmDecisionMode decisionMode,
    bool heavyPreMod,
    echo::SdmPreModulatorProfile preModulatorProfile)
{
    // Spans the unconditional cuda_.release() below and the CUDA re-admission,
    // so an active CUDA modulator keeps its context across reconfiguration and
    // the context is dropped only when the outcome is a CPU backend.
    const CudaFirProcessor::RuntimeHold runtimeHold;
    echo::SdmProcessor nextCpu;
    const auto interpolationMode =
#if ECHO_ENABLE_EXPERIMENTAL_SDM_SMOOTHSTEP
        echo::SdmInterpolationMode::SmoothstepExperimental;
#else
        echo::SdmInterpolationMode::Linear;
#endif
    nextCpu.configure(
        channels,
        profile,
        transportSampleRate,
        interpolationMode,
        decisionMode,
        heavyPreMod,
        preModulatorProfile);
    cpu_ = std::move(nextCpu);
    channels_ = std::max(1, channels);
    backend_ = Backend::Cpu;
    cuda_.release();
    fallbackReason_.clear();
    deviceName_.clear();
    warmupMilliseconds_ = 0.0;

    const bool ultimatePreModulator =
        preModulatorProfile == echo::SdmPreModulatorProfile::UltimateReference
        || preModulatorProfile == echo::SdmPreModulatorProfile::UltimateTransient;
    if (requestCuda && ultimatePreModulator)
    {
        error = "native_cuda_sdm_ultimate_premod_not_implemented";
        return false;
    }
    if (requestCuda && decisionMode == echo::SdmDecisionMode::ReferenceX)
    {
        error = "native_cuda_sdm_reference_x_not_implemented";
        return false;
    }

    const bool experimentalCpuOnly =
        interpolationMode != echo::SdmInterpolationMode::Linear;
    if (requestCuda && experimentalCpuOnly)
    {
        fallbackReason_ = "native_cuda_sdm_disabled_for_experimental_interpolation";
    }
    else if (requestCuda)
    {
        std::string cudaError;
        if (!CudaSdmProcessor::supportsRealtimeDoublePrecision(cudaError))
        {
            fallbackReason_ = cudaError.empty()
                ? "native_cuda_sdm_realtime_admission_failed"
                : std::move(cudaError);
        }
        else if (cuda_.configure(
                cpu_.configuration(),
                cpu_.state(),
                maximumInputFrames,
                cudaError))
        {
            const int admissionFrames =
                std::min(std::max(1, maximumInputFrames), 512);
            std::vector<float> admissionInput(
                static_cast<size_t>(admissionFrames * channels_),
                0.0f);
            for (int frame = 0; frame < admissionFrames; ++frame)
            {
                const double phase = static_cast<double>(frame);
                admissionInput[static_cast<size_t>(frame * channels_)] =
                    static_cast<float>(0.31 * std::sin(phase * 0.071));
                if (channels_ > 1)
                {
                    admissionInput[
                        static_cast<size_t>(frame * channels_ + 1)] =
                        static_cast<float>(0.27 * std::cos(phase * 0.053));
                }
            }
            const auto cpuStarted = std::chrono::steady_clock::now();
            const auto cpuOutput = cpu_.processDop(admissionInput.data(), admissionFrames);
            const double cpuMilliseconds =
                std::chrono::duration<double, std::milli>(
                    std::chrono::steady_clock::now() - cpuStarted).count();
            std::vector<uint32_t> cudaOutput;
            const auto cudaStarted = std::chrono::steady_clock::now();
            const bool cudaAdmissionCompleted = cuda_.processDop(
                admissionInput.data(),
                admissionFrames,
                cudaOutput,
                cudaError);
            const double cudaMilliseconds =
                std::chrono::duration<double, std::milli>(
                    std::chrono::steady_clock::now() - cudaStarted).count();
            const double admissionBudgetMilliseconds =
                static_cast<double>(admissionFrames)
                / static_cast<double>(std::max(1, transportSampleRate))
                * 1'000.0;
            cpu_.reset();
            std::string resetError;
            const bool cudaReset = cuda_.reset(cpu_.state(), resetError);
            if (cudaAdmissionCompleted
                && cudaReset
                && cudaOutput == cpuOutput
                && cudaMilliseconds < cpuMilliseconds * 0.9
                && cudaMilliseconds < admissionBudgetMilliseconds * 0.8)
            {
                backend_ = Backend::Cuda;
                deviceName_ = cuda_.deviceName();
                warmupMilliseconds_ = cuda_.warmupMilliseconds();
            }
            else if (!cudaAdmissionCompleted || !cudaReset)
            {
                fallbackReason_ = !cudaError.empty()
                    ? std::move(cudaError)
                    : std::move(resetError);
                if (fallbackReason_.empty())
                    fallbackReason_ = "native_cuda_sdm_admission_failed";
            }
            else if (cudaOutput != cpuOutput)
            {
                fallbackReason_ = "native_cuda_sdm_output_mismatch";
            }
            else
            {
                std::ostringstream reason;
                reason << std::fixed << std::setprecision(3)
                       << (cudaMilliseconds >= admissionBudgetMilliseconds * 0.8
                               ? "native_cuda_sdm_realtime_admission_failed:budget="
                               : "native_cuda_sdm_not_faster:cpu=")
                       << (cudaMilliseconds >= admissionBudgetMilliseconds * 0.8
                               ? admissionBudgetMilliseconds
                               : cpuMilliseconds)
                       << "ms,cuda=" << cudaMilliseconds << "ms";
                fallbackReason_ = reason.str();
            }
            if (backend_ != Backend::Cuda)
                cuda_.release();
        }
        else
        {
            fallbackReason_ = cudaError.empty()
                ? "native_cuda_sdm_initialization_failed"
                : std::move(cudaError);
        }
    }

    reset();
    error.clear();
    return true;
}

void NativeSdmProcessor::reset()
{
    cpu_.reset();
    if (backend_ == Backend::Cuda)
    {
        std::string error;
        if (!cuda_.reset(cpu_.state(), error))
            fallBackToCpu(error);
    }
    processedBlocks_ = 0;
    lastInputFrames_ = 0;
    lastOutputFrames_ = 0;
    lastProcessMilliseconds_ = 0.0;
    totalProcessMilliseconds_ = 0.0;
    peakProcessMilliseconds_ = 0.0;
    runtimeFallbacks_ = 0;
    preModulatorTailPending_ = false;
}

void NativeSdmProcessor::setTargetGain(double gain)
{
    cpu_.setTargetGain(gain);
    if (backend_ == Backend::Cuda)
        cuda_.setTargetGain(gain);
}

std::vector<uint32_t> NativeSdmProcessor::processDop(
    const float* interleaved,
    int frames)
{
    const auto started = std::chrono::steady_clock::now();
    if (backend_ != Backend::Cuda)
    {
        auto output = cpu_.processDop(interleaved, frames);
        if (frames > 0 && preModulatorTailTransportFrames() > 0)
            preModulatorTailPending_ = true;
        const auto finished = std::chrono::steady_clock::now();
        recordProcess(
            frames,
            static_cast<int>(output.size() / static_cast<size_t>(channels_)),
            std::chrono::duration<double, std::milli>(finished - started).count());
        return output;
    }

    std::vector<uint32_t> output;
    std::string cudaError;
    if (!cuda_.processDop(interleaved, frames, output, cudaError))
    {
        fallBackToCpu(cudaError);
        output = cpu_.processDop(interleaved, frames);
    }
    if (frames > 0 && preModulatorTailTransportFrames() > 0)
        preModulatorTailPending_ = true;
    const auto finished = std::chrono::steady_clock::now();
    recordProcess(
        frames,
        static_cast<int>(output.size() / static_cast<size_t>(channels_)),
        std::chrono::duration<double, std::milli>(finished - started).count());
    return output;
}

std::vector<uint8_t> NativeSdmProcessor::processNativeDsd(
    const float* interleaved,
    int frames)
{
    const auto started = std::chrono::steady_clock::now();
    if (backend_ != Backend::Cuda)
    {
        auto output = cpu_.processNativeDsd(interleaved, frames);
        if (frames > 0 && preModulatorTailTransportFrames() > 0)
            preModulatorTailPending_ = true;
        const auto finished = std::chrono::steady_clock::now();
        recordProcess(
            frames,
            static_cast<int>(
                output.size() / static_cast<size_t>(channels_ * 2)),
            std::chrono::duration<double, std::milli>(finished - started).count());
        return output;
    }

    std::vector<uint8_t> output;
    std::string cudaError;
    if (!cuda_.processNativeDsd(interleaved, frames, output, cudaError))
    {
        fallBackToCpu(cudaError);
        output = cpu_.processNativeDsd(interleaved, frames);
    }
    if (frames > 0 && preModulatorTailTransportFrames() > 0)
        preModulatorTailPending_ = true;
    const auto finished = std::chrono::steady_clock::now();
    recordProcess(
        frames,
        static_cast<int>(
            output.size() / static_cast<size_t>(channels_ * 2)),
        std::chrono::duration<double, std::milli>(finished - started).count());
    return output;
}

std::vector<uint32_t> NativeSdmProcessor::flushDop()
{
    const int tailFrames = preModulatorTailTransportFrames();
    if (!preModulatorTailPending_ || tailFrames <= 0)
        return {};
    std::vector<float> silence(
        static_cast<size_t>(tailFrames * channels_),
        0.0f);
    auto output = processDop(silence.data(), tailFrames);
    preModulatorTailPending_ = false;
    return output;
}

std::vector<uint8_t> NativeSdmProcessor::flushNativeDsd()
{
    const int tailFrames = preModulatorTailTransportFrames();
    if (!preModulatorTailPending_ || tailFrames <= 0)
        return {};
    std::vector<float> silence(
        static_cast<size_t>(tailFrames * channels_),
        0.0f);
    auto output = processNativeDsd(silence.data(), tailFrames);
    preModulatorTailPending_ = false;
    return output;
}

double NativeSdmProcessor::peakFeedbackState() const noexcept
{
    return backend_ == Backend::Cuda
        ? cuda_.peakFeedbackState()
        : cpu_.peakFeedbackState();
}

uint64_t NativeSdmProcessor::stabilityRecoveryCount() const noexcept
{
    return backend_ == Backend::Cuda
        ? cuda_.stabilityRecoveryCount()
        : cpu_.stabilityRecoveryCount();
}

void NativeSdmProcessor::fallBackToCpu(const std::string& cudaError)
{
    echo::SdmProcessorState state;
    const bool restored =
        cuda_.copyLatestState(state) && cpu_.restoreState(state);
    backend_ = Backend::Cpu;
    ++runtimeFallbacks_;
    fallbackReason_ = cudaError.empty()
        ? "native_cuda_sdm_runtime_failure"
        : "native_cuda_sdm_runtime_failure:" + cudaError;
    if (!restored)
    {
        cpu_.reset();
        fallbackReason_ += ":state_restore_failed";
    }
    deviceName_.clear();
}

void NativeSdmProcessor::recordProcess(
    int inputFrames,
    int outputFrames,
    double milliseconds)
{
    ++processedBlocks_;
    lastInputFrames_ = std::max(0, inputFrames);
    lastOutputFrames_ = std::max(0, outputFrames);
    lastProcessMilliseconds_ = milliseconds;
    totalProcessMilliseconds_ += milliseconds;
    peakProcessMilliseconds_ =
        std::max(peakProcessMilliseconds_, milliseconds);
}
