#include "TrackAnalysis.h"

#include <cmath>
#include <iostream>
#include <string>
#include <vector>

namespace {

int failures = 0;

void require(bool condition, const char* message)
{
    if (condition)
        return;
    std::cerr << message << '\n';
    ++failures;
}

void testBuckets()
{
    constexpr int rate = 48000;
    constexpr int count = echo::trackAnalysisBins;
    echo::TrackAnalyzer analyzer(static_cast<double>(count) / rate, rate, 1);
    std::vector<float> samples(count);
    for (int i = 0; i < count; ++i)
        samples[static_cast<size_t>(i)] = static_cast<float>(i + 1) / count;
    analyzer.addFrames(samples.data(), count);
    const auto result = analyzer.finish();
    for (int i = 0; i < count; ++i) {
        const float expected = static_cast<float>(i + 1) / count;
        if (std::fabs(result.peaks[static_cast<size_t>(i)] - expected) > 0.0001f) {
            require(false, "each sample must land in its own peak bin");
            return;
        }
    }
    require(result.energy[count - 1] > 0.99f, "the loudest bin normalizes to full scale");
}

void testSilence()
{
    echo::TrackAnalyzer analyzer(1.0, 48000, 1);
    std::vector<float> samples(48000, 0.f);
    analyzer.addFrames(samples.data(), static_cast<int>(samples.size()));
    const auto result = analyzer.finish();
    require(result.truePeak == 0, "silence has no true peak");
    require(result.gainDb == 0, "silence does not invent gain");
    require(result.peaks[0] == 0.f && result.energy[0] == 0.f, "silence envelope stays empty");
}

void testSine()
{
    constexpr int rate = 48000;
    constexpr int frames = rate * 3;
    echo::TrackAnalyzer analyzer(3.0, rate, 1);
    std::vector<float> samples(frames);
    for (int i = 0; i < frames; ++i) {
        const double t = static_cast<double>(i) / rate;
        samples[static_cast<size_t>(i)] = static_cast<float>(0.5 * std::sin(2.0 * 3.14159265358979323846 * 1000.0 * t));
    }
    analyzer.addFrames(samples.data(), frames);
    const auto result = analyzer.finish();
    require(result.truePeak > 0.48 && result.truePeak < 0.56, "1 kHz sine true peak stays near its amplitude");
    require(result.gainDb > -16.0 && result.gainDb < -4.0, "integrated gain stays near ReplayGain for a half-scale sine");
    require(result.peaks[400] > 0.45f, "middle envelope bin keeps the sine peak");
    require(result.energy[400] > 0.8f, "steady energy normalizes near full scale");
}

} // namespace

int main()
{
    testBuckets();
    testSilence();
    testSine();
    if (failures != 0) {
        std::cerr << failures << " track analysis checks failed\n";
        return 1;
    }
    std::cout << "track analysis checks passed\n";
    return 0;
}
