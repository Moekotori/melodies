#include "AutomixTempoProcessor.h"

#include <algorithm>
#include <cmath>
#include <limits>

namespace
{
constexpr double halfPi = 1.57079632679489661923;
}

WsolaTempoProcessor::WsolaTempoProcessor(int sampleRate, int channels, double tempoRatio)
    : sampleRate_(std::max(8'000, sampleRate)),
      channels_(std::max(1, channels)),
      ratio_(std::clamp(tempoRatio, minimumTempoRatio, maximumTempoRatio))
{
    initialRatio_ = ratio_;
    passthrough_ = std::abs(ratio_ - 1.0) <= unityRatioEpsilon;
    windowFrames_ = std::max(64, static_cast<int>(std::lround(sampleRate_ * 0.040)));
    if ((windowFrames_ & 1) != 0)
        ++windowFrames_;
    synthesisHopFrames_ = windowFrames_ / 2;
    searchFrames_ = std::max(8, static_cast<int>(std::lround(sampleRate_ * 0.005)));
    previousTail_.resize(static_cast<size_t>(synthesisHopFrames_ * channels_), 0.0f);
    input_.reserve(static_cast<size_t>((windowFrames_ * 12) * channels_));
}

AutomixTempoProcessor::AutomixTempoProcessor(int sampleRate, int channels, double tempoRatio)
    : WsolaTempoProcessor(
        sampleRate,
        channels,
        std::clamp(tempoRatio, minimumTempoRatio, maximumTempoRatio))
{
}

double AutomixTempoProcessor::returnToUnitySeconds(double tempoRatio) noexcept
{
    const double clamped = std::clamp(tempoRatio, minimumTempoRatio, maximumTempoRatio);
    const double maximumDeviation = maximumTempoRatio - 1.0;
    const double normalizedDeviation =
        std::min(std::abs(clamped - 1.0), maximumDeviation) / maximumDeviation;
    return minimumReturnToUnitySeconds
        + (maximumReturnToUnitySeconds - minimumReturnToUnitySeconds) * normalizedDeviation;
}

void WsolaTempoProcessor::scheduleReturnToUnity(uint64_t overlapOutputFrames, double rampSeconds)
{
    if (passthrough_)
        return;
    rampStartOutputFrame_ = overlapOutputFrames;
    rampDurationOutputFrames_ = std::max<uint64_t>(
        1,
        static_cast<uint64_t>(std::llround(
            std::max(0.0, rampSeconds) * static_cast<double>(sampleRate_))));
}

bool WsolaTempoProcessor::active() const noexcept
{
    return ! passthrough_;
}

void WsolaTempoProcessor::updateRampRatio() noexcept
{
    if (rampDurationOutputFrames_ == 0 || producedOutputFrames_ < rampStartOutputFrame_)
        return;
    const uint64_t framesIntoRamp = producedOutputFrames_ - rampStartOutputFrame_;
    if (framesIntoRamp >= rampDurationOutputFrames_)
    {
        ratio_ = 1.0;
        return;
    }
    const double progress = static_cast<double>(framesIntoRamp)
        / static_cast<double>(rampDurationOutputFrames_);
    ratio_ = initialRatio_ + (1.0 - initialRatio_) * progress;
}

bool WsolaTempoProcessor::rampCompleted() const noexcept
{
    return rampDurationOutputFrames_ > 0
        && producedOutputFrames_ >= rampStartOutputFrame_ + rampDurationOutputFrames_;
}

void WsolaTempoProcessor::drainToPassthrough(std::vector<float>& output)
{
    // previousTail_ holds the next un-emitted hop of input; everything after
    // it in input_ has not been emitted either. Flush both verbatim so the
    // handoff to passthrough is sample-continuous.
    output.insert(output.end(), previousTail_.begin(), previousTail_.end());
    producedOutputFrames_ += static_cast<uint64_t>(synthesisHopFrames_);
    const size_t resumeSample = static_cast<size_t>(
        (previousCandidateFrame_ + 2 * synthesisHopFrames_) * channels_);
    if (input_.size() > resumeSample)
    {
        output.insert(
            output.end(),
            input_.begin() + static_cast<ptrdiff_t>(resumeSample),
            input_.end());
        producedOutputFrames_ +=
            (input_.size() - resumeSample) / static_cast<size_t>(channels_);
    }
    input_.clear();
    initialized_ = false;
    ratio_ = 1.0;
    passthrough_ = true;
}

double WsolaTempoProcessor::correlationAt(int candidateFrame) const
{
    double dot = 0.0;
    double leftEnergy = 1.0e-12;
    double rightEnergy = 1.0e-12;
    for (int frame = 0; frame < synthesisHopFrames_; ++frame)
    {
        for (int channel = 0; channel < channels_; ++channel)
        {
            const float left = previousTail_[static_cast<size_t>(frame * channels_ + channel)];
            const float right = input_[static_cast<size_t>(
                (candidateFrame + frame) * channels_ + channel)];
            dot += static_cast<double>(left) * right;
            leftEnergy += static_cast<double>(left) * left;
            rightEnergy += static_cast<double>(right) * right;
        }
    }
    return dot / std::sqrt(leftEnergy * rightEnergy);
}

std::vector<float> WsolaTempoProcessor::process(
    const float* interleaved,
    int frames,
    bool finalBlock)
{
    if (! active())
    {
        if (interleaved == nullptr || frames <= 0)
            return {};
        producedOutputFrames_ += static_cast<uint64_t>(frames);
        return std::vector<float>(
            interleaved,
            interleaved + static_cast<size_t>(frames * channels_));
    }
    if (interleaved != nullptr && frames > 0)
    {
        input_.insert(
            input_.end(),
            interleaved,
            interleaved + static_cast<size_t>(frames * channels_));
    }

    std::vector<float> output;
    const int availableFrames = static_cast<int>(input_.size() / static_cast<size_t>(channels_));
    output.reserve(static_cast<size_t>(
        std::max(0, availableFrames / std::max(1, synthesisHopFrames_))
        * synthesisHopFrames_ * channels_));

    if (! initialized_ && availableFrames >= windowFrames_)
    {
        for (int frame = 0; frame < synthesisHopFrames_; ++frame)
            for (int channel = 0; channel < channels_; ++channel)
                output.push_back(input_[static_cast<size_t>(frame * channels_ + channel)]);
        const int tailStart = synthesisHopFrames_;
        std::copy_n(
            input_.begin() + static_cast<ptrdiff_t>(tailStart * channels_),
            static_cast<size_t>(synthesisHopFrames_ * channels_),
            previousTail_.begin());
        initialized_ = true;
        previousCandidateFrame_ = 0;
        expectedInputFrame_ = static_cast<double>(synthesisHopFrames_) * ratio_;
        producedOutputFrames_ += static_cast<uint64_t>(synthesisHopFrames_);
    }

    while (initialized_)
    {
        updateRampRatio();
        if (rampCompleted())
        {
            drainToPassthrough(output);
            return output;
        }
        const int refreshedAvailable = static_cast<int>(input_.size() / static_cast<size_t>(channels_));
        const int expected = static_cast<int>(std::lround(expectedInputFrame_));
        const int minimum = std::max(previousCandidateFrame_ + 1, expected - searchFrames_);
        const int maximum = std::min(expected + searchFrames_, refreshedAvailable - windowFrames_);
        if (maximum < minimum)
            break;

        int bestCandidate = minimum;
        double bestCorrelation = -std::numeric_limits<double>::infinity();
        for (int candidate = minimum; candidate <= maximum; ++candidate)
        {
            const double score = correlationAt(candidate);
            if (score > bestCorrelation)
            {
                bestCorrelation = score;
                bestCandidate = candidate;
            }
        }

        for (int frame = 0; frame < synthesisHopFrames_; ++frame)
        {
            const double progress = static_cast<double>(frame)
                / static_cast<double>(std::max(1, synthesisHopFrames_ - 1));
            const float previousGain = static_cast<float>(std::cos(progress * halfPi));
            const float incomingGain = static_cast<float>(std::sin(progress * halfPi));
            for (int channel = 0; channel < channels_; ++channel)
            {
                const size_t offset = static_cast<size_t>(frame * channels_ + channel);
                const float incoming = input_[static_cast<size_t>(
                    (bestCandidate + frame) * channels_ + channel)];
                output.push_back(previousTail_[offset] * previousGain + incoming * incomingGain);
            }
        }

        const int tailStart = bestCandidate + synthesisHopFrames_;
        std::copy_n(
            input_.begin() + static_cast<ptrdiff_t>(tailStart * channels_),
            static_cast<size_t>(synthesisHopFrames_ * channels_),
            previousTail_.begin());
        previousCandidateFrame_ = bestCandidate;
        expectedInputFrame_ += static_cast<double>(synthesisHopFrames_) * ratio_;
        producedOutputFrames_ += static_cast<uint64_t>(synthesisHopFrames_);
        compactInput();
    }

    if (finalBlock && initialized_)
    {
        output.insert(output.end(), previousTail_.begin(), previousTail_.end());
        producedOutputFrames_ += static_cast<uint64_t>(synthesisHopFrames_);
        initialized_ = false;
        input_.clear();
    }
    return output;
}

void WsolaTempoProcessor::compactInput()
{
    const int keepBeforeCandidate = windowFrames_ + searchFrames_;
    if (previousCandidateFrame_ <= keepBeforeCandidate * 4)
        return;

    const int removeFrames = previousCandidateFrame_ - keepBeforeCandidate;
    input_.erase(
        input_.begin(),
        input_.begin() + static_cast<ptrdiff_t>(removeFrames * channels_));
    previousCandidateFrame_ -= removeFrames;
    expectedInputFrame_ -= static_cast<double>(removeFrames);
}
