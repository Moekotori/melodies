#include "SdmSpectrumAnalysis.h"
#include "SdmSoundCapture.h"
#include "../sdm-realtime-lab/RoutePlan.h"
#include <algorithm>
#include <cmath>
#include <iomanip>
#include <iostream>
#include <stdexcept>

namespace {
constexpr double pi = 3.14159265358979323846;
constexpr double duration = 0.25;
constexpr double settling = 0.05;
const std::array<echo::sdmLab::FrequencyBand, 4> bands {{{0, 20000}, {20000, 100000},
    {100000, 200000}, {200000, 500000}}};

double sourceAt(double seconds, bool loud)
{
    if (loud)
        return (0.8 + 0.2 * std::sin(2.0 * pi * 7.0 * seconds)) * std::sin(2.0 * pi * 1000.0 * seconds);
    return 1.0e-5 * std::sin(2.0 * pi * 1000.37 * seconds);
}
template <typename Values>
void writeNumbers(const Values& powers)
{
    std::cout << '[';
    for (size_t index = 0; index < powers.size(); ++index)
        std::cout << (index == 0 ? "" : ",") << powers[index];
    std::cout << ']';
}
void measure(const echo::sdmRealtimeLab::Route& route, bool loud,
    echo::SdmQualityProfile quality, const char* qualityName, const std::string& captureDirectory)
{
    echo::EchoSrcProcessor fir;
    std::string error;
    if (!fir.configure(1, route.stages, error)) throw std::runtime_error(error);
    echo::SdmProcessor sdm;
    sdm.configure(1, quality, route.carrierRate,
        echo::SdmInterpolationMode::Linear, echo::SdmDecisionMode::Greedy, false,
        echo::SdmPreModulatorProfile::UltimateReference);
    const auto config = sdm.configuration();
    const int nativeRate = route.carrierRate * echo::sdmBitsPerTransportSample;
    double delayBits = config.preModulatorDelayBits;
    int stageRate = route.sourceRate;
    for (const auto& stage : route.stages)
    {
        for (size_t index = 0; index < stage.taps.size() / 2; ++index)
            if (std::abs(stage.taps[index] - stage.taps[stage.taps.size() - index - 1]) > 1.0e-8f)
                throw std::runtime_error("sound_focus_requires_linear_phase");
        stageRate *= stage.upsampleFactor;
        delayBits += static_cast<double>(stage.taps.size() - 1) * nativeRate / (2.0 * stageRate);
    }
    if (stageRate != route.carrierRate || config.preModulatorUpsampleFactor != 16)
        throw std::runtime_error("sound_focus_unexpected_route");
    const int frames = static_cast<int>(std::llround(duration * route.sourceRate));
    echo::sdmLab::SoundCapture capture(captureDirectory, route.id + "-" + qualityName
        + (loud ? "-full-scale-7hz-envelope" : "-off-grid-1khz-minus100"), route.sourceRate, frames);
    const size_t firstBit = static_cast<size_t>(std::llround(settling * nativeRate));
    const size_t analysisCount = static_cast<size_t>(frames) * 8 * 16 - firstBit;
    if (analysisCount > echo::sdmLab::maximumAnalysisSamples)
        throw std::runtime_error("sound_focus_analysis_memory_limit");
    std::vector<double> actual;
    actual.reserve(analysisCount);
    uint64_t checksum = 14695981039346656037ull;
    size_t bitIndex = 0;
    for (int offset = 0; offset < frames; offset += 2048)
    {
        const int count = std::min(2048, frames - offset);
        std::vector<float> source(static_cast<size_t>(count));
        for (int index = 0; index < count; ++index)
            source[index] = static_cast<float>(sourceAt(static_cast<double>(offset + index) / route.sourceRate, loud));
        capture.writeSource(source);
        const auto carrier = fir.process(source.data(), count);
        const auto bytes = sdm.processNativeDsd(carrier.data(), static_cast<int>(carrier.size()));
        capture.writeDsd(bytes);
        for (uint8_t byte : bytes)
        {
            checksum = (checksum ^ byte) * 1099511628211ull;
            for (int bit = 0; bit < 8; ++bit, ++bitIndex)
                if (bitIndex >= firstBit) actual.push_back((byte & (1u << bit)) != 0 ? 1.0 : -1.0);
        }
    }
    if (actual.size() != analysisCount) throw std::runtime_error("sound_focus_sample_count");
    capture.finish();
    const std::vector<double> tones = loud ? std::vector<double>{993, 1000, 1007} : std::vector<double>{1000.37};
    const auto output = echo::sdmLab::analyzeSpectrum(actual, nativeRate, bands, tones);
    for (size_t index = 0; index < actual.size(); ++index)
        actual[index] -= config.profileHeadroomGain * sourceAt((firstBit + static_cast<double>(index) - delayBits) / nativeRate, loud);
    const auto residual = echo::sdmLab::analyzeSpectrum(actual, nativeRate, bands, tones);
    const auto counts = sdm.clampCounts();
    std::cout << "{\"route\":\"" << route.id << "\",\"stimulus\":\""
        << (loud ? "full-scale-7hz-envelope" : "off-grid-1khz-minus100")
        << "\",\"quality\":\"" << qualityName
        << "\",\"terminal\":\"ultimate-reference\",\"ecEnvelopeGated\":"
        << (config.ecTuning.envelopeGated ? "true" : "false")
        << ",\"sourceRate\":" << route.sourceRate << ",\"nativeRate\":" << nativeRate
        << ",\"headroomDb\":" << -echo::sdmLab::amplitudeDb(config.profileHeadroomGain)
        << ",\"ditherAmplitude\":" << config.ditherAmplitude << ",\"ntfPeakGain\":" << config.ntfPeakGain
        << ",\"declaredDelayBits\":" << delayBits << ",\"analyzedSamples\":" << actual.size()
        << ",\"inBandSourceErrorDb\":" << residual.bandPowerDb[0]
        << ",\"inputReferredInBandSourceErrorDb\":" << residual.bandPowerDb[0] - echo::sdmLab::amplitudeDb(config.profileHeadroomGain)
        << ",\"maskedInBandSourceErrorDb\":" << residual.bandPowerWithoutExcludedTonesDb[0]
        << ",\"strongestErrorToneHz\":" << residual.strongestToneHz
        << ",\"strongestErrorTonePeakDb\":" << residual.strongestTonePeakDb
        << ",\"outputBandPowerDb\":";
    writeNumbers(output.bandPowerDb);
    std::cout << ",\"sourceErrorBandPowerDb\":";
    writeNumbers(residual.bandPowerDb);
    std::cout << ",\"ntfNumerator\":";
    writeNumbers(config.feedbackNumeratorCoefficients);
    std::cout << ",\"ntfDenominator\":";
    writeNumbers(config.feedbackDenominatorCoefficients);
    std::cout << ",\"checksumFnv1a64\":\"" << std::hex << checksum << std::dec
        << "\",\"peakFeedback\":" << sdm.peakFeedbackState()
        << ",\"recoveries\":" << sdm.stabilityRecoveryCount()
        << ",\"clamps\":{\"input\":" << counts.input << ",\"loopInput\":" << counts.loopInput
        << ",\"quantizationError\":" << counts.quantizationError << "}}";
}
}

int main(int argc, char** argv)
{
    try
    {
        echo::SdmQualityProfile quality = echo::SdmQualityProfile::AdaptiveSuper;
        const char* qualityName = "adaptive-super";
        if (argc < 2 || argc > 4) throw std::runtime_error("usage: echo-sdm-sound-focus <fir-plan> [--adaptive-super|--adaptive|--reference-ec|--reference|--insane] [capture-directory]");
        if (argc >= 3)
        {
            const std::string argument = argv[2];
            if (argument == "--adaptive") { quality = echo::SdmQualityProfile::Adaptive; qualityName = "adaptive"; }
            else if (argument == "--reference-ec") { quality = echo::SdmQualityProfile::ReferenceEc; qualityName = "reference-ec"; }
            else if (argument == "--reference") { quality = echo::SdmQualityProfile::Reference; qualityName = "reference"; }
            else if (argument == "--insane") { quality = echo::SdmQualityProfile::Insane; qualityName = "insane"; }
            else if (argument != "--adaptive-super") throw std::runtime_error("sound_focus_invalid_quality");
        }
        const auto routes = echo::sdmRealtimeLab::readRoutes(argv[1]);
        std::cout << std::setprecision(15) << "{\"schemaVersion\":1,\"generator\":\"echo-sdm-sound-focus\","
            << "\"reference\":\"analytic source; declared linear delay and fixed headroom; no gain/phase fit\","
            << "\"window\":{\"name\":\"kaiser\",\"beta\":24},\"durationSeconds\":0.25,"
            << "\"settlingSeconds\":0.05,\"bandsHz\":[[0,20000],[20000,100000],[100000,200000],[200000,500000]],"
            << "\"scope\":\"CPU mono source FIR + SDM; no HQPlayer capture or DAC result\",\"measurements\":[";
        int rows = 0;
        for (const auto& route : routes)
            if (route.id.find("hq-balanced") != std::string::npos && route.carrierRate == route.sourceRate * 8)
                for (bool loud : {true, false})
                {
                    std::cout << (rows++ == 0 ? "" : ",");
                    measure(route, loud, quality, qualityName, argc == 4 ? argv[3] : "");
                    std::cout.flush();
                }
        if (rows != 4) throw std::runtime_error("sound_focus_incomplete_route_set");
        std::cout << "]}\n";
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
