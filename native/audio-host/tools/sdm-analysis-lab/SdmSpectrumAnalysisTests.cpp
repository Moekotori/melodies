#include "SdmSpectrumAnalysis.h"

#include <cmath>
#include <iostream>
#include <stdexcept>
#include <string>
#include <vector>

namespace {
constexpr double pi = 3.14159265358979323846;
constexpr double rate = 384'000;
constexpr size_t frames = 65'536;
const std::array<echo::sdmLab::FrequencyBand, 4> bands {{{0, 20'000}, {20'000, 100'000},
                                                      {100'000, 150'000}, {150'000, 190'000}}};

void require(bool condition, const std::string& message)
{
    if (!condition)
        throw std::runtime_error(message);
}

std::vector<double> sine(double frequency, double amplitude)
{
    std::vector<double> samples(frames);
    for (size_t frame = 0; frame < frames; ++frame)
        samples[frame] = amplitude * std::sin(2.0 * pi * frequency * frame / rate);
    return samples;
}
} // namespace

int main()
{
    try
    {
        auto signal = sine(1'000.37, 0.1);
        const auto primary = echo::sdmLab::analyzeSpectrum(signal, rate, bands);
        require(std::abs(primary.bandPowerDb[0] + 23.0102999566) < 0.01,
            "PSD normalization must recover the known sine RMS, including non-grid frequency");
        require(std::abs(primary.strongestToneHz - 1'000.37) < 0.1,
            "FFT/sub-bin search must recover a line between the former 50 Hz probes");
        require(std::abs(primary.strongestTonePeakDb + 20.0) < 0.02,
            "tone amplitude must use coherent gain rather than PSD gain");
        const auto spur = sine(6'173.19, 1.0e-6);
        for (size_t frame = 0; frame < frames; ++frame)
            signal[frame] += spur[frame];
        const auto mixed = echo::sdmLab::analyzeSpectrum(signal, rate, bands, {1'000.37});
        require(std::abs(mixed.strongestToneHz - 6'173.19) < 0.1,
            "off-grid weak spur must be found beside a larger excluded stimulus");
        require(std::abs(mixed.strongestTonePeakDb + 120.0) < 0.05,
            "weak spur magnitude must remain calibrated");
        require(std::abs(mixed.bandPowerWithoutExcludedTonesDb[0] + 123.0102999566) < 0.05,
            "masked PSD must remove the large tone without removing the weak off-grid spur");
        const auto ultrasonic = echo::sdmLab::analyzeSpectrum(sine(80'123.45, 0.5), rate, bands);
        require(std::abs(ultrasonic.bandPowerDb[1] + 9.0308998699) < 0.01,
            "ultrasonic band must recover known RMS without decimation aliasing");
        require(ultrasonic.bandPowerDb[0] < -140.0,
            "analyzer leakage into the audio band must stay below the calibrated limit");
        // Deliberate gain error must remain in the source-reference residual.
        // This catches normalization or fitting that would hide FIR damage.
        auto error = sine(1'000.37, 0.09);
        const auto reference = sine(1'000.37, 0.1);
        for (size_t frame = 0; frame < frames; ++frame)
            error[frame] -= reference[frame];
        const auto gainError = echo::sdmLab::analyzeSpectrum(error, rate, bands);
        require(std::abs(gainError.bandPowerDb[0] + 43.0102999566) < 0.01,
            "independent reference must expose a 10 percent gain error");
        std::fill(error.begin(), error.end(), 0.0);
        const auto zero = echo::sdmLab::analyzeSpectrum(error, rate, bands);
        require(zero.bandPowerDb[0] == -300.0, "numerical zero must report the explicit dB floor");
        bool invalidRejected = false;
        try { (void)echo::sdmLab::analyzeSpectrum(error, 0.0, bands); }
        catch (const std::runtime_error&) { invalidRejected = true; }
        require(invalidRejected, "invalid dimensions must fail before FFT processing");
        std::vector<double> nativeLeakage;
        for (int nativeRate : {5'644'800, 6'144'000})
        {
            // Match the actual DSD128 observation duration and native rate.
            // Large out-of-band tones reveal FFT/window leakage without
            // borrowing any product NTF or reconstruction filter.
            std::vector<double> nativeSamples(static_cast<size_t>(nativeRate / 5));
            for (size_t index = 0; index < nativeSamples.size(); ++index)
                nativeSamples[index] = 0.5 * std::sin(2.0 * pi * 333'333.37 * index / nativeRate)
                    + 0.25 * std::sin(2.0 * pi * (nativeRate / 2.0 - 7'311.19) * index / nativeRate);
            const auto native = echo::sdmLab::analyzeSpectrum(nativeSamples, nativeRate, bands);
            require(native.bandPowerDb[0] < -160.0,
                "native-rate ultrasonic leakage must not masquerade as audio-band engine noise");
            nativeLeakage.push_back(native.bandPowerDb[0]);
        }
        std::cout << "{\"passed\":true,\"sampleRate\":384000,\"samples\":65536,"
            << "\"sineRmsDb\":" << primary.bandPowerDb[0]
            << ",\"offGridSpurHz\":" << mixed.strongestToneHz
            << ",\"offGridSpurPeakDb\":" << mixed.strongestTonePeakDb
            << ",\"ultrasonicLeakageDb\":" << ultrasonic.bandPowerDb[0]
            << ",\"knownGainErrorRmsDb\":" << gainError.bandPowerDb[0]
            << ",\"nativeRateLeakageDb\":[" << nativeLeakage[0] << ',' << nativeLeakage[1]
            << "],\"nativeCalibrationRates\":[5644800,6144000],\"nativeObservationSeconds\":0.2,"
            << "\"numericalDbFloor\":-300}\n";
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
