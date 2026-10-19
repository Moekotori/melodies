#include "SdmAnalysisLab.h"
#include "SdmSpectrumAnalysis.h"
#include "NativeFormatProcessor.h"

#include <algorithm>
#include <bit>
#include <cmath>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <ostream>
#include <stdexcept>
#include <string>
#include <vector>

namespace echo::sdmLab {
namespace {
constexpr double pi = 3.14159265358979323846;
constexpr double durationSeconds = 0.25;
constexpr double settlingSeconds = 0.05;
const std::array<FrequencyBand, 4> bands {{{0, 20'000}, {20'000, 100'000},
                                        {100'000, 200'000}, {200'000, 500'000}}};

struct Route {
    std::string id;
    int sourceRate;
    int transportRate;
    std::vector<EchoSrcStageConfig> stages;
    double firDelaySeconds = 0.0;
};

std::vector<Route> readRoutes(const std::string& path)
{
    if (std::filesystem::file_size(path) > 512 * 1024)
        throw std::runtime_error("sdm_analysis_plan_too_large");
    std::ifstream input(path);
    std::string magic;
    int count = 0;
    if (!(input >> magic >> count) || magic != "ECHO_SDM_ANALYSIS_1" || count < 1 || count > 4)
        throw std::runtime_error("sdm_analysis_invalid_plan_header");
    std::vector<Route> routes;
    for (int routeIndex = 0; routeIndex < count; ++routeIndex)
    {
        Route route {};
        int stageCount = 0;
        if (!(input >> route.id >> route.sourceRate >> route.transportRate >> stageCount)
            || route.id.size() > 48 || route.id.find_first_not_of("abcdefghijklmnopqrstuvwxyz0123456789-") != std::string::npos
            || (route.sourceRate != 44'100 && route.sourceRate != 48'000)
            || route.transportRate != route.sourceRate * 8 || stageCount != 3)
            throw std::runtime_error("sdm_analysis_invalid_route");
        int stageRate = route.sourceRate;
        for (int stageIndex = 0; stageIndex < stageCount; ++stageIndex)
        {
            EchoSrcStageConfig stage;
            int taps = 0;
            if (!(input >> stage.upsampleFactor >> taps) || stage.upsampleFactor != 2
                || taps < 3 || taps > 8'191 || (taps % 2) == 0)
                throw std::runtime_error("sdm_analysis_invalid_fir_dimensions");
            stage.taps.resize(static_cast<size_t>(taps));
            for (auto& tap : stage.taps)
                if (!(input >> tap) || !std::isfinite(tap))
                    throw std::runtime_error("sdm_analysis_invalid_fir_tap");
            for (int tap = 0; tap < taps / 2; ++tap)
                if (std::abs(stage.taps[tap] - stage.taps[taps - 1 - tap]) > 1.0e-8f)
                    throw std::runtime_error("sdm_analysis_requires_linear_phase_for_declared_delay");
            stageRate *= stage.upsampleFactor;
            route.firDelaySeconds += static_cast<double>(taps - 1) / (2.0 * stageRate);
            route.stages.push_back(std::move(stage));
        }
        routes.push_back(std::move(route));
    }
    std::string trailing;
    if (input >> trailing)
        throw std::runtime_error("sdm_analysis_unexpected_plan_content");
    return routes;
}

struct Stimulus {
    const char* name;
    double amplitude;
    int kind;
    std::vector<double> tonesHz;
};

double stimulusAt(const Stimulus& stimulus, double seconds)
{
    if (stimulus.kind == 1)
        return stimulus.amplitude * (std::sin(2.0 * pi * 19'000.0 * seconds)
                                     + std::sin(2.0 * pi * 20'000.0 * seconds));
    if (stimulus.kind == 2)
        return (0.8 + 0.2 * std::sin(2.0 * pi * 7.0 * seconds))
            * std::sin(2.0 * pi * 1'000.0 * seconds);
    return stimulus.amplitude * std::sin(2.0 * pi * 1'000.37 * seconds);
}

void writeSpectrum(const Spectrum& spectrum, std::ostream& output)
{
    output << "{\"binWidthHz\":" << spectrum.binWidthHz
        << ",\"equivalentNoiseBandwidthHz\":" << spectrum.equivalentNoiseBandwidthHz
        << ",\"bandPowerDb\":[";
    for (size_t index = 0; index < bands.size(); ++index)
        output << (index == 0 ? "" : ",") << spectrum.bandPowerDb[index];
    output << "],\"bandPowerWithoutExcludedTonesDb\":[";
    for (size_t index = 0; index < bands.size(); ++index)
        output << (index == 0 ? "" : ",") << spectrum.bandPowerWithoutExcludedTonesDb[index];
    output << "],\"strongestToneHz\":" << spectrum.strongestToneHz
        << ",\"strongestTonePeakDb\":" << spectrum.strongestTonePeakDb << '}';
}

void writeCounts(const SdmClampCounts& counts, std::ostream& output)
{
    output << "{\"input\":" << counts.input << ",\"loopInput\":" << counts.loopInput
        << ",\"quantizationError\":" << counts.quantizationError << '}';
}

void measureRoute(const Route& route, const Stimulus& stimulus, std::ostream& output,
    SdmPreModulatorProfile preModulatorProfile = SdmPreModulatorProfile::Standard)
{
    EchoSrcProcessor fir;
    std::string error;
    if (!fir.configure(1, route.stages, error))
        throw std::runtime_error(error);
    SdmProcessor modulator;
    modulator.configure(1, SdmQualityProfile::Adaptive, route.transportRate,
        SdmInterpolationMode::Linear, SdmDecisionMode::Greedy, false, preModulatorProfile);
    const auto config = modulator.configuration();
    const int nativeRate = route.transportRate * sdmBitsPerTransportSample;
    const size_t firstBit = static_cast<size_t>(std::llround(settlingSeconds * nativeRate));
    const int sourceFrames = static_cast<int>(std::llround(durationSeconds * route.sourceRate));
    const size_t analyzedSamples = static_cast<size_t>(sourceFrames) * 8 * sdmBitsPerTransportSample - firstBit;
    if (analyzedSamples > maximumAnalysisSamples)
        throw std::runtime_error("sdm_analysis_route_exceeds_memory_bound");
    const double delayBits = route.firDelaySeconds * nativeRate
        + config.preModulatorDelayBits
        + sdmBitsPerTransportSample / config.preModulatorUpsampleFactor - 1;
    std::vector<double> actual;
    actual.reserve(analyzedSamples);
    size_t bitIndex = 0;
    for (int offset = 0; offset < sourceFrames; offset += 2'048)
    {
        const int frames = std::min(2'048, sourceFrames - offset);
        std::vector<float> source(static_cast<size_t>(frames));
        for (int frame = 0; frame < frames; ++frame)
            source[frame] = static_cast<float>(stimulusAt(stimulus,
                static_cast<double>(offset + frame) / route.sourceRate));
        const auto carrier = fir.process(source.data(), frames);
        const auto bytes = modulator.processNativeDsd(carrier.data(), static_cast<int>(carrier.size()));
        for (uint8_t byte : bytes)
            for (int bit = 0; bit < 8; ++bit, ++bitIndex)
                if (bitIndex >= firstBit)
                    actual.push_back((byte & (1u << bit)) != 0u ? 1.0 : -1.0);
    }
    const auto outputSpectrum = analyzeSpectrum(actual, nativeRate, bands, stimulus.tonesHz);
    output << "{\"route\":\"" << route.id << "\",\"stimulus\":\"" << stimulus.name
        << "\",\"qualityProfile\":\"adaptive\",\"sourceSampleRate\":" << route.sourceRate
        << ",\"preModulatorProfile\":\""
        << (preModulatorProfile == SdmPreModulatorProfile::Standard ? "standard" : "ultimate-reference") << "\""
        << ",\"transportSampleRate\":" << route.transportRate << ",\"nativeSampleRate\":" << nativeRate
        << ",\"profileHeadroomDb\":" << -amplitudeDb(config.profileHeadroomGain)
        << ",\"declaredDelayBits\":" << delayBits << ",\"analyzedSamples\":" << actual.size()
        << ",\"settlingSeconds\":" << settlingSeconds << ",\"toneResponses\":[";
    // Dynamic-envelope sidebands are not separately resolved by this short
    // window. Their combined waveform is still included in reference error.
    if (stimulus.kind != 2)
        for (size_t toneIndex = 0; toneIndex < stimulus.tonesHz.size(); ++toneIndex)
        {
            const double frequency = stimulus.tonesHz[toneIndex];
            const auto response = measureTone(actual, nativeRate, frequency);
            const double expectedPhase = 2.0 * pi * frequency * (firstBit - delayBits) / nativeRate - pi / 2.0;
            const double phaseDifference = response.phaseRadians - expectedPhase;
            output << (toneIndex == 0 ? "" : ",") << "{\"frequencyHz\":" << frequency
                << ",\"gainErrorDb\":" << amplitudeDb(response.magnitude / (stimulus.amplitude * config.profileHeadroomGain))
                << ",\"phaseErrorDegrees\":" << std::atan2(std::sin(phaseDifference), std::cos(phaseDifference)) * 180.0 / pi << '}';
        }
    output << "],\"outputSpectrum\":";
    writeSpectrum(outputSpectrum, output);
    // This reference is an analytic source evaluated at native-bit times.
    // No product FIR, interpolation, fitted gain or fitted phase is reused.
    for (size_t index = 0; index < actual.size(); ++index)
        actual[index] -= config.profileHeadroomGain * stimulusAt(stimulus,
            (static_cast<double>(firstBit + index) - delayBits) / nativeRate);
    const auto errorSpectrum = analyzeSpectrum(actual, nativeRate, bands, stimulus.tonesHz);
    output << ",\"alignedSourceErrorSpectrum\":";
    writeSpectrum(errorSpectrum, output);
    output << ",\"alignedSourceErrorDbReProtectedFs\":"
        << errorSpectrum.bandPowerDb[0] - amplitudeDb(config.profileHeadroomGain)
        << ",\"peakFeedback\":" << modulator.peakFeedbackState()
        << ",\"recoveries\":" << modulator.stabilityRecoveryCount() << ",\"clamps\":";
    writeCounts(modulator.clampCounts(), output);
    output << '}';
}

uint64_t appendChecksum(uint64_t hash, uint8_t value)
{
    return (hash ^ value) * 1099511628211ull;
}

std::vector<float> createStressInput(int rate)
{
    uint32_t seed = 0xdeadbeefu;
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

void probeClamp(int rate, SdmDecisionMode mode, bool ec, std::ostream& output)
{
    SdmProcessor processor;
    processor.configure(1, SdmQualityProfile::Adaptive, rate, SdmInterpolationMode::Linear, mode);
    processor.setExtendedCompensationEnabled(ec);
    constexpr int blockFrames = 64;
    const auto input = createStressInput(rate);
    uint64_t inputChecksum = 14695981039346656037ull;
    uint64_t outputChecksum = inputChecksum;
    for (float sample : input)
    {
        const uint32_t word = std::bit_cast<uint32_t>(sample);
        for (int byte = 0; byte < 4; ++byte)
            inputChecksum = appendChecksum(inputChecksum, static_cast<uint8_t>(word >> (byte * 8)));
    }
    SdmClampCounts previous;
    int events = 0;
    output << "{\"transportSampleRate\":" << rate << ",\"decisionMode\":" << static_cast<int>(mode)
        << ",\"ecEnabled\":" << (ec ? "true" : "false")
        << ",\"seed\":3735928559,\"inputFnv1a64\":\"" << std::hex << inputChecksum << std::dec
        << "\",\"eventResolutionFrames\":64,\"events\":[";
    for (int offset = 0; offset < rate / 8; offset += blockFrames)
    {
        const int frames = std::min(blockFrames, rate / 8 - offset);
        const auto bytes = processor.processNativeDsd(input.data() + offset, frames);
        for (uint8_t byte : bytes)
            outputChecksum = appendChecksum(outputChecksum, byte);
        const auto current = processor.clampCounts();
        if ((current.input != previous.input || current.loopInput != previous.loopInput
             || current.quantizationError != previous.quantizationError) && events < 16)
        {
            output << (events++ == 0 ? "" : ",") << "{\"frameStart\":" << offset
                << ",\"stimulusSection\":" << (offset / 2'048) % 4 << ",\"delta\":";
            writeCounts({current.input - previous.input, current.loopInput - previous.loopInput,
                         current.quantizationError - previous.quantizationError}, output);
            output << '}';
        }
        previous = current;
    }
    SdmProcessor comparison;
    comparison.configure(1, SdmQualityProfile::Adaptive, rate, SdmInterpolationMode::Linear, mode);
    comparison.setExtendedCompensationEnabled(ec);
    uint64_t comparisonChecksum = 14695981039346656037ull;
    for (int offset = 0; offset < static_cast<int>(input.size()); offset += 2'048)
    {
        const auto bytes = comparison.processNativeDsd(input.data() + offset,
            std::min(2'048, static_cast<int>(input.size()) - offset));
        for (uint8_t byte : bytes)
            comparisonChecksum = appendChecksum(comparisonChecksum, byte);
    }
    const auto counts = comparison.clampCounts();
    const bool invariant = comparisonChecksum == outputChecksum
        && counts.input == previous.input && counts.loopInput == previous.loopInput
        && counts.quantizationError == previous.quantizationError
        && comparison.stabilityRecoveryCount() == processor.stabilityRecoveryCount();
    if (!invariant)
        throw std::runtime_error("sdm_analysis_clamp_probe_chunk_mismatch");
    output << "],\"chunkInvariant\":true,\"comparisonBlockFrames\":2048,\"outputFnv1a64\":\""
        << std::hex << outputChecksum << std::dec << "\",\"clamps\":";
    writeCounts(previous, output);
    output << ",\"recoveries\":" << processor.stabilityRecoveryCount()
        << ",\"peakFeedback\":" << processor.peakFeedbackState() << '}';
}
} // namespace

void runAnalysisLab(const std::string& planPath, std::ostream& output)
{
    const auto routes = readRoutes(planPath);
    const std::vector<Stimulus> stimuli {
        {"off-grid-1khz-minus20", 0.1, 0, {1'000.37}},
        {"off-grid-1khz-minus100", 1.0e-5, 0, {1'000.37}},
        {"ccif-19khz-20khz", 0.05, 1, {19'000, 20'000}},
        {"full-scale-7hz-envelope", 1.0, 2, {993, 1'000, 1'007}},
    };
    output << std::setprecision(15) << "{\"schemaVersion\":1,\"generator\":\"echo-sdm-analysis-lab\","
        << "\"reference\":\"analytic source; declared linear delay only; no gain/phase fit\","
        << "\"window\":{\"name\":\"kaiser\",\"beta\":24},\"durationSeconds\":0.25,"
        << "\"maximumAnalysisSamples\":" << maximumAnalysisSamples
        << ",\"bandsHz\":[[0,20000],[20000,100000],[100000,200000],[200000,500000]],\"measurements\":[";
    int rows = 0;
    for (const auto& route : routes)
        for (const auto& stimulus : stimuli)
        {
            if (route.id.find("linear") != std::string::npos && stimulus.kind != 0)
                continue;
            output << (rows++ == 0 ? "" : ",");
            measureRoute(route, stimulus, output);
            if (stimulus.kind == 1 && route.id.find("hq-balanced") != std::string::npos)
            {
                output << ',';
                ++rows;
                measureRoute(route, stimulus, output, SdmPreModulatorProfile::UltimateReference);
            }
        }
    output << "],\"adaptiveClampProbes\":[";
    int probes = 0;
    for (int rate : {352'800, 384'000})
        for (const auto mode : {SdmDecisionMode::Greedy, SdmDecisionMode::Extra, SdmDecisionMode::Horizon})
            for (bool ec : {true, false})
            {
                if (!ec && mode != SdmDecisionMode::Extra)
                    continue;
                output << (probes++ == 0 ? "" : ",");
                probeClamp(rate, mode, ec, output);
            }
    output << "]}\n";
}
} // namespace echo::sdmLab
