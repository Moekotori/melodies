#include "NativeFormatProcessor.h"
#include "CudaSdmProcessor.h"

#include <algorithm>
#include <cmath>
#include <iostream>
#include <limits>
#include <stdexcept>
#include <string>
#include <vector>

namespace {
constexpr double pi = 3.14159265358979323846;

void require(bool condition, const std::string& message)
{
    if (!condition)
        throw std::runtime_error(message);
}

bool sameCounts(const echo::SdmClampCounts& left, const echo::SdmClampCounts& right)
{
    return left.input == right.input && left.loopInput == right.loopInput
        && left.quantizationError == right.quantizationError;
}

bool idleFrame(const std::vector<uint8_t>& output, size_t frame, int channels, int channel)
{
    const size_t base = frame * channels * 2 + channel;
    return output[base] == 0x69u && output[base + channels] == 0x69u;
}

// Prevents digital silence from leaving the weak-signal hysteresis gate shut.
void weakSignalAfterSilence()
{
    constexpr int rate = 352'800;
    for (const auto profile : {echo::SdmQualityProfile::Reference, echo::SdmQualityProfile::Adaptive})
    {
        echo::SdmProcessor processor;
        processor.configure(2, profile, rate);
        std::vector<float> silence(rate / 25 * 2, 0.0f);
        const auto idle = processor.processNativeDsd(silence.data(), rate / 25);
        require(idleFrame(idle, rate / 25 - 1, 2, 0), "digital zero must settle to idle");
        for (double level : {-120.0, -114.0})
        {
            std::vector<float> input(rate / 20 * 2, 0.0f);
            for (int frame = 0; frame < rate / 20; ++frame)
                input[frame * 2] = static_cast<float>(std::pow(10.0, level / 20.0)
                    * std::sin(2.0 * pi * 1'000.0 * frame / rate));
            const auto output = processor.processNativeDsd(input.data(), rate / 20);
            require(processor.state().idleLocked[0] == 0, "nonzero weak signal must unlock its channel");
            int idleFrames = 0;
            for (int frame = rate / 50; frame < rate / 20; ++frame)
            {
                idleFrames += idleFrame(output, frame, 2, 0) ? 1 : 0;
                require(idleFrame(output, frame, 2, 1), "silent sibling channel must remain idle");
            }
            require(idleFrames < (rate / 20 - rate / 50) / 2, "weak signal must not collapse to idle bytes");
            (void)processor.processNativeDsd(silence.data(), rate / 25);
        }
        // Nonzero samples far below 24-bit resolution still wake immediately.
        const float tiny[] {1.0e-12f, 0.0f};
        (void)processor.processNativeDsd(tiny, 1);
        require(processor.state().idleLocked[0] == 0, "tiny nonzero sample must wake immediately");
        (void)processor.processNativeDsd(silence.data(), rate / 25);
        require(processor.state().idleLocked[0] == 1, "returning to digital zero must relock");
        require(sameCounts(processor.clampCounts(), {}), "weak signals must never be limited");
    }
}

std::vector<float> fullScaleStress(int rate, uint32_t seed)
{
    std::vector<float> input(rate / 8);
    for (int frame = 0; frame < static_cast<int>(input.size()); ++frame)
    {
        const int section = (frame / 2'048) % 4;
        if (section < 2)
            input[frame] = section == 0 ? 1.0f : -1.0f;
        else if (section == 2)
            input[frame] = static_cast<float>(
                0.57 * std::sin(2.0 * pi * 997.0 * frame / rate)
                + 0.43 * std::sin(2.0 * pi * 19'001.0 * frame / rate));
        else
        {
            seed = seed * 1664525u + 1013904223u;
            input[frame] = static_cast<float>(static_cast<double>(seed) / 2147483648.0 - 1.0);
        }
    }
    return input;
}

void runStress(int rate, echo::SdmQualityProfile profile, echo::SdmDecisionMode decision, uint32_t seed)
{
    echo::SdmProcessor processor;
    processor.configure(1, profile, rate, echo::SdmInterpolationMode::Linear, decision);
    const auto input = fullScaleStress(rate, seed);
    for (int offset = 0; offset < static_cast<int>(input.size()); offset += 2'048)
        (void)processor.processNativeDsd(input.data() + offset,
            std::min(2'048, static_cast<int>(input.size()) - offset));
    const std::string cell = "rate=" + std::to_string(rate)
        + " profile=" + std::to_string(static_cast<int>(profile))
        + " decision=" + std::to_string(static_cast<int>(decision))
        + " seed=" + std::to_string(seed);
    require(processor.stabilityRecoveryCount() == 0, "feedback recovery: " + cell);
    if (!processor.adaptiveLoopGainEnabled())
        require(sameCounts(processor.clampCounts(), {}), "fixed nominal full scale must not be clamped: " + cell);
    require(processor.peakFeedbackState() < 3.75, "feedback must retain margin: " + cell);
}

// Catches the reproduced DSD128/256 fixed-loop failures and seed-sensitive
// DSD512 failures, with both bit-rate families at the actual protected gain.
void fixedProfileStability()
{
    int cells = 0;
    std::string failures;
    for (int rate : {176'400, 192'000, 352'800, 384'000, 705'600, 768'000, 1'411'200, 1'536'000})
        for (auto profile : {echo::SdmQualityProfile::Hifi, echo::SdmQualityProfile::Reference,
                             echo::SdmQualityProfile::Insane, echo::SdmQualityProfile::ReferenceEc})
            for (uint32_t seed : {0x12345678u, 0xdeadbeefu, 0x9e3779b9u})
            {
                try { runStress(rate, profile, echo::SdmDecisionMode::Greedy, seed); }
                catch (const std::exception& error) { failures += std::string(error.what()) + "\n"; }
                ++cells;
            }
    for (int rate : {192'000, 384'000, 1'536'000})
        for (auto profile : {echo::SdmQualityProfile::Hifi, echo::SdmQualityProfile::Reference,
                             echo::SdmQualityProfile::Insane, echo::SdmQualityProfile::Adaptive})
            for (auto mode : {echo::SdmDecisionMode::Extra, echo::SdmDecisionMode::Horizon})
            {
                try { runStress(rate, profile, mode, 0xdeadbeefu); }
                catch (const std::exception& error) { failures += std::string(error.what()) + "\n"; }
                ++cells;
            }
    require(failures.empty(), failures);
    std::cout << "PASS full-scale stability: " << cells << " cells, zero recoveries; fixed profiles zero clamps\n";
}

// Counters must reflect actual product clamps, survive state handoff and
// saturate instead of wrapping during a long session.
void clampCountsAndState()
{
    echo::SdmProcessor original;
    original.configure(2, echo::SdmQualityProfile::Hifi, 352'800);
    std::vector<float> overdrive(2 * 8'000, 8.0f);
    (void)original.processNativeDsd(overdrive.data(), 8'000);
    require(original.clampCounts().input > 0, "overdrive must count input clamps");
    auto captured = original.state();
    // Restore a bounded, near-limit feedback state to exercise the loop clamp.
    const auto config = original.configuration();
    std::fill(captured.errorHistory.begin(), captured.errorHistory.end(), 0.0);
    std::fill(captured.feedbackHistory.begin(), captured.feedbackHistory.end(), 0.0);
    captured.errorHistory[0] = 3.9 / config.feedbackNumeratorCoefficients[0];
    require(original.restoreState(captured), "bounded state must restore");
    const float sample[] {0.4f, 0.0f};
    (void)original.processNativeDsd(sample, 1);
    require(original.clampCounts().loopInput > 0, "near-limit feedback must count loop clamps");
    echo::SdmProcessor predictive;
    predictive.configure(1, echo::SdmQualityProfile::Reference, 705'600,
        echo::SdmInterpolationMode::Linear, echo::SdmDecisionMode::Extra);
    auto predictiveState = predictive.state();
    const auto predictiveConfig = predictive.configuration();
    predictiveState.transitionRampPosition = predictiveConfig.transitionRampFrames;
    // A bounded feedback state whose next-step error prefers the opposite
    // output sign exercises the error clamp independently of input limiting.
    const auto& n = predictiveConfig.feedbackNumeratorCoefficients;
    const auto& d = predictiveConfig.feedbackDenominatorCoefficients;
    const double feedback = 3.6;
    const double future = -n[0] * predictiveConfig.stabilityLimit + d[0] * feedback;
    const double determinant = n[0] * n[2] - n[1] * n[1];
    predictiveState.errorHistory[0] = (feedback * n[2] - n[1] * future) / determinant;
    predictiveState.errorHistory[1] = (n[0] * future - feedback * n[1]) / determinant;
    require(predictive.restoreState(predictiveState), "predictive state must restore");
    const float zero = 0.0f;
    (void)predictive.processNativeDsd(&zero, 1);
    require(predictive.clampCounts().quantizationError > 0, "predictive error clamp must be counted");
    captured = original.state();
    echo::SdmProcessor restored;
    restored.configure(2, echo::SdmQualityProfile::Hifi, 352'800);
    require(restored.restoreState(captured), "counter-bearing state must restore");
    require(sameCounts(restored.clampCounts(), captured.clampCounts), "restored counters must match");
    std::vector<float> next(2 * 513, 0.01f);
    require(original.processNativeDsd(next.data(), 513) == restored.processNativeDsd(next.data(), 513),
        "counter-bearing state must resume bit-exactly across stereo worker blocks");
    require(sameCounts(original.clampCounts(), restored.clampCounts()), "handoff counts must remain exact");
    auto saturated = restored.state();
    saturated.clampCounts = {~uint64_t {0}, ~uint64_t {0}, ~uint64_t {0}};
    require(restored.restoreState(saturated), "saturated counters must restore");
    (void)restored.processNativeDsd(overdrive.data(), 8'000);
    require(sameCounts(restored.clampCounts(), saturated.clampCounts), "counts must saturate");
    restored.reset();
    require(sameCounts(restored.clampCounts(), {}), "reset must clear session counters");
    uint64_t errorCount = 0;
    echo::recordSdmClamp(4.0, 3.5, errorCount);
    echo::recordSdmClamp(3.0, 3.0, errorCount);
    require(errorCount == 1, "clamp counting must distinguish limited and unchanged values");
}

void chunkInvariance()
{
    const auto mono = fullScaleStress(352'800, 0xdeadbeefu);
    std::vector<float> input(mono.size() * 2);
    for (size_t frame = 0; frame < mono.size(); ++frame)
    {
        input[frame * 2] = mono[frame];
        input[frame * 2 + 1] = -mono[frame];
    }
    echo::SdmProcessor whole, chunked;
    whole.configure(2, echo::SdmQualityProfile::Reference, 352'800);
    chunked.configure(2, echo::SdmQualityProfile::Reference, 352'800);
    const auto expected = whole.processNativeDsd(input.data(), static_cast<int>(mono.size()));
    std::vector<uint8_t> actual;
    for (int offset = 0; offset < static_cast<int>(mono.size());)
    {
        const int frames = std::min(offset % 2 == 0 ? 513 : 127,
            static_cast<int>(mono.size()) - offset);
        auto output = chunked.processNativeDsd(input.data() + offset * 2, frames);
        actual.insert(actual.end(), output.begin(), output.end());
        offset += frames;
    }
    require(expected == actual, "serial/parallel block boundaries must not change bits");
    require(sameCounts(whole.clampCounts(), chunked.clampCounts()), "block boundaries must not change counts");
}

void cudaParity()
{
    std::string error;
    if (!CudaSdmProcessor::supportsRealtimeDoublePrecision(error))
    {
        std::cout << "SKIP CUDA device: " << error << '\n';
        return;
    }
    for (auto profile : {echo::SdmQualityProfile::Reference, echo::SdmQualityProfile::Adaptive})
    {
        constexpr int rate = 384'000;
        echo::SdmProcessor cpu;
        cpu.configure(2, profile, rate);
        CudaSdmProcessor gpu;
        require(gpu.configure(cpu.configuration(), cpu.state(), 4'096, error), error);
        const int frames = rate / 25 + 512;
        std::vector<float> input(frames * 2, 0.0f);
        for (int frame = rate / 25; frame < frames; ++frame)
            input[frame * 2] = frame < frames - 128 ? 1.0e-6f : 8.0f;
        for (int offset = 0; offset < frames; offset += 4'096)
        {
            const int count = std::min(4'096, frames - offset);
            const auto expected = cpu.processNativeDsd(input.data() + offset * 2, count);
            std::vector<uint8_t> actual;
            require(gpu.processNativeDsd(input.data() + offset * 2, count, actual, error), error);
            require(expected == actual, "CUDA weak-signal wake and overdrive must preserve bits");
            require(sameCounts(cpu.clampCounts(), gpu.clampCounts()), "CUDA clamp counters must match CPU");
        }
        echo::SdmProcessorState state;
        require(gpu.copyLatestState(state), "CUDA state copy must succeed");
        echo::SdmProcessor resumed;
        resumed.configure(2, profile, rate);
        require(resumed.restoreState(state), "CUDA state must restore into CPU");
        require(sameCounts(resumed.clampCounts(), cpu.clampCounts()), "CUDA handoff must preserve counters");
        std::vector<float> tail(2 * 513, 0.001f);
        require(cpu.processDop(tail.data(), 513) == resumed.processDop(tail.data(), 513),
            "CUDA-to-CPU handoff must preserve DoP bits");
        std::cout << "PASS CUDA parity and handoff: profile=" << static_cast<int>(profile) << '\n';
    }
}
} // namespace

int main(int argc, char** argv)
{
    try
    {
        if (argc > 1 && std::string(argv[1]) == "--cuda-only")
        {
            cudaParity();
            return 0;
        }
        weakSignalAfterSilence();
        std::cout << "PASS weak signals after silence\n";
        fixedProfileStability();
        clampCountsAndState();
        std::cout << "PASS clamp counts, saturation and state restoration\n";
        chunkInvariance();
        std::cout << "PASS serial/parallel chunk invariance\n";
        cudaParity();
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << "FAIL " << error.what() << '\n';
        return 1;
    }
}
