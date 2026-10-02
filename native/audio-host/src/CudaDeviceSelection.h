#pragma once

#include <cuda_runtime.h>

#include <algorithm>
#include <iostream>
#include <mutex>
#include <string>
#include <vector>

enum class CudaDeviceWorkload
{
    Throughput,
    DoublePrecision,
};

struct CudaDeviceSelection
{
    int index = -1;
    std::string name;
    int singleToDoublePrecisionPerfRatio = 0;
};

class CudaDeviceSelector final
{
public:
    static bool select(
        const char* errorPrefix,
        CudaDeviceSelection& selection,
        std::string& error,
        CudaDeviceWorkload workload = CudaDeviceWorkload::Throughput)
    {
        std::lock_guard<std::mutex> lock(selectionMutex_);
        const bool preferDoublePrecision =
            workload == CudaDeviceWorkload::DoublePrecision;
        auto& cachedSelection = preferDoublePrecision
            ? cachedDoublePrecisionSelection_
            : cachedThroughputSelection_;
        if (cachedSelection.index >= 0)
        {
            if (!activate(
                    cachedSelection.index,
                    errorPrefix,
                    error))
                return false;
            selection = cachedSelection;
            error.clear();
            return true;
        }

        int count = 0;
        const cudaError_t countStatus = cudaGetDeviceCount(&count);
        if (countStatus != cudaSuccess)
        {
            error = failure(
                errorPrefix,
                "device_count_failed",
                countStatus);
            return false;
        }

        long long bestScore = -1;
        CudaDeviceSelection best;
        for (int index = 0; index < count; ++index)
        {
            cudaDeviceProp properties {};
            int computeMode = cudaComputeModeProhibited;
            int multiprocessorCount = 0;
            int clockRate = 0;
            int singleToDoublePrecisionPerfRatio = 0;
            if (cudaGetDeviceProperties(&properties, index)
                    != cudaSuccess
                || cudaDeviceGetAttribute(
                       &computeMode,
                       cudaDevAttrComputeMode,
                       index)
                    != cudaSuccess
                || cudaDeviceGetAttribute(
                       &multiprocessorCount,
                       cudaDevAttrMultiProcessorCount,
                       index)
                    != cudaSuccess
                || cudaDeviceGetAttribute(
                       &clockRate,
                       cudaDevAttrClockRate,
                       index)
                    != cudaSuccess
                || computeMode == cudaComputeModeProhibited)
                continue;
            // This attribute is only an SDM admission hint. An older driver
            // that cannot report it must not make the device unavailable to
            // the independent CUDA FIR backend.
            if (cudaDeviceGetAttribute(
                    &singleToDoublePrecisionPerfRatio,
                    cudaDevAttrSingleToDoublePrecisionPerfRatio,
                    index) != cudaSuccess)
                singleToDoublePrecisionPerfRatio = 0;

            const long long score = preferDoublePrecision
                ? static_cast<long long>(clockRate) * 1'024ll
                    / static_cast<long long>(
                        std::max(1, singleToDoublePrecisionPerfRatio))
                : static_cast<long long>(multiprocessorCount)
                    * static_cast<long long>(clockRate);
            if (score > bestScore)
            {
                bestScore = score;
                best.index = index;
                best.name = properties.name;
                best.singleToDoublePrecisionPerfRatio =
                    singleToDoublePrecisionPerfRatio;
            }
        }
        if (best.index < 0)
        {
            error = std::string(errorPrefix)
                + "no_compatible_device";
            return false;
        }
        if (!activate(best.index, errorPrefix, error))
            return false;

        cachedSelection = best;
        selection = best;
        error.clear();
        return true;
    }

    static bool activate(
        int deviceIndex,
        const char* errorPrefix,
        std::string& error)
    {
        // Record on every activation, not only when cudaSetDevice() runs:
        // activeDevice_ is thread-local, so after a reset another thread that
        // still remembers the device would otherwise recreate the primary
        // context without it ever being tracked (and therefore never released).
        if (std::find(activatedDevices_.begin(), activatedDevices_.end(), deviceIndex)
            == activatedDevices_.end())
            activatedDevices_.push_back(deviceIndex);
        if (activeDevice_ == deviceIndex)
            return true;
        const cudaError_t status = cudaSetDevice(deviceIndex);
        if (status != cudaSuccess)
        {
            error = failure(
                errorPrefix,
                "set_device_failed",
                status);
            return false;
        }
        activeDevice_ = deviceIndex;
        return true;
    }

    // The primary context created by the first runtime call costs hundreds of
    // MB of host commit for the life of the process. Processors retain the
    // runtime while they hold CUDA resources; when the last one releases (or an
    // admission probe decided against CUDA), the contexts are torn down so
    // "upsampling off" also means "no CUDA in memory". The next CUDA user pays
    // one context creation again (~100 ms, off the audio callback).
    static void retainRuntime()
    {
        std::lock_guard<std::mutex> lock(selectionMutex_);
        ++runtimeUsers_;
    }

    static void releaseRuntime()
    {
        std::lock_guard<std::mutex> lock(selectionMutex_);
        if (runtimeUsers_ > 0)
            --runtimeUsers_;
        resetIfIdleLocked();
    }

    static void resetIfIdle()
    {
        std::lock_guard<std::mutex> lock(selectionMutex_);
        resetIfIdleLocked();
    }

private:
    static void resetIfIdleLocked()
    {
        if (runtimeUsers_ != 0 || activatedDevices_.empty())
            return;
        for (const int deviceIndex : activatedDevices_)
        {
            const cudaError_t setStatus = cudaSetDevice(deviceIndex);
            const cudaError_t resetStatus = setStatus == cudaSuccess ? cudaDeviceReset() : setStatus;
            std::cerr << "[echo-audio-host] CUDA runtime released: device=" << deviceIndex
                      << " status=" << cudaGetErrorString(resetStatus) << std::endl;
        }
        activatedDevices_.clear();
        activeDevice_ = -1;
    }

    static std::string failure(
        const char* prefix,
        const char* operation,
        cudaError_t status)
    {
        return std::string(prefix) + operation + ":"
            + cudaGetErrorString(status);
    }

    inline static std::mutex selectionMutex_;
    inline static CudaDeviceSelection cachedThroughputSelection_;
    inline static CudaDeviceSelection cachedDoublePrecisionSelection_;
    inline static int runtimeUsers_ = 0;
    inline static std::vector<int> activatedDevices_;
    inline static thread_local int activeDevice_ = -1;
};
