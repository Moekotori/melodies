#pragma once

#include <array>
#include <cstdint>
#include <vector>

namespace echo {

inline constexpr int trackAnalysisBins = 1024;

// One streaming pass over interleaved PCM. Loudness uses the 48 kHz BS.1770
// K-weighting coefficients, so the decoder must resample to 48 kHz first.
struct TrackAnalysisResult {
    double durationSeconds = 0;
    double gainDb = 0;
    double truePeak = 0;
    std::array<float, trackAnalysisBins> peaks {};
    std::array<float, trackAnalysisBins> energy {};
};

class TrackAnalyzer {
public:
    TrackAnalyzer(double durationSeconds, int sampleRate, int channels);

    void addFrames(const float* interleaved, int frames);
    TrackAnalysisResult finish();

private:
    struct Biquad {
        double b0 = 1;
        double b1 = 0;
        double b2 = 0;
        double a1 = 0;
        double a2 = 0;
        double x1 = 0;
        double x2 = 0;
        double y1 = 0;
        double y2 = 0;
        double process(double x);
    };

    void observeEnvelope(float mono);
    void observeTruePeak(int channel, float sample);
    void considerInterval(float p0, float p1, float p2, float p3);
    void observeLoudness(float left, float right);
    double integratedGainDb() const;

    static Biquad shelf48();
    static Biquad highPass48();
    static float cubic(float p0, float p1, float p2, float p3, float t);

    int sampleRate_;
    int channels_;
    int hop_;
    int64_t expected_;
    int64_t index_ = 0;
    int hopFill_ = 0;
    int cursor_ = 0;
    int filled_ = 0;
    int primed_[2] {};
    double hopSum_ = 0;
    double window_[4] {};
    double truePeak_ = 0;
    float history_[2][4] {};
    Biquad shelf_[2] {};
    Biquad highPass_[2] {};
    std::array<float, trackAnalysisBins> peaks_ {};
    std::array<double, trackAnalysisBins> sums_ {};
    std::array<int, trackAnalysisBins> counts_ {};
    // One mean-square per 400 ms block. Four hours at a 100 ms hop is the cap.
    static constexpr int maximumBlocks_ = 4 * 3600 * 10;
    std::vector<float> blocks_;
};

} // namespace echo
