#pragma once

#include <cuda_runtime.h>
#include "CudaRuntimeLeaseRegistry.h"

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

struct EchoCudaRuntimeApi final
{
    static bool setDevice(int device, const char* prefix, std::string& error)
    {
        const auto status = cudaSetDevice(device);
        if (status == cudaSuccess) return true;
        error = std::string(prefix) + "set_device_failed:" + cudaGetErrorString(status);
        return false;
    }
    static void resetDevice(int device) noexcept
    {
        const auto selected = cudaSetDevice(device);
        const auto status = selected == cudaSuccess ? cudaDeviceReset() : selected;
        std::cerr << "[echo-audio-host] CUDA runtime reset: device=" << device
                  << " status=" << cudaGetErrorString(status) << std::endl;
    }
};

class CudaDeviceSelector final
{
public:
    using RuntimeLease = CudaRuntimeLeaseRegistry<EchoCudaRuntimeApi>::Lease;
    static bool select(
        const char* errorPrefix,
        CudaDeviceSelection& selection,
        RuntimeLease& lease,
        std::string& error,
        CudaDeviceWorkload workload = CudaDeviceWorkload::Throughput)
    {
        // Retain before CUDA enumeration as well as device activation. Another
        // processor's last release cannot reset a context during this probe.
        ProbeHold probe;
        std::lock_guard<std::mutex> lock(selectionMutex_);
        const bool preferDoublePrecision =
            workload == CudaDeviceWorkload::DoublePrecision;
        auto& cachedSelection = preferDoublePrecision
            ? cachedDoublePrecisionSelection_
            : cachedThroughputSelection_;
        if (cachedSelection.index >= 0)
        {
            if (!acquireSelected(
                    cachedSelection.index,
                    lease,
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
        if (!acquireSelected(best.index, lease, errorPrefix, error))
            return false;

        cachedSelection = best;
        selection = best;
        error.clear();
        return true;
    }

    // The primary context created by the first runtime call costs hundreds of
    // MB of host commit for the life of the process. Processors retain the
    // runtime while they hold CUDA resources. Last-owner release requests a
    // context reset and reports its status; the driver library can stay loaded.
    // The next CUDA user pays context creation again, off the audio callback.
    static void retainRuntime()
    {
        runtime_.retainHold();
    }

    static void releaseRuntime()
    {
        runtime_.releaseHold();
    }

    static void resetIfIdle()
    {
        runtime_.resetIfIdle();
    }

private:
    struct ProbeHold
    {
        ProbeHold() { retainRuntime(); }
        ~ProbeHold() { releaseRuntime(); }
        ProbeHold(const ProbeHold&) = delete;
        ProbeHold& operator=(const ProbeHold&) = delete;
    };
    static bool acquireSelected(int deviceIndex, RuntimeLease& lease, const char* prefix, std::string& error)
    {
        auto selected = runtime_.acquire(deviceIndex, prefix, error);
        if (!selected) return false;
        lease = std::move(selected);
        return true;
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
    inline static CudaRuntimeLeaseRegistry<EchoCudaRuntimeApi> runtime_;
};
