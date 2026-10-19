#include "SdmShadowMeasurement.h"
#include "SdmRealtimeAdmission.h"
#include <array>
#include <stdexcept>

namespace echo::sdmRealtimeLab {
namespace {
using Clock = std::chrono::steady_clock;
constexpr int blocks = 32;
using Times = std::array<double, blocks>;
double elapsed(Clock::time_point start)
{
    return std::chrono::duration<double, std::milli>(Clock::now() - start).count();
}
void writeTimes(Times times, std::ostream& output)
{
    std::sort(times.begin(), times.end());
    output << "{\"medianMs\":" << (times[15] + times[16]) * 0.5
        << ",\"p95Ms\":" << times[30] << ",\"p99Ms\":" << times[31]
        << ",\"maxMs\":" << times[31] << '}';
}
void measure(const Route& route, SdmQualityProfile quality, bool ultimate, bool native, std::ostream& output)
{
    constexpr int frames = echo_audio_host::sdmRealtimeAdmissionSourceFrames;
    const int factor = route.carrierRate / route.sourceRate;
    const auto started = Clock::now();
    NativeFirProcessor fir;
    NativeSdmProcessor sdm;
    fir.setDoubleProcessing(true);
    sdm.setDoubleProcessing(true);
    std::string error, warning;
    if (!fir.configure(2, route.stages, false, frames, error)
        || !sdm.configure(2, quality, route.carrierRate, false, frames * factor, error,
            SdmDecisionMode::Greedy, false, ultimate ? SdmPreModulatorProfile::UltimateReference : SdmPreModulatorProfile::Standard))
        throw std::runtime_error(error);
    const double configureMs = elapsed(started);
    double admissionRatio = 0.0;
    const auto admissionStart = Clock::now();
    const bool admitted = echo_audio_host::admitSdmRealtime(fir, sdm, 2, route.sourceRate,
        route.carrierRate, factor, admissionRatio, warning, error, {}, native);
    const double admissionMs = elapsed(admissionStart);
    if (!admitted && error.find("native_sdm_realtime_admission_failed:") != 0)
        throw std::runtime_error(error);
    // Admission resets on exit. No reset occurs between the following
    // warmup and measurement blocks, even for a route the gate refused.
    struct ResetOnExit {
        NativeFirProcessor& fir; NativeSdmProcessor& sdm;
        ~ResetOnExit() { fir.reset(); sdm.reset(); }
    } reset {fir, sdm};
    const int firSupport = fir.prerollInputFrames()
        + (sdm.preModulatorTailTransportFrames() + factor - 1) / factor;
    const int warmupBlocks = std::max(1, (std::max(firSupport, route.sourceRate / 100) + frames - 1) / frames);
    std::vector<double> input(frames * 2);
    Times firTimes {}, sdmTimes {}, totalTimes {};
    const double deadlineMs = frames * 1000.0 / route.sourceRate;
    const double bufferCapacityMs = 250.0;
    double bufferMs = 60.0, minimumBufferMs = bufferMs, warmupMs = 0.0;
    double firstBlockMs = 0.0, missingMs = 0.0;
    int modeledUnderruns = 0, deadlineMisses = 0, budgetMisses = 0;
    uint32_t witness = 0;
    for (int block = -warmupBlocks; block < blocks; ++block)
    {
        for (int frame = 0; frame < frames; ++frame)
            for (int channel = 0; channel < 2; ++channel)
                input[frame * 2 + channel] = 0.29 * std::sin(
                    static_cast<double>((block + warmupBlocks) * frames + frame) * (0.031 + channel * 0.013));
        const auto start = Clock::now();
        const auto carrier = fir.processDouble(input.data(), frames);
        const double firMs = elapsed(start);
        if (carrier.size() != static_cast<size_t>(frames * factor * 2))
            throw std::runtime_error("shadow_fir_output_size_mismatch");
        const auto modStart = Clock::now();
        if (native)
        {
            const auto bytes = sdm.processNativeDsd(carrier.data(), frames * factor);
            if (bytes.size() != carrier.size() * 2) throw std::runtime_error("shadow_native_output_size_mismatch");
            witness ^= bytes.front() ^ bytes.back();
        }
        else
        {
            const auto words = sdm.processDop(carrier.data(), frames * factor);
            if (words.size() != carrier.size()) throw std::runtime_error("shadow_dop_output_size_mismatch");
            witness ^= words.front() ^ words.back();
        }
        const double sdmMs = elapsed(modStart), totalMs = elapsed(start);
        if (block == -warmupBlocks) firstBlockMs = totalMs;
        if (block < 0) { warmupMs += totalMs; continue; }
        firTimes[block] = firMs; sdmTimes[block] = sdmMs; totalTimes[block] = totalMs;
        deadlineMisses += totalMs > deadlineMs;
        budgetMisses += totalMs >= deadlineMs * 0.8;
        // Service-time model, explicitly not a DAC callback measurement.
        // A bounded FIFO starts with 60 ms; playback consumes during work.
        if (totalMs > bufferMs) { ++modeledUnderruns; missingMs += totalMs - bufferMs; }
        bufferMs = std::max(0.0, bufferMs - totalMs);
        minimumBufferMs = std::min(minimumBufferMs, bufferMs);
        bufferMs = std::min(bufferCapacityMs, bufferMs + deadlineMs);
    }
    const char* qualityName = quality == SdmQualityProfile::Hifi ? "hifi"
        : quality == SdmQualityProfile::Reference ? "reference"
        : quality == SdmQualityProfile::ReferenceEc ? "reference-ec"
        : quality == SdmQualityProfile::Adaptive ? "adaptive" : "insane";
    output << "{\"route\":\"" << route.id << "\",\"quality\":\"" << qualityName
        << "\",\"terminal\":\"" << (ultimate ? "ultimate-reference" : "standard")
        << "\",\"outputFormat\":\"" << (native ? "dsd-native-raw" : "dop24le")
        << "\",\"sourceBlockFrames\":" << frames << ",\"configureMs\":" << configureMs
        << ",\"firKernel\":\"" << fir.cpuKernelName() << "\""
        << ",\"admissionWallMs\":" << admissionMs << ",\"legacyMinResetRatio\":" << admissionRatio
        << ",\"legacyAdmitted\":" << (admitted ? "true" : "false")
        << ",\"firSupportSourceFrames\":" << firSupport << ",\"warmupBlocks\":" << warmupBlocks
        << ",\"warmupMs\":" << warmupMs << ",\"firstBlockMs\":" << firstBlockMs
        << ",\"measuredBlocks\":" << blocks << ",\"deadlineMs\":" << deadlineMs << ",\"fir\":";
    writeTimes(firTimes, output); output << ",\"sdm\":"; writeTimes(sdmTimes, output);
    output << ",\"wholeChain\":"; writeTimes(totalTimes, output);
    output << ",\"deadlineMisses\":" << deadlineMisses << ",\"budget08Misses\":" << budgetMisses
        << ",\"modeledBuffer\":{\"capacityMs\":" << bufferCapacityMs << ",\"initialMs\":60,\"minimumMs\":"
        << minimumBufferMs << ",\"finalMs\":" << bufferMs << ",\"underrunEvents\":" << modeledUnderruns
        << ",\"underrunMs\":" << missingMs << "},\"deviceUnderruns\":null,\"deviceBuffer\":null"
        << ",\"recoveries\":" << sdm.stabilityRecoveryCount() << ",\"outputWitness\":" << witness << '}';
}
}
void writeShadowMeasurements(const std::vector<Route>& routes, std::ostream& output)
{
    int rows = 0;
    for (const auto& route : routes)
    {
        const int factor = route.carrierRate / route.sourceRate;
        if (route.id.find("hq-balanced") != std::string::npos && (factor == 8 || factor == 16))
            for (auto quality : {SdmQualityProfile::Hifi, SdmQualityProfile::Reference, SdmQualityProfile::Insane})
            {
                output << (rows++ ? "," : "");
                measure(route, quality, false, false, output);
            }
        if (route.sourceRate == 48'000 && factor == 8 && route.id.find("ultimate-reference") != std::string::npos)
        {
            output << (rows++ ? "," : "");
            measure(route, SdmQualityProfile::Reference, true, true, output);
        }
    }
}
void writeReferenceChainMeasurements(const std::vector<Route>& routes, std::ostream& output)
{
    int rows = 0;
    for (const auto& route : routes)
    {
        output << (rows++ ? "," : "");
        measure(route, SdmQualityProfile::ReferenceEc, true, true, output);
        if (route.carrierRate / route.sourceRate == 8)
        {
            output << ',';
            measure(route, SdmQualityProfile::Adaptive, true, true, output);
        }
    }
}
}
