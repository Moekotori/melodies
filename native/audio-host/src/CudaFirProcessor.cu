#include "CudaFirProcessor.h"
#include "CudaDeviceSelection.h"

#include <cuda_runtime.h>

#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstddef>
#include <cstring>
#include <limits>
#include <utility>

namespace
{
constexpr float firNumericalZeroThreshold = 1.0e-12f;
constexpr int firBlockSize = 256;

__global__ __launch_bounds__(firBlockSize) void processPolyphaseFirKernel(
    const double* __restrict__ input,
    const double* __restrict__ history,
    const double* __restrict__ coefficients,
    const int* __restrict__ delays,
    const int* __restrict__ phaseOffsets,
    double* __restrict__ output,
    int inputFrames,
    int channels,
    int upsampleFactor,
    int historyFrames)
{
    const int sampleIndex = blockIdx.x * blockDim.x + threadIdx.x;
    const int outputFrames = inputFrames * upsampleFactor;
    const int outputSamples = outputFrames * channels;
    if (sampleIndex >= outputSamples)
        return;

    const int channel = sampleIndex % channels;
    const int outputFrame = sampleIndex / channels;
    const int inputFrame = outputFrame / upsampleFactor;
    const int phase = outputFrame % upsampleFactor;
    const int combinedFrame = historyFrames + inputFrame;
    double sum = 0.0f;

    for (int tap = phaseOffsets[phase]; tap < phaseOffsets[phase + 1]; ++tap)
    {
        const int sourceFrame = combinedFrame - delays[tap];
        const double source = sourceFrame < historyFrames
            ? history[sourceFrame * channels + channel]
            : input[(sourceFrame - historyFrames) * channels + channel];
        sum = __dadd_rn(sum, __dmul_rn(coefficients[tap], source));
    }

    output[sampleIndex] = sum;
}

std::string cudaFailure(const char* operation, cudaError_t status)
{
    return std::string("native_cuda_") + operation + ":" + cudaGetErrorString(status);
}

bool checked(cudaError_t status, const char* operation, std::string& error)
{
    if (status == cudaSuccess)
        return true;
    error = cudaFailure(operation, status);
    return false;
}

} // namespace

class CudaFirProcessor::Impl
{
public:
    struct DeviceStage
    {
        int upsampleFactor = 1;
        int historyFrames = 0;
        int tapEntryCount = 0;
        double* coefficients = nullptr;
        int* delays = nullptr;
        int* phaseOffsets = nullptr;
        double* history = nullptr;
        double* nextHistory = nullptr;
        double* hostHistory[2] { nullptr, nullptr };
    };

    ~Impl()
    {
        release();
    }

    // True once selectDevice() succeeded; every allocation happens after that.
    bool touchedRuntime() const noexcept
    {
        return deviceIndex >= 0 || stream != nullptr || !stages.empty()
            || bufferA != nullptr || bufferB != nullptr
            || hostInput != nullptr || hostOutput != nullptr;
    }

    void release()
    {
        // configure()/release() run on every playback configuration whether or
        // not CUDA DSP is enabled. Even cudaFree(nullptr) initialises the CUDA
        // runtime and creates the primary context (hundreds of MB of host
        // commit, nvcuda loaded for the life of the process), so skip the
        // runtime entirely when nothing was ever selected or allocated.
        if (touchedRuntime())
        {
            std::string cleanupError;
            if (!runtimeLease.activate("native_cuda_", cleanupError))
                std::cerr << "[echo-audio-host] CUDA FIR cleanup: " << cleanupError << std::endl;
            if (stream != nullptr)
                cudaStreamSynchronize(stream);
            for (auto& stage : stages)
            {
                cudaFree(stage.coefficients);
                cudaFree(stage.delays);
                cudaFree(stage.phaseOffsets);
                cudaFree(stage.history);
                cudaFree(stage.nextHistory);
                cudaFreeHost(stage.hostHistory[0]);
                cudaFreeHost(stage.hostHistory[1]);
            }
            cudaFree(bufferA);
            cudaFree(bufferB);
            cudaFreeHost(hostInput);
            cudaFreeHost(hostOutput);
            if (stream != nullptr)
                cudaStreamDestroy(stream);
            for (auto event : events) if (event) cudaEventDestroy(event);
        }
        runtimeLease.reset();
        stages.clear();
        bufferA = nullptr;
        bufferB = nullptr;
        hostInput = nullptr;
        hostOutput = nullptr;
        bufferCapacitySamples = 0;
        stream = nullptr;
        channels = 0;
        maximumOutputFactor = 1;
        deviceIndex = -1;
        selectedDeviceName.clear();
        committedHistoryIndex = 0;
        warmupMilliseconds = 0.0;
        configured = false;
        failBeforeCommit = false;
        timings = {};
        for (auto& event : events) event = nullptr;
    }

    bool selectDevice(std::string& error)
    {
        CudaDeviceSelection selection;
        if (!CudaDeviceSelector::select(
                "native_cuda_",
                selection,
                runtimeLease,
                error))
            return false;
        deviceIndex = selection.index;
        selectedDeviceName = std::move(selection.name);
        return true;
    }

    bool ensureCapacity(size_t requiredSamples, std::string& error)
    {
        if (requiredSamples <= bufferCapacitySamples)
            return true;
        if (requiredSamples > static_cast<size_t>(std::numeric_limits<int>::max()))
        {
            error = "native_cuda_fir_block_too_large";
            return false;
        }

        double* nextBufferA = nullptr;
        double* nextBufferB = nullptr;
        double* nextHostInput = nullptr;
        double* nextHostOutput = nullptr;
        const size_t bytes = requiredSamples * sizeof(double);
        if (!checked(cudaMalloc(&nextBufferA, bytes), "allocate_buffer_a_failed", error)
            || !checked(cudaMalloc(&nextBufferB, bytes), "allocate_buffer_b_failed", error)
            || !checked(cudaMallocHost(&nextHostInput, bytes), "allocate_pinned_input_failed", error)
            || !checked(cudaMallocHost(&nextHostOutput, bytes), "allocate_pinned_output_failed", error))
        {
            cudaFree(nextBufferA);
            cudaFree(nextBufferB);
            cudaFreeHost(nextHostInput);
            cudaFreeHost(nextHostOutput);
            return false;
        }

        cudaFree(bufferA);
        cudaFree(bufferB);
        cudaFreeHost(hostInput);
        cudaFreeHost(hostOutput);
        bufferA = nextBufferA;
        bufferB = nextBufferB;
        hostInput = nextHostInput;
        hostOutput = nextHostOutput;
        bufferCapacitySamples = requiredSamples;
        return true;
    }

    int channels = 0;
    int maximumOutputFactor = 1;
    int deviceIndex = -1;
    CudaDeviceSelector::RuntimeLease runtimeLease;
    cudaStream_t stream = nullptr;
    std::vector<DeviceStage> stages;
    double* bufferA = nullptr;
    double* bufferB = nullptr;
    double* hostInput = nullptr;
    double* hostOutput = nullptr;
    size_t bufferCapacitySamples = 0;
    std::string selectedDeviceName;
    int committedHistoryIndex = 0;
    double warmupMilliseconds = 0.0;
    bool configured = false;
    bool failBeforeCommit = false;
    cudaEvent_t events[4] {};
    CudaFirProcessor::Timings timings;
};

CudaFirProcessor::CudaFirProcessor() : impl_(std::make_unique<Impl>()) {}
CudaFirProcessor::~CudaFirProcessor() = default;
CudaFirProcessor::CudaFirProcessor(CudaFirProcessor&&) noexcept = default;
CudaFirProcessor& CudaFirProcessor::operator=(CudaFirProcessor&&) noexcept = default;

void CudaFirProcessor::release()
{
    impl_->release();
}

bool CudaFirProcessor::configure(
    int channels,
    const std::vector<echo::EchoSrcStageConfig>& stages,
    int maximumInputFrames,
    std::string& error)
{
    impl_->release();
    if (channels <= 0 || stages.empty() || maximumInputFrames <= 0)
    {
        error = "native_cuda_invalid_fir_configuration";
        return false;
    }
    if (!impl_->selectDevice(error))
    {
        impl_->release();
        return false;
    }
    if (!checked(cudaStreamCreateWithFlags(&impl_->stream, cudaStreamNonBlocking),
                 "stream_create_failed", error))
    {
        impl_->release();
        return false;
    }

    impl_->channels = channels;
    for (auto& event : impl_->events)
        if (!checked(cudaEventCreate(&event), "fir_event_create_failed", error))
        {
            impl_->release();
            return false;
        }
    impl_->maximumOutputFactor = 1;
    impl_->stages.reserve(stages.size());

    for (const auto& config : stages)
    {
        if (config.upsampleFactor != 1 && config.upsampleFactor != 2
            && config.upsampleFactor != 4 && config.upsampleFactor != 8)
        {
            error = "native_cuda_invalid_upsample_factor";
            impl_->release();
            return false;
        }
        if (config.taps.empty())
        {
            error = "native_cuda_empty_taps";
            impl_->release();
            return false;
        }
        if (impl_->maximumOutputFactor
            > std::numeric_limits<int>::max() / config.upsampleFactor)
        {
            error = "native_cuda_output_factor_overflow";
            impl_->release();
            return false;
        }
        impl_->maximumOutputFactor *= config.upsampleFactor;

        Impl::DeviceStage stage;
        stage.upsampleFactor = config.upsampleFactor;
        stage.historyFrames =
            static_cast<int>((config.taps.size() - 1) / static_cast<size_t>(config.upsampleFactor));

        std::vector<double> coefficients;
        std::vector<int> delays;
        std::vector<int> phaseOffsets(static_cast<size_t>(config.upsampleFactor + 1), 0);
        const double interpolationGain = static_cast<double>(config.upsampleFactor);
        for (int phase = 0; phase < config.upsampleFactor; ++phase)
        {
            phaseOffsets[static_cast<size_t>(phase)] = static_cast<int>(coefficients.size());
            for (size_t tap = static_cast<size_t>(phase);
                 tap < config.taps.size();
                 tap += static_cast<size_t>(config.upsampleFactor))
            {
                const double coefficient = config.taps[tap];
                if (std::abs(coefficient) <= firNumericalZeroThreshold)
                    continue;
                coefficients.push_back(coefficient * interpolationGain);
                delays.push_back(static_cast<int>(
                    tap / static_cast<size_t>(config.upsampleFactor)));
            }
        }
        phaseOffsets.back() = static_cast<int>(coefficients.size());
        stage.tapEntryCount = static_cast<int>(coefficients.size());

        const size_t coefficientBytes = std::max<size_t>(1, coefficients.size()) * sizeof(double);
        const size_t delayBytes = std::max<size_t>(1, delays.size()) * sizeof(int);
        const size_t offsetBytes = phaseOffsets.size() * sizeof(int);
        const size_t historySamples =
            static_cast<size_t>(stage.historyFrames) * static_cast<size_t>(channels);
        const size_t historyBytes = std::max<size_t>(1, historySamples) * sizeof(double);
        impl_->timings.deviceBytes += coefficientBytes + delayBytes + offsetBytes + historyBytes * 2;
        impl_->timings.pinnedBytes += historySamples > 0 ? historyBytes * 2 : 0;

        if (!checked(cudaMalloc(&stage.coefficients, coefficientBytes),
                     "allocate_coefficients_failed", error)
            || !checked(cudaMalloc(&stage.delays, delayBytes),
                        "allocate_delays_failed", error)
            || !checked(cudaMalloc(&stage.phaseOffsets, offsetBytes),
                        "allocate_phase_offsets_failed", error)
            || !checked(cudaMalloc(&stage.history, historyBytes),
                        "allocate_history_failed", error)
            || !checked(cudaMalloc(&stage.nextHistory, historyBytes),
                        "allocate_next_history_failed", error)
            || (historySamples > 0
                && (!checked(cudaMallocHost(&stage.hostHistory[0], historyBytes),
                             "allocate_history_snapshot_a_failed", error)
                    || !checked(cudaMallocHost(&stage.hostHistory[1], historyBytes),
                                "allocate_history_snapshot_b_failed", error))))
        {
            cudaFree(stage.coefficients);
            cudaFree(stage.delays);
            cudaFree(stage.phaseOffsets);
            cudaFree(stage.history);
            cudaFree(stage.nextHistory);
            cudaFreeHost(stage.hostHistory[0]);
            cudaFreeHost(stage.hostHistory[1]);
            impl_->release();
            return false;
        }
        if (historySamples > 0)
        {
            std::memset(stage.hostHistory[0], 0, historyBytes);
            std::memset(stage.hostHistory[1], 0, historyBytes);
        }
        if ((!coefficients.empty()
             && !checked(cudaMemcpy(stage.coefficients, coefficients.data(),
                                    coefficients.size() * sizeof(double), cudaMemcpyHostToDevice),
                         "copy_coefficients_failed", error))
            || (!delays.empty()
                && !checked(cudaMemcpy(stage.delays, delays.data(),
                                       delays.size() * sizeof(int), cudaMemcpyHostToDevice),
                            "copy_delays_failed", error))
            || !checked(cudaMemcpy(stage.phaseOffsets, phaseOffsets.data(),
                                   offsetBytes, cudaMemcpyHostToDevice),
                        "copy_phase_offsets_failed", error)
            || !checked(cudaMemset(stage.history, 0, historyBytes),
                        "clear_history_failed", error)
            || !checked(cudaMemset(stage.nextHistory, 0, historyBytes),
                        "clear_next_history_failed", error))
        {
            cudaFree(stage.coefficients);
            cudaFree(stage.delays);
            cudaFree(stage.phaseOffsets);
            cudaFree(stage.history);
            cudaFree(stage.nextHistory);
            cudaFreeHost(stage.hostHistory[0]);
            cudaFreeHost(stage.hostHistory[1]);
            impl_->release();
            return false;
        }
        impl_->stages.push_back(stage);
    }

    const size_t maximumSamples =
        static_cast<size_t>(maximumInputFrames)
        * static_cast<size_t>(channels)
        * static_cast<size_t>(impl_->maximumOutputFactor);
    if (!impl_->ensureCapacity(maximumSamples, error))
    {
        impl_->release();
        return false;
    }
    impl_->timings.deviceBytes += maximumSamples * sizeof(double) * 2;
    impl_->timings.pinnedBytes += maximumSamples * sizeof(double) * 2;

    impl_->configured = true;
    const int warmupFrames = std::min(maximumInputFrames, 64);
    std::vector<double> warmupInput(
        static_cast<size_t>(warmupFrames) * static_cast<size_t>(channels),
        0.0f);
    std::vector<double> warmupOutput;
    const auto warmupStarted = std::chrono::steady_clock::now();
    if (!processDouble(warmupInput.data(), warmupFrames, warmupOutput, error))
    {
        error = error.empty()
            ? "native_cuda_fir_warmup_failed"
            : "native_cuda_fir_warmup_failed:" + error;
        impl_->release();
        return false;
    }
    const auto warmupFinished = std::chrono::steady_clock::now();
    const double warmupMilliseconds =
        std::chrono::duration<double, std::milli>(warmupFinished - warmupStarted).count();
    if (!reset(error))
    {
        impl_->release();
        return false;
    }
    impl_->warmupMilliseconds = warmupMilliseconds;
    error.clear();
    return true;
}

void CudaFirProcessor::reset()
{
    std::string error;
    (void)reset(error);
}

bool CudaFirProcessor::reset(std::string& error)
{
    if (!impl_->configured || impl_->deviceIndex < 0)
    {
        error = "native_cuda_fir_not_configured";
        return false;
    }
    if (!impl_->runtimeLease.activate("native_cuda_", error)) return false;
    for (auto& stage : impl_->stages)
    {
        const size_t historySamples =
            static_cast<size_t>(stage.historyFrames)
            * static_cast<size_t>(impl_->channels);
        const size_t historyBytes =
            std::max<size_t>(1, historySamples)
            * sizeof(double);
        if (!checked(cudaMemsetAsync(stage.history, 0, historyBytes, impl_->stream), "reset_history_failed", error)
            || !checked(cudaMemsetAsync(stage.nextHistory, 0, historyBytes, impl_->stream), "reset_next_history_failed", error))
            return false;
    }
    if (!checked(cudaStreamSynchronize(impl_->stream), "reset_stream_sync_failed", error)) return false;
    for (auto& stage : impl_->stages)
    {
        const size_t historySamples = static_cast<size_t>(stage.historyFrames) * static_cast<size_t>(impl_->channels);
        const size_t historyBytes = historySamples * sizeof(double);
        if (historySamples > 0)
        {
            std::memset(stage.hostHistory[0], 0, historyBytes);
            std::memset(stage.hostHistory[1], 0, historyBytes);
        }
    }
    impl_->committedHistoryIndex = 0;
    error.clear();
    return true;
}

bool CudaFirProcessor::processDouble(
    const double* interleaved,
    int frames,
    std::vector<double>& output,
    std::string& error)
{
    output.clear();
    if (!impl_->configured)
    {
        error = "native_cuda_fir_not_configured";
        return false;
    }
    if (interleaved == nullptr || frames <= 0)
    {
        if (frames == 0)
        {
            error.clear();
            return true;
        }
        error = "native_cuda_invalid_fir_input";
        return false;
    }
    if (!impl_->runtimeLease.activate(
            "native_cuda_",
            error))
        return false;

    const size_t inputSamples =
        static_cast<size_t>(frames) * static_cast<size_t>(impl_->channels);
    const size_t outputSamples =
        inputSamples * static_cast<size_t>(impl_->maximumOutputFactor);
    // Capacity belongs to configuration, never to a processing block.
    if (outputSamples > impl_->bufferCapacitySamples)
    {
        error = "native_cuda_fir_block_too_large";
        return false;
    }

    std::memcpy(impl_->hostInput, interleaved, inputSamples * sizeof(double));
    if (!checked(cudaEventRecord(impl_->events[0], impl_->stream), "fir_event_start_failed", error)) return false;
    if (!checked(cudaMemcpyAsync(
                     impl_->bufferA,
                     impl_->hostInput,
                     inputSamples * sizeof(double),
                     cudaMemcpyHostToDevice,
                     impl_->stream),
                 "copy_input_failed", error))
        return false;

    if (!checked(cudaEventRecord(impl_->events[1], impl_->stream), "fir_event_h2d_failed", error)) return false;
    double* input = impl_->bufferA;
    double* nextOutput = impl_->bufferB;
    int inputFrames = frames;
    const int pendingHistoryIndex = 1 - impl_->committedHistoryIndex;
    for (auto& stage : impl_->stages)
    {
        const int outputFrames = inputFrames * stage.upsampleFactor;
        const int stageOutputSamples = outputFrames * impl_->channels;
        const int outputBlocks = (stageOutputSamples + firBlockSize - 1) / firBlockSize;
        processPolyphaseFirKernel<<<outputBlocks, firBlockSize, 0, impl_->stream>>>(
            input,
            stage.history,
            stage.coefficients,
            stage.delays,
            stage.phaseOffsets,
            nextOutput,
            inputFrames,
            impl_->channels,
            stage.upsampleFactor,
            stage.historyFrames);
        if (!checked(cudaPeekAtLastError(), "fir_kernel_launch_failed", error))
            return false;

        const int historySamples = stage.historyFrames * impl_->channels;
        if (historySamples > 0)
        {
            const size_t frameBytes =
                static_cast<size_t>(impl_->channels) * sizeof(double);
            if (inputFrames >= stage.historyFrames)
            {
                const double* historyInput =
                    input
                    + static_cast<size_t>(
                        inputFrames - stage.historyFrames)
                        * static_cast<size_t>(impl_->channels);
                if (!checked(cudaMemcpyAsync(
                                 stage.nextHistory,
                                 historyInput,
                                 static_cast<size_t>(stage.historyFrames)
                                     * frameBytes,
                                 cudaMemcpyDeviceToDevice,
                                 impl_->stream),
                             "copy_history_tail_failed",
                             error))
                    return false;
            }
            else
            {
                const int retainedFrames =
                    stage.historyFrames - inputFrames;
                const size_t retainedBytes =
                    static_cast<size_t>(retainedFrames) * frameBytes;
                const size_t inputBytes =
                    static_cast<size_t>(inputFrames) * frameBytes;
                if (!checked(cudaMemcpyAsync(
                                 stage.nextHistory,
                                 stage.history
                                     + static_cast<size_t>(inputFrames)
                                         * static_cast<size_t>(
                                             impl_->channels),
                                 retainedBytes,
                                 cudaMemcpyDeviceToDevice,
                                 impl_->stream),
                             "copy_retained_history_failed",
                             error)
                    || !checked(cudaMemcpyAsync(
                                    stage.nextHistory
                                        + static_cast<size_t>(
                                            retainedFrames)
                                            * static_cast<size_t>(
                                                impl_->channels),
                                    input,
                                    inputBytes,
                                    cudaMemcpyDeviceToDevice,
                                    impl_->stream),
                                "append_history_input_failed",
                                error))
                    return false;
            }
            std::swap(stage.history, stage.nextHistory);
        }

        std::swap(input, nextOutput);
        inputFrames = outputFrames;
    }

    if (!checked(cudaEventRecord(impl_->events[2], impl_->stream), "fir_event_kernel_failed", error)) return false;
    for (const auto& stage : impl_->stages)
    {
        const size_t historySamples =
            static_cast<size_t>(stage.historyFrames)
            * static_cast<size_t>(impl_->channels);
        if (historySamples > 0
            && !checked(cudaMemcpyAsync(
                            stage.hostHistory[pendingHistoryIndex],
                            stage.history,
                            historySamples * sizeof(double),
                            cudaMemcpyDeviceToHost,
                            impl_->stream),
                        "copy_history_snapshot_failed", error))
            return false;
    }

    if (!checked(cudaMemcpyAsync(
                     impl_->hostOutput,
                     input,
                     outputSamples * sizeof(double),
                     cudaMemcpyDeviceToHost,
                     impl_->stream),
                 "copy_output_failed", error)
        || !checked(cudaEventRecord(impl_->events[3], impl_->stream), "fir_event_d2h_failed", error))
        return false;
    const auto synchronizeStarted = std::chrono::steady_clock::now();
    if (!checked(cudaStreamSynchronize(impl_->stream), "stream_sync_failed", error)) return false;
    impl_->timings.synchronizeMs = std::chrono::duration<double, std::milli>(
        std::chrono::steady_clock::now() - synchronizeStarted).count();
    float elapsed[3] {};
    for (int index = 0; index < 3; ++index)
        if (!checked(cudaEventElapsedTime(&elapsed[index], impl_->events[index], impl_->events[index + 1]),
                     "fir_event_elapsed_failed", error)) return false;
    impl_->timings.h2dMs = elapsed[0];
    impl_->timings.firMs = elapsed[1];
    impl_->timings.d2hMs = elapsed[2];

    if (impl_->failBeforeCommit)
    {
        impl_->failBeforeCommit = false;
        error = "native_cuda_fir_injected_before_commit";
        return false;
    }
    const auto commitStarted = std::chrono::steady_clock::now();
    output.assign(impl_->hostOutput, impl_->hostOutput + outputSamples);
    impl_->committedHistoryIndex = pendingHistoryIndex;
    impl_->timings.commitMs = std::chrono::duration<double, std::milli>(
        std::chrono::steady_clock::now() - commitStarted).count();
    error.clear();
    return true;
}

bool CudaFirProcessor::copyLatestHistory(
    std::vector<std::vector<double>>& histories) const
{
    histories.clear();
    if (!impl_->configured)
        return false;
    histories.reserve(impl_->stages.size());
    for (const auto& stage : impl_->stages)
    {
        const size_t historySamples =
            static_cast<size_t>(stage.historyFrames)
            * static_cast<size_t>(impl_->channels);
        if (historySamples == 0)
        {
            histories.emplace_back();
            continue;
        }
        const double* snapshot = stage.hostHistory[impl_->committedHistoryIndex];
        if (snapshot == nullptr)
        {
            histories.clear();
            return false;
        }
        histories.emplace_back(snapshot, snapshot + historySamples);
    }
    return true;
}

bool CudaFirProcessor::restoreHistory(
    const std::vector<std::vector<double>>& histories,
    std::string& error)
{
    if (!impl_->configured || histories.size() != impl_->stages.size())
    {
        error = "native_cuda_fir_history_shape_mismatch";
        return false;
    }
    if (!impl_->runtimeLease.activate(
            "native_cuda_",
            error))
        return false;

    for (size_t index = 0; index < impl_->stages.size(); ++index)
        if (histories[index].size() != static_cast<size_t>(impl_->stages[index].historyFrames) * impl_->channels)
        {
            error = "native_cuda_fir_history_shape_mismatch";
            return false;
        }
    for (size_t index = 0; index < impl_->stages.size(); ++index)
    {
        auto& stage = impl_->stages[index];
        const size_t historySamples =
            static_cast<size_t>(stage.historyFrames)
            * static_cast<size_t>(impl_->channels);
        if (histories[index].size() != historySamples)
        {
            error = "native_cuda_fir_history_shape_mismatch";
            return false;
        }
        if (historySamples == 0)
            continue;

        const size_t historyBytes = historySamples * sizeof(double);
        if (!checked(cudaMemcpyAsync(
                         stage.history,
                         histories[index].data(),
                         historyBytes,
                         cudaMemcpyHostToDevice,
                         impl_->stream),
                     "restore_history_failed",
                     error)
            || !checked(cudaMemcpyAsync(
                            stage.nextHistory,
                            histories[index].data(),
                            historyBytes,
                            cudaMemcpyHostToDevice,
                            impl_->stream),
                        "restore_next_history_failed",
                        error))
            return false;
    }
    if (!checked(
            cudaStreamSynchronize(impl_->stream),
            "restore_history_sync_failed",
            error))
        return false;
    for (size_t index = 0; index < impl_->stages.size(); ++index)
        if (!histories[index].empty())
            for (auto* snapshot : impl_->stages[index].hostHistory)
                std::memcpy(snapshot, histories[index].data(), histories[index].size() * sizeof(double));
    impl_->committedHistoryIndex = 0;
    error.clear();
    return true;
}

bool CudaFirProcessor::active() const noexcept
{
    return impl_->configured;
}

std::string CudaFirProcessor::deviceName() const
{
    return impl_->selectedDeviceName;
}

double CudaFirProcessor::warmupMilliseconds() const noexcept
{
    return impl_->warmupMilliseconds;
}

bool CudaFirProcessor::builtWithCuda() noexcept
{
    return true;
}

void CudaFirProcessor::failNextBlockForTesting() { impl_->failBeforeCommit = true; }
CudaFirProcessor::Timings CudaFirProcessor::lastTimings() const noexcept { return impl_->timings; }

CudaFirProcessor::RuntimeHold::RuntimeHold()
{
    CudaDeviceSelector::retainRuntime();
}

CudaFirProcessor::RuntimeHold::~RuntimeHold()
{
    CudaDeviceSelector::releaseRuntime();
}

// Compatibility surface: float callers are promoted before the FP64 engine.
// The production double route never passes through these conversions.
bool CudaFirProcessor::process(const float* input, int frames,
    std::vector<float>& output, std::string& error)
{
    std::vector<double> promoted;
    if (input && frames > 0)
        promoted.assign(input, input + static_cast<size_t>(frames) * impl_->channels);
    std::vector<double> result;
    const bool success = processDouble(promoted.empty() ? nullptr : promoted.data(), frames, result, error);
    output.assign(result.begin(), result.end());
    return success;
}
bool CudaFirProcessor::copyLatestHistory(std::vector<std::vector<float>>& histories) const
{
    std::vector<std::vector<double>> source;
    histories.clear();
    if (!copyLatestHistory(source)) return false;
    for (const auto& history : source) histories.emplace_back(history.begin(), history.end());
    return true;
}
bool CudaFirProcessor::restoreHistory(const std::vector<std::vector<float>>& histories, std::string& error)
{
    std::vector<std::vector<double>> promoted;
    for (const auto& history : histories) promoted.emplace_back(history.begin(), history.end());
    return restoreHistory(promoted, error);
}
