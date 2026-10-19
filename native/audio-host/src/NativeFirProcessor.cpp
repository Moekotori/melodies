#include "NativeFirProcessor.h"
#if defined(ECHO_SRC_FIR_EXPERIMENTAL_AVX2) && ECHO_SRC_FIR_EXPERIMENTAL_AVX2 \
    && (defined(_M_X64) || defined(__x86_64__))
#include "../../audio-engine/EchoSrcFirAvx2.h"
#endif

#include <algorithm>
#include <chrono>
#include <cmath>
#include <iomanip>
#include <limits>
#include <sstream>
#include <utility>
#include <type_traits>

const char* NativeFirProcessor::cpuKernelName() const noexcept
{
#if defined(ECHO_SRC_FIR_EXPERIMENTAL_AVX2) && ECHO_SRC_FIR_EXPERIMENTAL_AVX2 \
    && (defined(_M_X64) || defined(__x86_64__))
    if (echo::firKernel::avx2Available()) return "avx2-fp64";
#endif
#if defined(_M_X64) || defined(__SSE2__)
    return "sse2-fp64";
#else
    return "scalar-fp64";
#endif
}

bool NativeFirProcessor::configure(
    int channels,
    const std::vector<echo::EchoSrcStageConfig>& stages,
    bool requestCuda,
    int maximumInputFrames,
    std::string& error)
{
    // Spans cuda_.configure()'s internal release and the admission benchmark,
    // so an active CUDA processor keeps its context across per-track
    // reconfiguration; the context is dropped only when CUDA is not chosen.
    const CudaFirProcessor::RuntimeHold runtimeHold;
    echo::EchoSrcProcessor nextCpu;
    if (!nextCpu.configure(channels, stages, error))
        return false;

    cpu_ = std::move(nextCpu);
    channels_ = std::max(1, channels);
    maximumInputFrames_ = std::max(1, maximumInputFrames);
    outputFactor_ = 1;
    size_t impulseEndFrame = 0;
    double nominalLatencyFrames = 0.0;
    for (const auto& stage : stages)
    {
        const auto factor = static_cast<size_t>(stage.upsampleFactor);
        impulseEndFrame =
            impulseEndFrame * factor + (stage.taps.size() - 1);
        outputFactor_ *= stage.upsampleFactor;

        bool symmetric = true;
        float peakMagnitude = 0.0f;
        size_t peakIndex = 0;
        for (size_t tap = 0; tap < stage.taps.size(); ++tap)
        {
            const float magnitude = std::abs(stage.taps[tap]);
            if (magnitude > peakMagnitude)
            {
                peakMagnitude = magnitude;
                peakIndex = tap;
            }
            const size_t mirrored = stage.taps.size() - 1 - tap;
            const float tolerance = std::max(
                1.0e-7f,
                std::max(
                    std::abs(stage.taps[tap]),
                    std::abs(stage.taps[mirrored]))
                    * 1.0e-5f);
            if (std::abs(
                    stage.taps[tap]
                    - stage.taps[mirrored])
                > tolerance)
                symmetric = false;
        }
        const double stageLatency = symmetric
            ? static_cast<double>(stage.taps.size() - 1) * 0.5
            : static_cast<double>(peakIndex);
        nominalLatencyFrames =
            nominalLatencyFrames
                * static_cast<double>(stage.upsampleFactor)
            + stageLatency;
    }
    const size_t emittedSupportFrames =
        static_cast<size_t>(outputFactor_) - 1;
    tailOutputFrames_ = stages.empty()
        || impulseEndFrame <= emittedSupportFrames
        ? 0
        : impulseEndFrame - emittedSupportFrames;
    prerollInputFrames_ = static_cast<int>(
        (tailOutputFrames_ + static_cast<size_t>(outputFactor_) - 1)
        / static_cast<size_t>(outputFactor_));
    nominalLatencyOutputFrames_ = static_cast<int>(
        std::llround(nominalLatencyFrames));
    backend_ = Backend::Cpu;
    fallbackReason_.clear();
    deviceName_.clear();
    warmupMilliseconds_ = 0.0;
    // Drop CUDA resources from a previous configuration (e.g. upsampling just
    // turned off); a no-op unless this processor ever selected a device.
    cuda_.release();

    if (requestCuda && !stages.empty())
    {
        std::string cudaError;
        if (cuda_.configure(channels, stages, maximumInputFrames, cudaError))
        {
            const int admissionFrames =
                std::max(1, maximumInputFrames);
            std::vector<double> admissionInput(
                static_cast<size_t>(admissionFrames * channels_),
                0.0f);
            for (int frame = 0; frame < admissionFrames; ++frame)
            {
                for (int channel = 0; channel < channels_; ++channel)
                {
                    const double phase =
                        static_cast<double>(frame)
                        * (0.037 + static_cast<double>(channel) * 0.011);
                    admissionInput[
                        static_cast<size_t>(frame * channels_ + channel)] =
                        0.31 * std::sin(phase);
                }
            }

            double cpuMilliseconds =
                std::numeric_limits<double>::infinity();
            double cudaMilliseconds =
                std::numeric_limits<double>::infinity();
            bool cudaAdmissionCompleted = true;
            std::vector<double> cudaOutput;
            for (int iteration = 0; iteration < 4; ++iteration)
            {
                const auto cpuStarted =
                    std::chrono::steady_clock::now();
                const auto cpuOutput = cpu_.processDouble(
                    admissionInput.data(), admissionFrames);
                cpuMilliseconds = std::min(
                    cpuMilliseconds,
                    std::chrono::duration<double, std::milli>(
                        std::chrono::steady_clock::now()
                        - cpuStarted).count());

                const auto cudaStarted =
                    std::chrono::steady_clock::now();
                cudaAdmissionCompleted = cuda_.processDouble(
                    admissionInput.data(),
                    admissionFrames,
                    cudaOutput,
                    cudaError);
                cudaMilliseconds = std::min(
                    cudaMilliseconds,
                    std::chrono::duration<double, std::milli>(
                        std::chrono::steady_clock::now()
                        - cudaStarted).count());
                if (!cudaAdmissionCompleted) break;
                if (cudaOutput != cpuOutput)
                {
                    cudaAdmissionCompleted = false;
                    cudaError = "native_cuda_fir_fp64_output_mismatch";
                    break;
                }
            }
            cpu_.reset();
            std::string resetError;
            if (!cuda_.reset(resetError))
            {
                cudaAdmissionCompleted = false;
                cudaError = std::move(resetError);
            }

            if (cudaAdmissionCompleted
                && cudaMilliseconds < cpuMilliseconds * 0.9)
            {
                backend_ = Backend::Cuda;
                deviceName_ = cuda_.deviceName();
                warmupMilliseconds_ = cuda_.warmupMilliseconds();
            }
            else
            {
                if (!cudaAdmissionCompleted)
                {
                    fallbackReason_ = cudaError.empty()
                        ? "native_cuda_fir_admission_failed"
                        : std::move(cudaError);
                }
                else
                {
                    std::ostringstream reason;
                    reason << std::fixed << std::setprecision(3)
                           << "native_cuda_fir_not_faster:cpu="
                           << cpuMilliseconds
                           << "ms,cuda=" << cudaMilliseconds << "ms";
                    fallbackReason_ = reason.str();
                }
                cuda_.release();
            }
        }
        else
        {
            fallbackReason_ = cudaError.empty()
                ? "native_cuda_dsp_initialization_failed"
                : std::move(cudaError);
        }
    }

    reset();
    error.clear();
    return true;
}

void NativeFirProcessor::reset()
{
    cpu_.reset();
    processingError_.clear();
    if (backend_ == Backend::Cuda)
    {
        std::string error;
        if (!cuda_.reset(error))
        {
            backend_ = Backend::Cpu;
            fallbackReason_ = "native_cuda_fir_reset_failed:" + error;
            deviceName_.clear();
            cuda_.release();
        }
    }
    flushPending_ = active();
    processedBlocks_ = 0;
    lastInputFrames_ = 0;
    lastOutputFrames_ = 0;
    lastProcessMilliseconds_ = 0.0;
    totalProcessMilliseconds_ = 0.0;
    peakProcessMilliseconds_ = 0.0;
    runtimeFallbacks_ = 0;
    cudaProcessedBlocks_ = 0;
}

template <typename Sample>
std::vector<double> NativeFirProcessor::processDouble(const Sample* interleaved, int frames)
{
    if (!processingError_.empty()) return {};
    if (interleaved && frames > 0) flushPending_ = active();
    const auto start = std::chrono::steady_clock::now();
    std::vector<double> output;
    if (backend_ == Backend::Cuda)
    {
        // float decoder input is promoted once; double DSP input remains double.
        std::vector<double> promoted;
        const double* input = nullptr;
        if constexpr (std::is_same_v<Sample, double>) input = interleaved;
        else if (interleaved && frames > 0)
        {
            promoted.assign(interleaved, interleaved + static_cast<size_t>(frames) * channels_);
            input = promoted.data();
        }
        std::string error;
        if (!cuda_.processDouble(input, frames, output, error))
        {
            if (!fallBackToCpu(error)) return {};
            output = cpu_.processDouble(interleaved, frames);
        }
        else if (frames > 0) ++cudaProcessedBlocks_;
    }
    else output = cpu_.processDouble(interleaved, frames);
    recordProcess(frames, output.size(), std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - start).count());
    return output;
}

std::vector<double> NativeFirProcessor::flushDouble()
{
    if (!active() || !flushPending_ || tailOutputFrames_ == 0) { flushPending_ = false; return {}; }
    std::vector<double> output;
    output.reserve(static_cast<size_t>(prerollInputFrames_) * outputFactor_ * channels_);
    std::vector<double> silence(static_cast<size_t>(maximumInputFrames_) * channels_, 0);
    size_t remaining = static_cast<size_t>(prerollInputFrames_);
    while (remaining > 0) {
        const int frames = static_cast<int>(std::min(remaining, static_cast<size_t>(maximumInputFrames_)));
        auto block = processDouble(silence.data(), frames);
        if (!processingError_.empty()) return {};
        output.insert(output.end(), block.begin(), block.end()); remaining -= frames;
    }
    flushPending_ = false;
    output.resize(std::min(output.size(), tailOutputFrames_ * static_cast<size_t>(channels_)));
    return output;
}

std::vector<float> NativeFirProcessor::process(const float* interleaved, int frames)
{
    if (!processingError_.empty()) return {};
    if (interleaved != nullptr && frames > 0)
        flushPending_ = active();

    const auto started = std::chrono::steady_clock::now();
    if (backend_ != Backend::Cuda)
    {
        auto output = cpu_.process(interleaved, frames);
        const auto finished = std::chrono::steady_clock::now();
        recordProcess(
            frames,
            output.size(),
            std::chrono::duration<double, std::milli>(finished - started).count());
        return output;
    }

    std::vector<float> output;
    std::string cudaError;
    if (cuda_.process(interleaved, frames, output, cudaError))
    {
        if (frames > 0) ++cudaProcessedBlocks_;
        const auto finished = std::chrono::steady_clock::now();
        recordProcess(
            frames,
            output.size(),
            std::chrono::duration<double, std::milli>(finished - started).count());
        return output;
    }

    if (!fallBackToCpu(cudaError)) return {};
    output = cpu_.process(interleaved, frames);
    const auto finished = std::chrono::steady_clock::now();
    recordProcess(
        frames,
        output.size(),
        std::chrono::duration<double, std::milli>(finished - started).count());
    return output;
}

std::vector<float> NativeFirProcessor::flush()
{
    if (!active() || !flushPending_ || tailOutputFrames_ == 0)
    {
        flushPending_ = false;
        return {};
    }

    const size_t requiredInputFrames =
        static_cast<size_t>(prerollInputFrames_);
    std::vector<float> output;
    output.reserve(
        requiredInputFrames
        * static_cast<size_t>(outputFactor_)
        * static_cast<size_t>(channels_));
    std::vector<float> silence(
        static_cast<size_t>(maximumInputFrames_)
            * static_cast<size_t>(channels_),
        0.0f);

    size_t remaining = requiredInputFrames;
    while (remaining > 0)
    {
        const int framesThisBlock = static_cast<int>(std::min(
            remaining,
            static_cast<size_t>(maximumInputFrames_)));
        auto block = process(silence.data(), framesThisBlock);
        if (!processingError_.empty()) return {};
        output.insert(output.end(), block.begin(), block.end());
        remaining -= static_cast<size_t>(framesThisBlock);
    }

    output.resize(
        tailOutputFrames_ * static_cast<size_t>(channels_));
    flushPending_ = false;
    return output;
}

bool NativeFirProcessor::copyLatestHistory(
    std::vector<std::vector<float>>& histories) const
{
    if (backend_ == Backend::Cuda)
        return cuda_.copyLatestHistory(histories);
    cpu_.copyHistory(histories);
    return true;
}

bool NativeFirProcessor::restoreHistory(
    const std::vector<std::vector<float>>& histories)
{
    if (!cpu_.restoreHistory(histories))
        return false;
    if (backend_ == Backend::Cuda)
    {
        std::string error;
        if (!cuda_.restoreHistory(histories, error))
        {
            backend_ = Backend::Cpu;
            ++runtimeFallbacks_;
            fallbackReason_ = error.empty()
                ? "native_cuda_fir_history_restore_failed"
                : "native_cuda_fir_history_restore_failed:" + error;
            deviceName_.clear();
            cuda_.release();
        }
    }
    flushPending_ = active();
    return true;
}

void NativeFirProcessor::recordProcess(
    int inputFrames,
    size_t outputSamples,
    double milliseconds)
{
    ++processedBlocks_;
    lastInputFrames_ = std::max(0, inputFrames);
    lastOutputFrames_ = static_cast<int>(
        outputSamples / static_cast<size_t>(std::max(1, channels_)));
    lastProcessMilliseconds_ = milliseconds;
    totalProcessMilliseconds_ += milliseconds;
    peakProcessMilliseconds_ = std::max(peakProcessMilliseconds_, milliseconds);
}

template std::vector<double> NativeFirProcessor::processDouble<float>(const float*, int);
template std::vector<double> NativeFirProcessor::processDouble<double>(const double*, int);

bool NativeFirProcessor::copyLatestHistory(std::vector<std::vector<double>>& histories) const
{
    if (backend_ == Backend::Cuda) return cuda_.copyLatestHistory(histories);
    cpu_.copyHistory(histories);
    return true;
}
bool NativeFirProcessor::restoreHistory(const std::vector<std::vector<double>>& histories)
{
    if (!cpu_.restoreHistory(histories)) return false;
    if (backend_ == Backend::Cuda)
    {
        std::string error;
        if (!cuda_.restoreHistory(histories, error))
        {
            backend_ = Backend::Cpu;
            ++runtimeFallbacks_;
            fallbackReason_ = "native_cuda_fir_history_restore_failed:" + error;
            deviceName_.clear();
            cuda_.release();
        }
    }
    flushPending_ = active();
    return true;
}

bool NativeFirProcessor::fallBackToCpu(const std::string& error)
{
    // Host snapshots advance only after the entire GPU block and D2H finish.
    // Discard every pending GPU result, restore that boundary, replay once.
    std::vector<std::vector<double>> histories;
    const bool restored = cuda_.copyLatestHistory(histories) && cpu_.restoreHistory(histories);
    backend_ = Backend::Cpu;
    ++runtimeFallbacks_;
    fallbackReason_ = "native_cuda_fir_runtime_failure:" + error;
    deviceName_.clear();
    cuda_.release();
    if (!restored)
    {
        processingError_ = fallbackReason_ + ":history_restore_failed";
        return false;
    }
    return true;
}
