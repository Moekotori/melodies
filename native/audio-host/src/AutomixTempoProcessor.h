#pragma once

#include <cstddef>
#include <cstdint>
#include <vector>

// Stereo-linked WSOLA that runs only on decoder threads. The realtime audio
// callback consumes its already-prepared PCM from the lock-free ring.
class WsolaTempoProcessor
{
public:
    static constexpr double minimumTempoRatio = 0.5;
    static constexpr double maximumTempoRatio = 2.0;
    // Ratios closer to unity than this are audibly identical to 1.0 and run
    // as verbatim passthrough.
    static constexpr double unityRatioEpsilon = 0.0001;
    // beat_match only needs the shifted tempo through the audible overlap;
    // afterwards the ratio glides back to 1.0 within this window so the rest
    // of the track plays at its native tempo.
    static constexpr double minimumReturnToUnitySeconds = 4.0;
    static constexpr double maximumReturnToUnitySeconds = 8.0;

    WsolaTempoProcessor(int sampleRate, int channels, double tempoRatio);

    // Arms a linear glide of the tempo ratio back to 1.0 once the processor
    // has produced overlapOutputFrames (deck-local frames at the processor's
    // sample rate, i.e. where the crossfade overlap ends). After the glide the
    // processor drains its window state and becomes a verbatim passthrough.
    void scheduleReturnToUnity(uint64_t overlapOutputFrames, double rampSeconds);

    bool active() const noexcept;
    double currentRatio() const noexcept { return ratio_; }
    uint64_t producedOutputFrames() const noexcept { return producedOutputFrames_; }
    std::vector<float> process(const float* interleaved, int frames, bool finalBlock = false);

private:
    double correlationAt(int candidateFrame) const;
    void compactInput();
    void updateRampRatio() noexcept;
    bool rampCompleted() const noexcept;
    void drainToPassthrough(std::vector<float>& output);

    int sampleRate_ = 48'000;
    int channels_ = 2;
    double ratio_ = 1.0;
    double initialRatio_ = 1.0;
    bool passthrough_ = false;
    uint64_t producedOutputFrames_ = 0;
    uint64_t rampStartOutputFrame_ = 0;
    uint64_t rampDurationOutputFrames_ = 0;
    int windowFrames_ = 1'920;
    int synthesisHopFrames_ = 960;
    int searchFrames_ = 240;
    bool initialized_ = false;
    double expectedInputFrame_ = 0.0;
    int previousCandidateFrame_ = 0;
    std::vector<float> input_;
    std::vector<float> previousTail_;
};

class AutomixTempoProcessor final : public WsolaTempoProcessor
{
public:
    static constexpr double minimumTempoRatio = 0.94;
    static constexpr double maximumTempoRatio = 1.06;

    static constexpr bool supportsTempoRatio(double ratio) noexcept
    {
        return ratio >= minimumTempoRatio && ratio <= maximumTempoRatio;
    }

    // Glide duration scales with how far the plan pulled the tempo away from
    // unity: barely-shifted plans recover in the minimum window, a full
    // +/-6% shift takes the maximum window.
    static double returnToUnitySeconds(double tempoRatio) noexcept;

    AutomixTempoProcessor(int sampleRate, int channels, double tempoRatio);
};
