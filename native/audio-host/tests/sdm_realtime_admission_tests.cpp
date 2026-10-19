#include "SdmRealtimeAdmission.h"
#include <cmath>
#include <iostream>
#include <stdexcept>

namespace {
void require(bool value, const std::string& message)
{
    if (!value) throw std::runtime_error(message);
}

void checkDsd64(int sourceRate)
{
    const int carrierRate = sourceRate * 4;
    NativeFirProcessor fir;
    NativeSdmProcessor sdm;
    std::string error, warning;
    const std::vector<echo::EchoSrcStageConfig> stages {
        {2, {0.25f, 0.5f, 0.25f}}, {2, {0.25f, 0.5f, 0.25f}},
    };
    require(fir.configure(2, stages, false, 4096, error), error);
    require(sdm.configure(2, echo::SdmQualityProfile::Adaptive, carrierRate, false, 16384,
        error, echo::SdmDecisionMode::Greedy, false, echo::SdmPreModulatorProfile::UltimateReference), error);
    double ratio = 0.0;
    require(echo_audio_host::admitSdmRealtime(fir, sdm, 2, sourceRate, carrierRate, 4,
        ratio, warning, error), error);
    require(ratio > 0.0 && ratio < 0.8 && warning.empty(), "DSD64 must run a real admission probe");
    require(fir.processedBlocks() == 0 && sdm.processedBlocks() == 0,
        "admission must clear telemetry and PCM/SDM histories before playback");
    require(sdm.stabilityRecoveryCount() == 0 && sdm.clampCounts().input == 0,
        "admission must clear guardrail counters");
    // Reset must include Ultimate FIR history and DoP marker, not just counters.
    NativeFirProcessor freshFir;
    NativeSdmProcessor freshSdm;
    require(freshFir.configure(2, stages, false, 512, error), error);
    require(freshSdm.configure(2, echo::SdmQualityProfile::Adaptive, carrierRate, false, 2048,
        error, echo::SdmDecisionMode::Greedy, false, echo::SdmPreModulatorProfile::UltimateReference), error);
    std::vector<float> input(512 * 2, 0.001f);
    const auto actual = fir.process(input.data(), 512);
    const auto expected = freshFir.process(input.data(), 512);
    require(actual == expected && sdm.processDop(actual.data(), 2048) == freshSdm.processDop(expected.data(), 2048),
        "admitted path must start at the same state as a fresh route");
    std::cout << "PASS DSD64 source=" << sourceRate << " ratio=" << ratio << '\n';
}

void checkExpensiveDsd64Admission()
{
    // Run the real DSD64 gate on a costly valid FIR. A faster CPU/kernel may
    // legitimately admit it; the threshold and reset must still be enforced.
    const std::vector<float> taps(8191, 1.0f / 8191);
    NativeFirProcessor fir;
    NativeSdmProcessor sdm;
    std::string error, warning;
    require(fir.configure(2, {{2, taps}, {2, taps}}, false, 4096, error), error);
    require(sdm.configure(2, echo::SdmQualityProfile::Adaptive, 192000, false, 16384, error), error);
    double ratio = 0.0;
    const bool admitted = echo_audio_host::admitSdmRealtime(fir, sdm, 2, 48000, 192000, 4,
        ratio, warning, error);
    require(std::isfinite(ratio) && ratio > 0.0 && admitted == (ratio < 0.8),
        "expensive DSD64 must be measured and obey the unchanged realtime threshold");
    require(admitted ? error.empty() : error.find("native_sdm_realtime_admission_failed:") == 0,
        "admission/refusal must preserve the measured budget reason");
    require(fir.processedBlocks() == 0 && sdm.processedBlocks() == 0,
        "admission must release probe history for either measured outcome");
    std::cout << "PASS DSD64 expensive measured admission=" << admitted << " ratio=" << ratio << '\n';
}

void rejectUnsupportedCudaTerminal()
{
#if ECHO_SDM_ADMISSION_HAS_CUDA
    for (auto profile : {echo::SdmPreModulatorProfile::UltimateReference, echo::SdmPreModulatorProfile::UltimateTransient})
    {
        echo::SdmProcessor cpu;
        cpu.configure(2, echo::SdmQualityProfile::Adaptive, 384000, echo::SdmInterpolationMode::Linear,
            echo::SdmDecisionMode::Greedy, false, profile);
        CudaSdmProcessor cuda;
        std::string error;
        require(!cuda.configure(cpu.configuration(), cpu.state(), 512, error)
            && error == "native_cuda_sdm_ultimate_premod_not_implemented",
            "low-level CUDA must reject Ultimate before device selection or kernel execution");
        auto invalid = cpu.configuration();
        invalid.preModulatorProfile = echo::SdmPreModulatorProfile::Standard;
        require(!cuda.configure(invalid, cpu.state(), 512, error)
            && error == "native_cuda_sdm_invalid_premod_factor", "factor 16 must not enter a four-carrier kernel");
    }
    std::cout << "PASS low-level CUDA Ultimate/factor rejection\n";
#else
    std::cout << "SKIP low-level CUDA checks: CPU-only build\n";
#endif
}

void standardCudaParity()
{
    std::string error;
    if (!CudaSdmProcessor::supportsRealtimeDoublePrecision(error))
    {
        std::cout << "SKIP Standard CUDA parity: device unavailable\n";
        return;
    }
    echo::SdmProcessor cpu;
    cpu.configure(2, echo::SdmQualityProfile::Adaptive, 384000);
    CudaSdmProcessor cuda;
    require(cuda.configure(cpu.configuration(), cpu.state(), 1024, error), error);
    std::vector<float> input(1024 * 2, 0.000001f);
    for (int frames : {512, 64, 513})
    {
        const auto expected = cpu.processDop(input.data(), frames);
        std::vector<uint32_t> actual;
        require(cuda.processDop(input.data(), frames, actual, error), error);
        require(expected == actual, "supported CUDA Standard must remain bit-identical after the new guard");
    }
    std::cout << "PASS supported Standard CUDA bits\n";
}
}

int main()
{
    try
    {
        for (int rate : {176400, 192000, 352800, 384000, 705600, 768000, 1411200, 1536000})
            require(echo_audio_host::requiresSdmRealtimeAdmission(rate, 1), "all DSD families/rates need admission");
        require(!echo_audio_host::requiresSdmRealtimeAdmission(48000, 1), "ordinary PCM must not acquire an SDM probe");
        checkDsd64(44100);
        checkDsd64(48000);
        checkExpensiveDsd64Admission();
        rejectUnsupportedCudaTerminal();
        standardCudaParity();
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
