#include "SdmRealtimeLab.h"
#include "RoutePlan.h"
#include "SdmBackendChecks.h"
#include "SdmShadowMeasurement.h"
#include "NativeFirProcessor.h"
#include "NativeSdmProcessor.h"
#include <algorithm>
#include <array>
#include <chrono>
#include <cmath>
#include <iomanip>
#include <stdexcept>

namespace echo::sdmRealtimeLab {
namespace {
using Clock = std::chrono::steady_clock;
constexpr double pi = 3.14159265358979323846;
constexpr int trials = 4;
constexpr double admissionRatio = 0.8;

double elapsedMs(Clock::time_point from, Clock::time_point to = Clock::now())
{
    return std::chrono::duration<double, std::milli>(to - from).count();
}
double median(std::array<double, trials> values)
{
    std::sort(values.begin(), values.end());
    return (values[1] + values[2]) * 0.5;
}
void writeTimes(const std::array<double, trials>& values, std::ostream& output)
{
    output << "{\"medianMs\":" << median(values)
        << ",\"minMs\":" << *std::min_element(values.begin(), values.end())
        << ",\"maxMs\":" << *std::max_element(values.begin(), values.end()) << ",\"samplesMs\":[";
    for (int index = 0; index < trials; ++index)
        output << (index == 0 ? "" : ",") << values[index];
    output << "]}";
}
const char* qualityName(SdmQualityProfile quality)
{
    switch (quality)
    {
        case SdmQualityProfile::Hifi: return "hifi";
        case SdmQualityProfile::Reference: return "reference";
        case SdmQualityProfile::Insane: return "insane";
        case SdmQualityProfile::Adaptive: return "adaptive";
        default: return "reference-ec";
    }
}
void writeRoute(const Route& route, SdmQualityProfile quality,
    SdmPreModulatorProfile terminal, SdmDecisionMode decision, int sourceFrames, std::ostream& output)
{
    const auto configStart = Clock::now();
    NativeFirProcessor fir;
    NativeSdmProcessor sdm;
    std::string error;
    const int factor = route.carrierRate / route.sourceRate;
    if (!fir.configure(2, route.stages, false, sourceFrames, error)
        || !sdm.configure(2, quality, route.carrierRate, false, sourceFrames * factor,
            error, decision, false, terminal))
        throw std::runtime_error(error);
    const double configureMs = elapsedMs(configStart);
    std::vector<float> input(static_cast<size_t>(sourceFrames) * 2);
    std::array<double, trials> firMs {}, sdmMs {}, totalMs {};
    double coldMs = 0.0;
    uint32_t liveOutput = 0;
    // Tiny blocks need enough warmup to fill the FIR support; one tiny block
    // would measure startup tails rather than a settled streaming route.
    const int warmupBlocks = sourceFrames < 4096
        ? std::max(1, (fir.prerollInputFrames() + sourceFrames - 1) / sourceFrames + 1) : 1;
    for (int trial = -warmupBlocks; trial < trials; ++trial)
    {
        for (int frame = 0; frame < sourceFrames; ++frame)
        {
            const double t = static_cast<double>((trial + warmupBlocks) * sourceFrames + frame) / route.sourceRate;
            const double envelope = 0.82 + 0.18 * std::sin(2 * pi * 7 * t);
            input[frame * 2] = static_cast<float>(0.98 * envelope * std::sin(2 * pi * 1000.37 * t));
            input[frame * 2 + 1] = static_cast<float>(0.93 * envelope * std::sin(2 * pi * 19001.19 * t));
        }
        const auto start = Clock::now();
        const auto carrier = fir.process(input.data(), sourceFrames);
        const auto middle = Clock::now();
        if (carrier.size() != static_cast<size_t>(sourceFrames * factor * 2))
            throw std::runtime_error("realtime_fir_output_size_mismatch");
        const auto outputWords = sdm.processDop(carrier.data(), sourceFrames * factor);
        const auto end = Clock::now();
        if (outputWords.size() != carrier.size())
            throw std::runtime_error("realtime_sdm_output_size_mismatch");
        liveOutput ^= outputWords.front() ^ outputWords.back();
        if (trial == -warmupBlocks) coldMs = elapsedMs(start, end);
        if (trial >= 0)
        {
            firMs[trial] = elapsedMs(start, middle);
            sdmMs[trial] = elapsedMs(middle, end);
            totalMs[trial] = elapsedMs(start, end);
        }
    }
    const double deadlineMs = static_cast<double>(sourceFrames) * 1000 / route.sourceRate;
    const double maximum = *std::max_element(totalMs.begin(), totalMs.end());
    const auto counts = sdm.clampCounts();
    const bool guarded = sdm.stabilityRecoveryCount() == 0 && counts.input == 0
        && counts.loopInput == 0 && counts.quantizationError == 0;
    output << "{\"route\":\"" << route.id << "\",\"sourceRate\":" << route.sourceRate
        << ",\"carrierRate\":" << route.carrierRate << ",\"channels\":2,\"sourceBlockFrames\":" << sourceFrames
        << ",\"quality\":\"" << qualityName(quality)
        << "\",\"terminal\":\"" << (terminal == SdmPreModulatorProfile::Standard ? "standard" : "ultimate-reference")
        << "\",\"decision\":" << static_cast<int>(decision)
        << ",\"configureMs\":" << configureMs << ",\"firstBlockMs\":" << coldMs
        << ",\"warmupBlocks\":" << warmupBlocks
        << ",\"deadlineMs\":" << deadlineMs << ",\"fir\":";
    writeTimes(firMs, output);
    output << ",\"sdm\":";
    writeTimes(sdmMs, output);
    output << ",\"wholeChain\":";
    writeTimes(totalMs, output);
    output << ",\"medianRealtimeRatio\":" << median(totalMs) / deadlineMs
        << ",\"maxRealtimeRatio\":" << maximum / deadlineMs
        << ",\"maxObservedDeadlineMarginPercent\":" << (1.0 - maximum / deadlineMs) * 100
        << ",\"withinObserved08Budget\":" << (maximum < deadlineMs * admissionRatio && guarded ? "true" : "false")
        << ",\"nominalGuardrailsClear\":" << (guarded ? "true" : "false")
        << ",\"recoveries\":" << sdm.stabilityRecoveryCount()
        << ",\"clamps\":{\"input\":" << counts.input << ",\"loopInput\":" << counts.loopInput
        << ",\"quantizationError\":" << counts.quantizationError << '}'
        << ",\"firMacsPerSourceFrame\":" << fir.estimatedMacsPerInputFrame()
        << ",\"terminalTailCarrierFrames\":" << sdm.preModulatorTailTransportFrames()
        << ",\"outputWitness\":" << liveOutput << '}';
}

void writeTerminalCost(std::ostream& output)
{
    int rows = 0;
    for (int rate : {176'400, 192'000, 352'800, 384'000, 705'600, 768'000, 1'411'200, 1'536'000})
        for (bool ultimate : {false, true})
        {
            SdmHalfbandUpsampler standard;
            SdmUltimatePolyphaseUpsampler reference;
            standard.configure(2, resolveSdmPreModulatorStageCount(rate));
            reference.configure(2, SdmPreModulatorProfile::UltimateReference);
            double witness = 0.0;
            std::array<double, trials> milliseconds {};
            std::array<double, sdmUltimatePreModulatorFactor> outputSamples {};
            for (int trial = -1; trial < trials; ++trial)
            {
                const auto start = Clock::now();
                for (int frame = 0; frame < 8192; ++frame)
                    for (int channel = 0; channel < 2; ++channel)
                    {
                        const double sample = (frame % 31 - 15) * 0.03;
                        if (ultimate) reference.processSample(channel, sample, outputSamples.data());
                        else standard.processSample(channel, sample, outputSamples.data());
                        witness += outputSamples[0];
                    }
                if (trial >= 0) milliseconds[trial] = elapsedMs(start);
            }
            output << (rows++ == 0 ? "" : ",") << "{\"carrierRate\":" << rate
                << ",\"terminal\":\"" << (ultimate ? "ultimate-reference" : "standard")
                << "\",\"channels\":2,\"carrierFrames\":8192,\"factor\":"
                << (ultimate ? reference.factor() : standard.factor()) << ",\"time\":";
            writeTimes(milliseconds, output);
            output << ",\"projectedCoreRatio\":" << median(milliseconds) / (8192.0 * 1000 / rate)
                << ",\"outputWitness\":" << witness << '}';
        }
}
}

void runRealtimeLab(const std::string& planPath, std::ostream& output, bool focusedAdmission, bool smallBlocksOnly, bool shadowAdmission, bool terminalOnly)
{
    const auto start = Clock::now();
    const auto routes = readRoutes(planPath);
    if (terminalOnly)
    {
        output << std::setprecision(12) << "{\"mode\":\"terminal-fir-isolated\",\"terminalOnlyMeasurements\":[";
        writeTerminalCost(output);
        output << "]}\n";
        return;
    }
    if (shadowAdmission)
    {
        output << std::setprecision(12) << "{\"schemaVersion\":2,\"mode\":\"shadow-admission\","
            << "\"budgetRatio\":0.8,\"affectsAdmission\":false,\"measuredBlocks\":32,"
            << "\"percentileMethod\":\"nearest-rank; at n=32 P99 equals max\",\"measurements\":[";
        writeShadowMeasurements(routes, output);
        output << "],\"elapsedMs\":" << elapsedMs(start) << "}\n";
        return;
    }
    output << std::setprecision(12) << "{\"schemaVersion\":1,\"generator\":\"echo-sdm-realtime-lab\","
        << "\"scope\":\"CPU stereo source FIR + product SDM wrapper; no decoder/device/background load\","
        << "\"minimumWarmupBlocks\":1,\"measuredBlocks\":4,\"budgetRatio\":0.8,\"measurements\":[";
    int rows = 0;
    const auto row = [&](const Route& route, SdmQualityProfile quality, SdmPreModulatorProfile terminal,
                         SdmDecisionMode mode, int frames) {
        if (elapsedMs(start) > 40'000) throw std::runtime_error("realtime_lab_internal_budget_exhausted");
        output << (rows++ == 0 ? "" : ",");
        writeRoute(route, quality, terminal, mode, frames, output);
    };
    if (smallBlocksOnly)
    {
        for (const auto& route : routes)
            if (route.sourceRate == 48'000 && route.carrierRate == 384'000
                && route.id.find("hq-balanced") != std::string::npos)
                for (int frames : {64, 512})
                    for (auto terminal : {SdmPreModulatorProfile::Standard, SdmPreModulatorProfile::UltimateReference})
                        row(route, SdmQualityProfile::Adaptive, terminal, SdmDecisionMode::Greedy, frames);
        output << "],\"mode\":\"small-blocks\",\"elapsedMs\":" << elapsedMs(start) << "}\n";
        return;
    }
    if (focusedAdmission)
    {
        for (const auto& route : routes)
            if (route.id.find("ultimate-reference") != std::string::npos
                && route.carrierRate / route.sourceRate <= 8)
                for (auto quality : {SdmQualityProfile::Hifi, SdmQualityProfile::Reference, SdmQualityProfile::Insane})
                    row(route, quality, SdmPreModulatorProfile::UltimateReference, SdmDecisionMode::Greedy, 4096);
        output << "],\"mode\":\"focused-admission\",\"productBackendAdmission\":";
        writeProductBackendAdmission(output);
        output << ",\"elapsedMs\":" << elapsedMs(start) << "}\n";
        return;
    }
    for (const auto& route : routes)
    {
        const bool controlled = route.id.find("hq-balanced") != std::string::npos;
        for (auto quality : {SdmQualityProfile::Adaptive, SdmQualityProfile::ReferenceEc})
        {
            // Alternate the paired order to avoid always giving Ultimate the
            // later/hotter slot. This is still a short local probe, not a soak.
            const bool reverse = (route.carrierRate / route.sourceRate) == 8
                || quality == SdmQualityProfile::ReferenceEc;
            if (controlled && !reverse)
                row(route, quality, SdmPreModulatorProfile::Standard, SdmDecisionMode::Greedy, 4096);
            row(route, quality, SdmPreModulatorProfile::UltimateReference, SdmDecisionMode::Greedy, 4096);
            if (controlled && reverse)
                row(route, quality, SdmPreModulatorProfile::Standard, SdmDecisionMode::Greedy, 4096);
        }
        if (route.sourceRate == 48'000 && route.carrierRate == 384'000 && controlled)
        {
            for (int frames : {64, 512})
                for (auto terminal : {SdmPreModulatorProfile::Standard, SdmPreModulatorProfile::UltimateReference})
                    row(route, SdmQualityProfile::Adaptive, terminal, SdmDecisionMode::Greedy, frames);
            for (auto mode : {SdmDecisionMode::Extra, SdmDecisionMode::Horizon})
                row(route, SdmQualityProfile::Adaptive, SdmPreModulatorProfile::UltimateReference, mode, 4096);
        }
    }
    output << "],\"terminalOnlyMeasurements\":[";
    writeTerminalCost(output);
    output << "],\"backendChecks\":";
    writeBackendChecks(output);
    output << ",\"elapsedMs\":" << elapsedMs(start) << "}\n";
}
}
