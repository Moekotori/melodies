#include "SdmSpectrumAnalysis.h"

#include <algorithm>
#include <cmath>
#include <complex>
#include <stdexcept>

namespace echo::sdmLab {
namespace {
constexpr double pi = 3.14159265358979323846;

double besselI0(double x)
{
    double sum = 1.0;
    double term = 1.0;
    for (int index = 1; index <= 64; ++index)
    {
        term *= (x * x / 4.0) / (index * index);
        sum += term;
        if (term < sum * 1.0e-16)
            break;
    }
    return sum;
}

double windowAt(size_t index, size_t size, double inverseI0)
{
    const double x = 2.0 * static_cast<double>(index) / static_cast<double>(size - 1) - 1.0;
    return besselI0(analysisWindowBeta * std::sqrt(std::max(0.0, 1.0 - x * x))) * inverseI0;
}

void validate(const std::vector<double>& samples, double sampleRate)
{
    if (samples.size() < 32 || samples.size() > maximumAnalysisSamples
        || !std::isfinite(sampleRate) || sampleRate <= 0.0)
        throw std::runtime_error("sdm_analysis_invalid_or_oversized_input");
}

void fft(std::vector<std::complex<double>>& values)
{
    const size_t size = values.size();
    for (size_t index = 1, reversed = 0; index < size; ++index)
    {
        size_t bit = size >> 1u;
        for (; (reversed & bit) != 0u; bit >>= 1u)
            reversed ^= bit;
        reversed ^= bit;
        if (index < reversed)
            std::swap(values[index], values[reversed]);
    }
    for (size_t length = 2; length <= size; length <<= 1u)
    {
        const double angle = -2.0 * pi / static_cast<double>(length);
        const std::complex<double> step(std::cos(angle), std::sin(angle));
        for (size_t start = 0; start < size; start += length)
        {
            std::complex<double> factor(1.0, 0.0);
            for (size_t offset = 0; offset < length / 2; ++offset)
            {
                const auto even = values[start + offset];
                const auto odd = values[start + offset + length / 2] * factor;
                values[start + offset] = even + odd;
                values[start + offset + length / 2] = even - odd;
                factor *= step;
            }
        }
    }
}

bool excluded(double frequency, const std::vector<double>& tones, double width)
{
    return std::any_of(tones.begin(), tones.end(), [=](double tone) {
        return std::abs(frequency - tone) <= width;
    });
}
} // namespace

double powerDb(double power)
{
    return 10.0 * std::log10(std::max(1.0e-30, power));
}

double amplitudeDb(double amplitude)
{
    return 20.0 * std::log10(std::max(1.0e-15, amplitude));
}

ToneResponse measureTone(const std::vector<double>& samples, double sampleRate, double frequencyHz)
{
    validate(samples, sampleRate);
    if (!std::isfinite(frequencyHz) || frequencyHz <= 0.0 || frequencyHz >= sampleRate / 2.0)
        throw std::runtime_error("sdm_analysis_invalid_tone_frequency");
    const double inverseI0 = 1.0 / besselI0(analysisWindowBeta);
    const double angle = -2.0 * pi * frequencyHz / sampleRate;
    const std::complex<double> step(std::cos(angle), std::sin(angle));
    std::complex<double> phase(1.0, 0.0), value(0.0, 0.0);
    double windowSum = 0.0;
    for (size_t index = 0; index < samples.size(); ++index)
    {
        const double window = windowAt(index, samples.size(), inverseI0);
        value += samples[index] * window * phase;
        windowSum += window;
        phase *= step;
    }
    return {2.0 * std::abs(value) / windowSum, std::arg(value)};
}

Spectrum analyzeSpectrum(
    const std::vector<double>& samples,
    double sampleRate,
    const std::array<FrequencyBand, 4>& bands,
    const std::vector<double>& excludedTonesHz)
{
    validate(samples, sampleRate);
    for (const auto& band : bands)
        if (band.fromHz < 0.0 || band.toHz <= band.fromHz || band.toHz > sampleRate / 2.0)
            throw std::runtime_error("sdm_analysis_invalid_band");
    size_t fftSize = 1;
    while (fftSize < samples.size())
        fftSize <<= 1u;
    std::vector<std::complex<double>> bins(fftSize);
    const double inverseI0 = 1.0 / besselI0(analysisWindowBeta);
    double windowSum = 0.0, windowSquares = 0.0;
    for (size_t index = 0; index < samples.size(); ++index)
    {
        if (!std::isfinite(samples[index]))
            throw std::runtime_error("sdm_analysis_nonfinite_sample");
        const double window = windowAt(index, samples.size(), inverseI0);
        bins[index] = {samples[index] * window, 0.0};
        windowSum += window;
        windowSquares += window * window;
    }
    fft(bins);
    Spectrum result;
    result.binWidthHz = sampleRate / static_cast<double>(fftSize);
    result.equivalentNoiseBandwidthHz = sampleRate * windowSquares / (windowSum * windowSum);
    const double normalization = 1.0 / (static_cast<double>(fftSize) * windowSquares);
    // Exclude the full window main lobe, not merely a fixed 50 Hz grid cell.
    const double exclusionHz = (analysisWindowBeta / pi + 2.0) * sampleRate / samples.size();
    std::array<double, 4> powers {};
    std::array<double, 4> maskedPowers {};
    size_t strongestBin = 0;
    double strongestPower = 0.0;
    for (size_t index = 0; index <= fftSize / 2; ++index)
    {
        const double frequency = index * result.binWidthHz;
        const double power = std::norm(bins[index]);
        const double oneSided = index == 0 || index == fftSize / 2 ? 1.0 : 2.0;
        const bool excludeStimulus = excluded(frequency, excludedTonesHz, exclusionHz);
        for (size_t band = 0; band < bands.size(); ++band)
            if (frequency >= bands[band].fromHz && frequency < bands[band].toHz)
            {
                powers[band] += oneSided * power * normalization;
                if (!excludeStimulus)
                    maskedPowers[band] += oneSided * power * normalization;
            }
        if (frequency >= 20.0 && frequency <= 20'000.0
            && !excludeStimulus && power > strongestPower)
        {
            strongestPower = power;
            strongestBin = index;
        }
    }
    for (size_t band = 0; band < bands.size(); ++band)
    {
        result.bandPowerDb[band] = powerDb(powers[band]);
        result.bandPowerWithoutExcludedTonesDb[band] = powerDb(maskedPowers[band]);
    }
    if (strongestBin > 0 && strongestBin < fftSize / 2)
    {
        const double left = std::log(std::max(1.0e-300, std::norm(bins[strongestBin - 1])));
        const double center = std::log(std::max(1.0e-300, strongestPower));
        const double right = std::log(std::max(1.0e-300, std::norm(bins[strongestBin + 1])));
        const double denominator = left - 2.0 * center + right;
        const double offset = denominator == 0.0 ? 0.0
            : std::clamp(0.5 * (left - right) / denominator, -0.5, 0.5);
        result.strongestToneHz = (strongestBin + offset) * result.binWidthHz;
        // Refined frequency is measured again with coherent-gain correction.
        result.strongestTonePeakDb = amplitudeDb(
            measureTone(samples, sampleRate, result.strongestToneHz).magnitude);
    }
    return result;
}
} // namespace echo::sdmLab
