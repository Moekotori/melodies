#include "FirScalarReference.h"
#include "RoutePlan.h"
#include "CudaFirProcessor.h"
#include <algorithm>
#include <cmath>
#include <cstring>
#include <iostream>
#include <memory>
#include <stdexcept>

namespace {
void require(bool passed, const char* message)
{
    if (!passed) throw std::runtime_error(message);
}
bool sameBits(const std::vector<float>& a, const std::vector<float>& b)
{
    return a.size() == b.size() && std::memcmp(a.data(), b.data(), a.size() * sizeof(float)) == 0;
}
void verify(int channels, const std::vector<echo::EchoSrcStageConfig>& stages)
{
    echo::EchoSrcProcessor whole, chunked, resumed;
    std::string error;
    for (auto* processor : {&whole, &chunked, &resumed})
        require(processor->configure(channels, stages, error), "fir_configure");
    std::vector<FirReferenceStage> referenceStages;
    size_t factor = 1;
    for (const auto& stage : stages)
    {
        referenceStages.push_back({stage.upsampleFactor, stage.taps.data(), stage.taps.size()});
        factor *= stage.upsampleFactor;
    }
    std::unique_ptr<void, decltype(&firReferenceDestroy)> reference(
        firReferenceCreate(channels, referenceStages.data(), referenceStages.size()), firReferenceDestroy);
    constexpr int frames = 137;
    std::vector<float> input(frames * channels);
    for (size_t index = 0; index < input.size(); ++index)
        input[index] = static_cast<float>(0.41 * std::sin(index * 0.731) + 0.19 * std::cos(index * 0.157));
    // Different channel samples, cancellation, exact zero and weak signal.
    input[0] = 0.0f; input[1] = -0.0f; input[2] = 1.0e-20f; input[3] = -1.0e-20f;
    const auto expected = whole.process(input.data(), frames);
    std::vector<float> scalar(expected.size());
    require(firReferenceProcess(reference.get(), input.data(), frames, scalar.data(), scalar.size())
        == scalar.size(), "fir_scalar_size");
    require(sameBits(expected, scalar), "fir_simd_scalar_bit_mismatch");
    std::vector<float> joined;
    int offset = 0;
    for (int count : {1, 2, 3, 7, 64, 60})
    {
        const auto block = chunked.process(input.data() + offset * channels, count);
        joined.insert(joined.end(), block.begin(), block.end());
        offset += count;
    }
    require(offset == frames && sameBits(expected, joined), "fir_chunk_history_mismatch");
    std::vector<std::vector<float>> histories;
    whole.copyHistory(histories);
    require(resumed.restoreHistory(histories), "fir_restore_history");
    require(sameBits(whole.process(input.data(), frames), resumed.process(input.data(), frames)),
        "fir_restored_continuation_mismatch");
    whole.reset();
    require(sameBits(whole.process(input.data(), frames), expected), "fir_reset_mismatch");
    echo::SdmProcessor sdmA, sdmB;
    for (auto* processor : {&sdmA, &sdmB})
        processor->configure(std::min(channels, 2), echo::SdmQualityProfile::Adaptive, 384000,
            echo::SdmInterpolationMode::Linear, echo::SdmDecisionMode::Greedy, false,
            echo::SdmPreModulatorProfile::UltimateReference);
    if (channels <= 2)
        require(sdmA.processDop(expected.data(), frames * static_cast<int>(factor))
            == sdmB.processDop(scalar.data(), frames * static_cast<int>(factor)), "fir_sdm_bitstream_mismatch");
}
void verifyImpulse()
{
    echo::EchoSrcProcessor processor;
    std::string error;
    const std::vector<float> taps {0.25f, 0.5f, 0.25f};
    require(processor.configure(1, {{2, taps}}, error), "fir_impulse_configure");
    const float input[] {1.0f, 0.0f, 0.0f};
    require(processor.process(input, 3) == std::vector<float>({0.5f, 1.0f, 0.5f, 0.0f, 0.0f, 0.0f}),
        "fir_independent_impulse_response");
}
void measureCuda(const echo::sdmRealtimeLab::Route& route)
{
    if (!CudaFirProcessor::builtWithCuda()) return;
    CudaFirProcessor gpu;
    echo::EchoSrcProcessor cpu;
    std::string error;
    require(cpu.configure(2, route.stages, error), "fir_cuda_cpu_configure");
    constexpr int frames = 4096;
    if (!gpu.configure(2, route.stages, frames, error)) throw std::runtime_error(error);
    std::vector<float> input(frames * 2), actual;
    for (size_t index = 0; index < input.size(); ++index)
        input[index] = static_cast<float>(0.25 * std::sin(index * 0.731));
    double peakDifference = 0.0;
    size_t differentSamples = 0;
    size_t differentDopWords = 0;
    echo::SdmProcessor cpuSdm, gpuSdm;
    for (auto* processor : {&cpuSdm, &gpuSdm})
        processor->configure(2, echo::SdmQualityProfile::Adaptive, route.carrierRate,
            echo::SdmInterpolationMode::Linear, echo::SdmDecisionMode::Greedy, false,
            echo::SdmPreModulatorProfile::UltimateReference);
    for (int block = 0; block < 3; ++block)
    {
        const auto expected = cpu.process(input.data(), frames);
        require(gpu.process(input.data(), frames, actual, error), "fir_cuda_process");
        require(actual.size() == expected.size(), "fir_cuda_output_size");
        for (size_t index = 0; index < actual.size(); ++index)
        {
            require(std::isfinite(actual[index]), "fir_cuda_nonfinite");
            peakDifference = std::max(peakDifference, std::abs(static_cast<double>(actual[index]) - expected[index]));
            differentSamples += std::memcmp(&actual[index], &expected[index], sizeof(float)) != 0;
        }
        const auto cpuWords = cpuSdm.processDop(expected.data(), static_cast<int>(expected.size() / 2));
        const auto gpuWords = gpuSdm.processDop(actual.data(), static_cast<int>(actual.size() / 2));
        for (size_t index = 0; index < cpuWords.size(); ++index)
            differentDopWords += cpuWords[index] != gpuWords[index];
    }
    // CUDA currently accumulates FP32 FMA. Report the measured difference;
    // do not turn numerical closeness into a claim of identical DSD bits.
    require(peakDifference < 1.0e-5, "fir_cuda_unexpected_numeric_error");
    std::cout << "cudaFir route=" << route.id << " differentSamples=" << differentSamples
        << " peakAbsoluteDifference=" << peakDifference << " downstreamDifferentDopWords="
        << differentDopWords << " samples=" << input.size() * 8 * 3 << '\n';
}
}
int main(int argc, char** argv)
{
    try
    {
        require(argc == 2, "usage: echo-fir-kernel-tests <fir-plan>");
        verifyImpulse();
        int cases = 0;
        for (int channels : {1, 2, 3})
            for (int factor : {1, 2, 4, 8})
            {
                verify(channels, {{factor, {0.125f, -0.375f, 1.0e-13f, 0.0f, 0.25f,
                    -0.25f, 0.375f, -0.125f, 0.03125f}}});
                ++cases;
            }
        const auto routes = echo::sdmRealtimeLab::readRoutes(argv[1]);
        for (const auto& route : routes)
        {
            verify(2, route.stages);
            ++cases;
            if (route.sourceRate == 48000 && route.carrierRate == 384000) measureCuda(route);
        }
        std::cout << "passed=" << cases << " scalar/SIMD/chunk/reset/restore; downstream SDM bits exact\n";
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
