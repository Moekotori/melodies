#include "CudaFirProcessor.h"
#include "CudaSdmProcessor.h"
#include "../sdm-realtime-lab/RoutePlan.h"
#include "../sdm-analysis-lab/SdmSpectrumAnalysis.h"
#include <algorithm>
#include <bit>
#include <chrono>
#include <cmath>
#include <iomanip>
#include <iostream>
#include <stdexcept>

namespace {
constexpr double pi = 3.14159265358979323846;
constexpr std::array<echo::sdmLab::FrequencyBand, 4> bands {{{20, 20'000},
    {20'000, 100'000}, {100'000, 200'000}, {200'000, 500'000}}};
void powers(const std::array<double, 4>& values)
{
    std::cout << '[';
    for (size_t index = 0; index < values.size(); ++index) std::cout << (index ? "," : "") << values[index];
    std::cout << ']';
}
void sdm(double amplitude)
{
    constexpr int rate = 384'000, frames = rate / 8;
    echo::SdmProcessor cpu;
    cpu.configure(1, echo::SdmQualityProfile::ReferenceEc, rate);
    CudaSdmProcessor gpu;
    std::string error;
    if (!gpu.configure(cpu.configuration(), cpu.state(), 4096, error)) throw std::runtime_error(error);
    std::vector<float> input(frames);
    for (int frame = 0; frame < frames; ++frame) input[frame] = static_cast<float>(amplitude * std::sin(2 * pi * 1000 * frame / rate));
    std::vector<uint8_t> a, b;
    double cpuMs = 0, gpuMs = 0;
    for (int offset = 0; offset < frames; offset += 4096)
    {
        const int count = std::min(4096, frames - offset);
        const auto start = std::chrono::steady_clock::now();
        const auto expected = cpu.processNativeDsd(input.data() + offset, count);
        const auto middle = std::chrono::steady_clock::now();
        std::vector<uint8_t> actual;
        if (!gpu.processNativeDsd(input.data() + offset, count, actual, error)) throw std::runtime_error(error);
        const auto end = std::chrono::steady_clock::now();
        if (expected.size() != actual.size()) throw std::runtime_error("cuda_output_size_mismatch");
        cpuMs += std::chrono::duration<double, std::milli>(middle - start).count();
        gpuMs += std::chrono::duration<double, std::milli>(end - middle).count();
        a.insert(a.end(), expected.begin(), expected.end()); b.insert(b.end(), actual.begin(), actual.end());
    }
    size_t differentBits = 0;
    for (size_t byte = 0; byte < a.size(); ++byte) differentBits += std::popcount(static_cast<unsigned>(a[byte] ^ b[byte]));
    std::vector<double> cpuBits(1u << 18u), gpuBits(cpuBits.size());
    const size_t firstBit = static_cast<size_t>(rate / 20) * 16;
    for (size_t bit = 0; bit < cpuBits.size(); ++bit)
    {
        const size_t index = firstBit + bit;
        cpuBits[bit] = (a[index / 8] & (1u << (index % 8))) ? 1.0 : -1.0;
        gpuBits[bit] = (b[index / 8] & (1u << (index % 8))) ? 1.0 : -1.0;
    }
    const auto cpuSpectrum = echo::sdmLab::analyzeSpectrum(cpuBits, rate * 16.0, bands, {1000});
    const auto gpuSpectrum = echo::sdmLab::analyzeSpectrum(gpuBits, rate * 16.0, bands, {1000});
    echo::SdmProcessorState state;
    if (!gpu.copyLatestState(state)) throw std::runtime_error("cuda_state_export_failed");
    echo::SdmProcessor resumed; resumed.configure(1, echo::SdmQualityProfile::ReferenceEc, rate);
    if (!resumed.restoreState(state)) throw std::runtime_error("cuda_state_restore_failed");
    std::vector<uint8_t> next;
    if (!gpu.processNativeDsd(input.data(), 513, next, error)) throw std::runtime_error(error);
    const bool handoffExact = resumed.processNativeDsd(input.data(), 513) == next;
    // Force a real pre-launch capacity failure; the committed state must
    // survive and support replay of this block on the actual CPU processor.
    if (gpu.processNativeDsd(input.data(), 4097, next, error) || !gpu.copyLatestState(state)
        || !resumed.restoreState(state)) throw std::runtime_error("cuda_failed_block_boundary_lost");
    std::cout << "{\"amplitude\":" << amplitude << ",\"differentBits\":" << differentBits
        << ",\"totalBits\":" << a.size() * 8 << ",\"cpuMs\":" << cpuMs << ",\"cudaMs\":" << gpuMs
        << ",\"cpuRecoveries\":" << cpu.stabilityRecoveryCount() << ",\"cudaRecoveries\":" << gpu.stabilityRecoveryCount()
        << ",\"handoffExact\":" << (handoffExact ? "true" : "false") << ",\"cpuBandPowerDb\":";
    powers(cpuSpectrum.bandPowerWithoutExcludedTonesDb); std::cout << ",\"cudaBandPowerDb\":";
    powers(gpuSpectrum.bandPowerWithoutExcludedTonesDb); std::cout << '}';
}
void fir(const echo::sdmRealtimeLab::Route& route)
{
    CudaFirProcessor gpu; echo::EchoSrcProcessor cpu;
    std::string error;
    if (!cpu.configure(2, route.stages, error) || !gpu.configure(2, route.stages, 4096, error)) throw std::runtime_error(error);
    std::vector<double> input(4096 * 2), actual;
    for (size_t index = 0; index < input.size(); ++index) input[index] = 0.2 * std::sin(index * 0.0712345) + 1e-12;
    double peak = 0, errorPower = 0, referencePower = 0;
    size_t samples = 0;
    std::vector<double> firstGpuBlock;
    for (int block = 0; block < 4; ++block)
    {
        const auto expected = cpu.processDouble(input.data(), 4096);
        if (!gpu.processDouble(input.data(), 4096, actual, error) || actual.size() != expected.size()) throw std::runtime_error(error);
        if (block == 0) firstGpuBlock = actual;
        for (size_t index = 0; index < actual.size(); ++index)
        {
            const double difference = actual[index] - expected[index];
            peak = std::max(peak, std::abs(difference)); errorPower += difference * difference;
            referencePower += expected[index] * expected[index]; ++samples;
        }
    }
    if (!gpu.reset(error) || !gpu.processDouble(input.data(), 4096, actual, error) || actual != firstGpuBlock)
        throw std::runtime_error("cuda_fir_reset_changed_initial_output");
    std::vector<std::vector<double>> boundary;
    if (!gpu.copyLatestHistory(boundary) || !cpu.restoreHistory(boundary))
        throw std::runtime_error("cuda_fir_double_history_export_failed");
    const auto resumed = cpu.processDouble(input.data(), 513);
    if (!gpu.processDouble(input.data(), 513, actual, error) || actual != resumed)
        throw std::runtime_error("cuda_fir_double_history_handoff_changed_output");
    if (!gpu.restoreHistory(boundary, error)) throw std::runtime_error(error);
    gpu.failNextBlockForTesting();
    if (gpu.processDouble(input.data(), 513, actual, error) || !gpu.copyLatestHistory(boundary)
        || !cpu.restoreHistory(boundary) || cpu.processDouble(input.data(), 513) != resumed)
        throw std::runtime_error("cuda_fir_failed_block_committed_history");
    std::cout << "{\"route\":\"" << route.id << "\",\"samples\":" << samples
        << ",\"maxAbsoluteError\":" << peak << ",\"relativeRmsErrorDb\":";
    if (errorPower == 0) std::cout << "null";
    else std::cout << 10 * std::log10(errorPower / referencePower);
    std::cout << ",\"doubleHistoryExact\":true,\"faultBoundaryExact\":true}";
}
}
int main(int argc, char** argv)
{
    try
    {
        if (argc < 2 || argc > 3) throw std::runtime_error("usage: echo-sdm-cuda-precision <fir-plan> [--fir-only]");
        if (argc == 3)
        {
            if (std::string(argv[2]) != "--fir-only") throw std::runtime_error("invalid_precision_mode");
            std::cout << std::setprecision(12) << "{\"realCudaDevice\":true,\"fir\":[";
            bool first = true;
            for (const auto& route : echo::sdmRealtimeLab::readRoutes(argv[1]))
            {
                if (!first) std::cout << ',';
                first = false;
                fir(route);
            }
            std::cout << "]}\n";
            return 0;
        }
        std::cout << std::setprecision(12) << "{\"schemaVersion\":2,\"realCudaDevice\":true,\"physicalOutputProof\":false,\"sdm\":[";
        sdm(0.1); std::cout << ','; sdm(1.0e-6); std::cout << "],\"fir\":[";
        const auto routes = echo::sdmRealtimeLab::readRoutes(argv[1]); fir(routes.front());
        std::cout << "],\"bitMismatchIsQualityFailure\":false}\n"; return 0;
    }
    catch (const std::exception& error) { std::cerr << error.what() << '\n'; return 1; }
}
