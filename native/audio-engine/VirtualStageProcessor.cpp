#include "VirtualStageProcessor.h"

#include <algorithm>
#include <cmath>

namespace echo
{
namespace
{
constexpr double pi = 3.14159265358979323846;
constexpr double minimumAzimuthDegrees = 15.0;
constexpr double maximumAzimuthDegrees = 75.0;
// Equal total reflection energy in the two ears, with different arrivals.
// These feed-forward taps have no feedback or unbounded reverb tail.
constexpr double reflectionSeconds[2][4] = {
    { 0.0073, 0.0157, 0.0197, 0.0251 },
    { 0.0091, 0.0129, 0.0223, 0.0271 },
};
constexpr double reflectionGains[4] = { 0.18, 0.11, 0.075, 0.045 };
constexpr double reflectionGainSum = 0.41;
constexpr double longestReflectionSeconds = 0.0271;
constexpr double filterSettlingSeconds = 0.04;

double clampFinite(double value, double minimum, double maximum, double fallback)
{
    return std::isfinite(value) ? std::clamp<double>(value, minimum, maximum) : fallback;
}

int wrapIndex(int index, int length)
{
    const int wrapped = index % length;
    return wrapped < 0 ? wrapped + length : wrapped;
}
} // namespace

void VirtualStageProcessor::prepare(double sampleRate, int, int)
{
    sampleRate_ = clampFinite(sampleRate, 8'000.0, 384'000.0, 48'000.0);
    const auto lowpass = [this](double frequency) {
        return 1.0 - std::exp(-2.0 * pi * frequency / sampleRate_);
    };
    directPath_.prepare(sampleRate_);
    roomLowpassCoefficient_ = lowpass(4'500.0);
    roomBassCoefficient_ = lowpass(110.0);
    parameterCoefficient_ = 1.0 - std::exp(-1.0 / (0.015 * sampleRate_));
    mixStep_ = 1.0 / (0.020 * sampleRate_);
    delayLength_ = static_cast<int>(std::ceil(longestReflectionSeconds * sampleRate_)) + 4;
    maximumTailSamples_ = static_cast<int64_t>(std::ceil(
        (longestReflectionSeconds + filterSettlingSeconds) * sampleRate_)) + 4;
    for (int ear = 0; ear < 2; ++ear)
    {
        for (int tap = 0; tap < 4; ++tap)
            reflectionDelaySamples_[ear][tap] = reflectionSeconds[ear][tap] * sampleRate_;
        roomDelay_[ear].assign(static_cast<std::size_t>(delayLength_), 0.0);
    }
    resetHistory();
}

void VirtualStageProcessor::reset()
{
    resetHistory();
}

void VirtualStageProcessor::resetHistory()
{
    cursor_ = 0;
    directPath_.reset();
    smoothedMix_ = 0.0;
    mixStart_ = mixTarget_ = 0.0;
    mixPhase_ = 1.0;
    tailSamples_ = 0;
    historyArmed_ = false;
    processingActive_.store(false, std::memory_order_release);
    for (int ear = 0; ear < 2; ++ear)
    {
        roomLowpass_[ear] = roomBass_[ear] = 0.0;
        std::fill(roomDelay_[ear].begin(), roomDelay_[ear].end(), 0.0);
    }
}

double VirtualStageProcessor::readDelay(const std::vector<double>& delay, double delaySamples) const
{
    if (delayLength_ < 2)
        return 0.0;
    const double clamped = std::clamp<double>(delaySamples, 0.0, static_cast<double>(delayLength_ - 2));
    const int whole = static_cast<int>(clamped);
    const double fraction = clamped - static_cast<double>(whole);
    const int newer = wrapIndex(cursor_ - whole, delayLength_);
    const int older = wrapIndex(newer - 1, delayLength_);
    return delay[static_cast<std::size_t>(newer)] * (1.0 - fraction)
        + delay[static_cast<std::size_t>(older)] * fraction;
}

template <typename Sample>
void VirtualStageProcessor::processBlock(PlanarAudioBuffer<Sample>& buffer, int startSample, int numSamples)
{
    if (numSamples <= 0 || buffer.getNumChannels() < 2)
        return;
    if (!isProcessingActive() || delayLength_ < 2)
    {
        if (historyArmed_)
            resetHistory();
        return;
    }

    const auto state = getState();
    if (state.speakersBypassed)
    {
        if (historyArmed_) resetHistory();
        return;
    }
    const double targetMix = state.enabled ? state.strength : 0.0;
    if (targetMix == 0.0 && smoothedMix_ == 0.0)
    {
        if (historyArmed_) resetHistory();
        return;
    }
    if (targetMix != mixTarget_)
    {
        // Restart from the current audible mix when a control changes during
        // an existing ramp. The cubic has zero slope at both endpoints.
        mixStart_ = smoothedMix_;
        mixTarget_ = targetMix;
        mixPhase_ = 0.0;
    }
    directPath_.setAngle(state.azimuthDegrees, !historyArmed_);
    if (!historyArmed_)
    {
        smoothedReflection_ = state.reflection;
    }
    auto* left = buffer.getWritePointer(0, startSample);
    auto* right = buffer.getWritePointer(1, startSample);
    historyArmed_ = true;

    for (int sample = 0; sample < numSamples; ++sample)
    {
        if (mixPhase_ < 1.0)
        {
            mixPhase_ = std::min(1.0, mixPhase_ + mixStep_);
            const double eased = mixPhase_ * mixPhase_ * (3.0 - 2.0 * mixPhase_);
            smoothedMix_ = mixStart_ + (mixTarget_ - mixStart_) * eased;
        }
        smoothedReflection_ += parameterCoefficient_ * (state.reflection - smoothedReflection_);
        const double input[2] { left[sample], right[sample] };
        const auto direct = directPath_.process(input);
        for (int ear = 0; ear < 2; ++ear)
        {
            roomLowpass_[ear] += roomLowpassCoefficient_ * (input[ear] - roomLowpass_[ear]);
            roomBass_[ear] += roomBassCoefficient_ * (roomLowpass_[ear] - roomBass_[ear]);
            if (std::abs(roomLowpass_[ear]) < 1.0e-20) roomLowpass_[ear] = 0.0;
            if (std::abs(roomBass_[ear]) < 1.0e-20) roomBass_[ear] = 0.0;
            roomDelay_[ear][static_cast<std::size_t>(cursor_)] = roomLowpass_[ear] - roomBass_[ear];
        }

        double room[2] {};
        for (int ear = 0; ear < 2; ++ear)
            for (int tap = 0; tap < 4; ++tap)
                room[ear] += reflectionGains[tap] * readDelay(
                    roomDelay_[ear ^ (tap & 1)], reflectionDelaySamples_[ear][tap]);
        const double roomMix = smoothedMix_ * smoothedReflection_;
        // Reserve headroom for the added room energy, without a hard clipper.
        // Symmetric direct paths keep correlated content centered, including
        // when room is off; only bypass promises the original waveform.
        const double gain = 1.0 / (1.0 + reflectionGainSum * roomMix);
        left[sample] = static_cast<Sample>((input[0] + smoothedMix_ * (direct[0] - input[0]) + roomMix * room[0]) * gain);
        right[sample] = static_cast<Sample>((input[1] + smoothedMix_ * (direct[1] - input[1]) + roomMix * room[1]) * gain);

        cursor_ = cursor_ + 1 < delayLength_ ? cursor_ + 1 : 0;
        // Count from the LAST active input sample, not from the block boundary.
        // Include filter settling so a final impulse cannot lose its room tail.
        if (std::max(std::abs(input[0]), std::abs(input[1])) > 1.0e-9)
            tailSamples_ = maximumTailSamples_;
        else if (tailSamples_ > 0)
            --tailSamples_;
    }
    processingActive_.store(smoothedMix_ > 0.0, std::memory_order_release);
    if (targetMix == 0.0 && smoothedMix_ == 0.0)
        resetHistory();
}

int64_t VirtualStageProcessor::tailFramesRemaining() const noexcept
{
    return isProcessingActive() ? tailSamples_ : 0;
}

void VirtualStageProcessor::setState(const VirtualStageState& state)
{
    strength_.store(clampFinite(state.strength, 0.0f, 1.0f, 0.45f), std::memory_order_release);
    speakersBypassed_.store(state.speakersBypassed, std::memory_order_release);
    azimuthDegrees_.store(
        clampFinite(state.azimuthDegrees, minimumAzimuthDegrees, maximumAzimuthDegrees, 30.0f),
        std::memory_order_release);
    reflection_.store(clampFinite(state.reflection, 0.0f, 1.0f, 1.0f), std::memory_order_release);
    enabled_.store(state.enabled, std::memory_order_release);
}

VirtualStageState VirtualStageProcessor::getState() const
{
    return {
        enabled_.load(std::memory_order_acquire),
        strength_.load(std::memory_order_acquire),
        speakersBypassed_.load(std::memory_order_acquire),
        azimuthDegrees_.load(std::memory_order_acquire),
        reflection_.load(std::memory_order_acquire)
    };
}

void VirtualStageProcessor::setEnabled(bool enabled)
{
    enabled_.store(enabled, std::memory_order_release);
}

bool VirtualStageProcessor::isEnabled() const
{
    return enabled_.load(std::memory_order_acquire);
}

bool VirtualStageProcessor::isProcessingActive() const
{
    return !speakersBypassed_.load(std::memory_order_acquire)
        && (isEnabled() || processingActive_.load(std::memory_order_acquire));
}

template void VirtualStageProcessor::processBlock<float>(PlanarAudioBuffer<float>&, int, int);
template void VirtualStageProcessor::processBlock<double>(PlanarAudioBuffer<double>&, int, int);
} // namespace echo
