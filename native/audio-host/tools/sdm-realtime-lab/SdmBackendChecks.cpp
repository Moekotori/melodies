#include "SdmBackendChecks.h"
#include "NativeSdmProcessor.h"
#include "CudaFirProcessor.h"
#include <algorithm>
#include <chrono>
#include <cmath>
#include <iomanip>
#include <stdexcept>

namespace echo::sdmRealtimeLab {
namespace {
void require(bool condition, const std::string& message)
{
    if (!condition) throw std::runtime_error(message);
}
bool equalCounts(const SdmClampCounts& a, const SdmClampCounts& b)
{
    return a.input == b.input && a.loopInput == b.loopInput
        && a.quantizationError == b.quantizationError;
}
void ultimateCpuInvariants(int rate, SdmQualityProfile quality)
{
    SdmProcessor whole, chunked;
    for (auto* processor : {&whole, &chunked})
        processor->configure(2, quality, rate, SdmInterpolationMode::Linear,
            SdmDecisionMode::Greedy, false, SdmPreModulatorProfile::UltimateReference);
    // Silence must lock, then both channels must wake to small nonzero input.
    const int silentFrames = rate / 40;
    std::vector<float> silence(static_cast<size_t>(silentFrames) * 2, 0.0f);
    require(whole.processNativeDsd(silence.data(), silentFrames)
        == chunked.processNativeDsd(silence.data(), silentFrames), "ultimate_silence_mismatch");
    require(whole.state().idleLocked[0] != 0, "ultimate_idle_not_locked");
    std::vector<float> input(2048 * 2);
    for (int frame = 0; frame < 2048; ++frame)
    {
        input[frame * 2] = frame < 64 ? 1.0e-6f : static_cast<float>(0.8 * std::sin(frame * 0.131));
        input[frame * 2 + 1] = frame < 64 ? -1.0e-6f : static_cast<float>(0.75 * std::cos(frame * 0.117));
    }
    const auto expected = whole.processDop(input.data(), 2048);
    std::vector<uint32_t> actual;
    int offset = 0;
    for (int requested : {64, 512, 1024, 448})
    {
        const auto block = chunked.processDop(input.data() + offset * 2, requested);
        actual.insert(actual.end(), block.begin(), block.end());
        offset += requested;
    }
    require(expected == actual, "ultimate_serial_parallel_chunk_mismatch");
    require(equalCounts(whole.clampCounts(), chunked.clampCounts()), "ultimate_chunk_count_mismatch");
    require(whole.state().idleLocked[0] == 0 && whole.state().idleLocked[1] == 0,
        "ultimate_weak_signal_not_awake");
    SdmProcessor resumed;
    resumed.configure(2, quality, rate, SdmInterpolationMode::Linear,
        SdmDecisionMode::Greedy, false, SdmPreModulatorProfile::UltimateReference);
    require(resumed.restoreState(whole.state()), "ultimate_state_restore_failed");
    require(resumed.processNativeDsd(input.data(), 513) == whole.processNativeDsd(input.data(), 513),
        "ultimate_history_handoff_mismatch");
    require(equalCounts(resumed.clampCounts(), whole.clampCounts()), "ultimate_handoff_count_mismatch");
}
}

void writeBackendChecks(std::ostream& output)
{
    int invariants = 0;
    for (int rate : {176'400, 192'000, 352'800, 384'000, 705'600, 768'000, 1'411'200, 1'536'000})
        for (auto quality : {SdmQualityProfile::Adaptive, SdmQualityProfile::ReferenceEc})
        {
            ultimateCpuInvariants(rate, quality);
            ++invariants;
        }
    // Verify the product wrapper rejects both Ultimate profiles before using a kernel.
    for (auto profile : {SdmPreModulatorProfile::UltimateReference, SdmPreModulatorProfile::UltimateTransient})
    {
        NativeSdmProcessor processor;
        std::string error;
        require(!processor.configure(2, SdmQualityProfile::Adaptive, 384'000, true, 4096,
            error, SdmDecisionMode::Greedy, false, profile)
            && error == "native_cuda_sdm_ultimate_premod_not_implemented",
            "ultimate_cuda_must_be_explicitly_rejected");
    }
    output << "{\"ultimateCpuInvariantCases\":" << invariants
        << ",\"ultimateCudaRejectedProfiles\":2,\"ultimateCudaImplemented\":false,\"standardCudaChecks\":[";
    std::string error;
    const CudaFirProcessor::RuntimeHold hold;
    if (!CudaSdmProcessor::supportsRealtimeDoublePrecision(error))
    {
        output << "],\"cudaDeviceAvailable\":false}";
        return;
    }
    int rows = 0;
    std::string deviceName;
    for (int rate : {352'800, 384'000, 1'411'200, 1'536'000})
        for (auto quality : {SdmQualityProfile::Adaptive, SdmQualityProfile::ReferenceEc})
        {
            SdmProcessor cpu;
            cpu.configure(2, quality, rate);
            CudaSdmProcessor gpu;
            require(gpu.configure(cpu.configuration(), cpu.state(), 4096, error), error);
            deviceName = gpu.deviceName();
            std::vector<float> input(4096 * 2);
            for (int frame = 0; frame < 4096; ++frame)
            {
                input[frame * 2] = static_cast<float>(0.82 * std::sin(frame * 0.171));
                input[frame * 2 + 1] = static_cast<float>(0.73 * std::cos(frame * 0.113));
            }
            double cpuMs = 0.0, cudaMs = 0.0;
            for (int frames : {4096, 64, 513, 4096})
            {
                const auto start = std::chrono::steady_clock::now();
                const auto expected = cpu.processDop(input.data(), frames);
                const auto middle = std::chrono::steady_clock::now();
                std::vector<uint32_t> actual;
                require(gpu.processDop(input.data(), frames, actual, error), error);
                const auto end = std::chrono::steady_clock::now();
                require(actual == expected, "standard_cuda_bit_mismatch");
                require(equalCounts(cpu.clampCounts(), gpu.clampCounts()), "standard_cuda_count_mismatch");
                require(cpu.stabilityRecoveryCount() == gpu.stabilityRecoveryCount(), "standard_cuda_recovery_mismatch");
                if (frames == 4096)
                {
                    cpuMs = std::chrono::duration<double, std::milli>(middle - start).count();
                    cudaMs = std::chrono::duration<double, std::milli>(end - middle).count();
                }
            }
            SdmProcessorState state;
            require(gpu.copyLatestState(state), "standard_cuda_state_export_failed");
            SdmProcessor resumed;
            resumed.configure(2, quality, rate);
            require(resumed.restoreState(state), "standard_cuda_state_restore_failed");
            require(cpu.processDop(input.data(), 513) == resumed.processDop(input.data(), 513),
                "standard_cuda_cpu_handoff_mismatch");
            output << (rows++ == 0 ? "" : ",") << "{\"carrierRate\":" << rate
                << ",\"quality\":\"" << (quality == SdmQualityProfile::Adaptive ? "adaptive" : "reference-ec")
                << "\",\"bitExact\":true,\"handoffExact\":true,\"countsExact\":true"
                << ",\"last4096CarrierCpuMs\":" << cpuMs << ",\"last4096CarrierCudaMs\":" << cudaMs << '}';
        }
    output << "],\"cudaDeviceAvailable\":true,\"cudaDevice\":" << std::quoted(deviceName) << '}';
}

void writeProductBackendAdmission(std::ostream& output)
{
    output << '[';
    int rows = 0;
    for (auto quality : {SdmQualityProfile::Adaptive, SdmQualityProfile::ReferenceEc})
    {
        NativeSdmProcessor processor;
        std::string error;
        if (!processor.configure(2, quality, 384'000, true, 32768, error))
            throw std::runtime_error(error);
        output << (rows++ == 0 ? "" : ",") << "{\"quality\":\""
            << (quality == SdmQualityProfile::Adaptive ? "adaptive" : "reference-ec")
            << "\",\"terminal\":\"standard\",\"requestedBackend\":\"cuda\",\"selectedBackend\":\""
            << (processor.backend() == NativeSdmProcessor::Backend::Cuda ? "cuda" : "cpu")
            << "\",\"reason\":" << std::quoted(processor.fallbackReason()) << '}';
    }
    output << ']';
}
}
