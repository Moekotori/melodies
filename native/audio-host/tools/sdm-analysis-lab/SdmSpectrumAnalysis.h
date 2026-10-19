#pragma once

#include <array>
#include <cstddef>
#include <vector>

namespace echo::sdmLab {

inline constexpr size_t maximumAnalysisSamples = 1u << 21u;
inline constexpr double analysisWindowBeta = 24.0;

struct FrequencyBand {
    double fromHz;
    double toHz;
};

struct Spectrum {
    double binWidthHz = 0.0;
    double equivalentNoiseBandwidthHz = 0.0;
    std::array<double, 4> bandPowerDb {};
    // The excluded window lobes reduce measured bandwidth; this is a
    // masked noise/spur metric, not the same as full-band source error.
    std::array<double, 4> bandPowerWithoutExcludedTonesDb {};
    double strongestToneHz = 0.0;
    double strongestTonePeakDb = -300.0;
};

// Integrated powers are dB relative to +/-1 RMS. A 0.1-peak sine is
// -23.0103 dB; tone peak amplitude uses the separate coherent-gain scale.
// The FFT examines every bin, with sub-bin refinement of the strongest line.
Spectrum analyzeSpectrum(
    const std::vector<double>& samples,
    double sampleRate,
    const std::array<FrequencyBand, 4>& bands,
    const std::vector<double>& excludedTonesHz = {});

struct ToneResponse {
    double magnitude;
    double phaseRadians;
};

ToneResponse measureTone(
    const std::vector<double>& samples,
    double sampleRate,
    double frequencyHz);

double powerDb(double power);
double amplitudeDb(double amplitude);

} // namespace echo::sdmLab
