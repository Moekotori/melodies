#include "TrackAnalysis.h"

#include <algorithm>
#include <cmath>
#include <stdexcept>

namespace echo {
namespace {

double finiteOrZero(double value)
{
    return std::isfinite(value) ? value : 0;
}

float finiteSample(float value)
{
    return std::isfinite(value) ? value : 0.f;
}

} // namespace

double TrackAnalyzer::Biquad::process(double x)
{
    const double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
    x2 = x1;
    x1 = x;
    y2 = y1;
    y1 = y;
    return y;
}

TrackAnalyzer::Biquad TrackAnalyzer::shelf48()
{
    Biquad filter;
    filter.b0 = 1.53512485958697;
    filter.b1 = -2.69169618940638;
    filter.b2 = 1.19839281085285;
    filter.a1 = -1.69065929318241;
    filter.a2 = 0.73248077421585;
    return filter;
}

TrackAnalyzer::Biquad TrackAnalyzer::highPass48()
{
    Biquad filter;
    filter.b0 = 1.0;
    filter.b1 = -2.0;
    filter.b2 = 1.0;
    filter.a1 = -1.99004745483398;
    filter.a2 = 0.99007225036621;
    return filter;
}

float TrackAnalyzer::cubic(float p0, float p1, float p2, float p3, float t)
{
    const float t2 = t * t;
    const float t3 = t2 * t;
    return 0.5f * ((2.f * p1) + (-p0 + p2) * t + (2.f * p0 - 5.f * p1 + 4.f * p2 - p3) * t2
        + (-p0 + 3.f * p1 - 3.f * p2 + p3) * t3);
}

TrackAnalyzer::TrackAnalyzer(double durationSeconds, int sampleRate, int channels)
    : sampleRate_(sampleRate)
    , channels_(channels)
    , hop_(std::max(1, sampleRate / 10))
    , expected_(std::max<int64_t>(1, std::llround(durationSeconds * static_cast<double>(sampleRate))))
{
    if (! std::isfinite(durationSeconds) || durationSeconds <= 0 || durationSeconds > 4.0 * 3600.0)
        throw std::invalid_argument("track analysis duration is out of range");
    if (sampleRate != 48000)
        throw std::invalid_argument("track analysis requires 48 kHz");
    if (channels != 1 && channels != 2)
        throw std::invalid_argument("track analysis supports one or two channels");
    const auto ceiling = static_cast<int64_t>(4.0 * 3600.0 * sampleRate);
    if (expected_ > ceiling)
        expected_ = ceiling;
    for (int channel = 0; channel < channels; ++channel) {
        shelf_[channel] = shelf48();
        highPass_[channel] = highPass48();
    }
    const int reserve = static_cast<int>(std::min<double>(maximumBlocks_, durationSeconds * 10.0 + 8.0));
    blocks_.reserve(static_cast<size_t>(std::max(reserve, 0)));
}

void TrackAnalyzer::addFrames(const float* interleaved, int frames)
{
    if (interleaved == nullptr || frames <= 0)
        return;
    const int64_t limit = expected_ + sampleRate_;
    for (int frame = 0; frame < frames && index_ < limit; ++frame, ++index_) {
        const float* sample = interleaved + static_cast<size_t>(frame) * static_cast<size_t>(channels_);
        const float left = finiteSample(sample[0]);
        const float right = channels_ > 1 ? finiteSample(sample[1]) : left;
        const float mono = std::fabs(channels_ > 1 ? 0.5f * (left + right) : left);
        observeEnvelope(std::min(1.f, mono));
        observeTruePeak(0, left);
        if (channels_ > 1)
            observeTruePeak(1, right);
        observeLoudness(left, right);
    }
}

void TrackAnalyzer::observeEnvelope(float mono)
{
    const int bucket = std::min(trackAnalysisBins - 1,
        static_cast<int>(static_cast<double>(index_) / static_cast<double>(expected_) * trackAnalysisBins));
    peaks_[bucket] = std::max(peaks_[bucket], mono);
    sums_[bucket] += static_cast<double>(mono) * mono;
    counts_[bucket] += 1;
}

void TrackAnalyzer::considerInterval(float p0, float p1, float p2, float p3)
{
    for (const float t : { 0.25f, 0.5f, 0.75f })
        truePeak_ = std::max(truePeak_, static_cast<double>(std::fabs(cubic(p0, p1, p2, p3, t))));
}

void TrackAnalyzer::observeTruePeak(int channel, float sample)
{
    float* history = history_[channel];
    truePeak_ = std::max(truePeak_, static_cast<double>(std::fabs(sample)));
    if (primed_[channel] < 3) {
        history[primed_[channel]++] = sample;
        return;
    }
    considerInterval(history[0], history[1], history[2], sample);
    history[0] = history[1];
    history[1] = history[2];
    history[2] = sample;
}

void TrackAnalyzer::observeLoudness(float left, float right)
{
    const double filteredLeft = highPass_[0].process(shelf_[0].process(left));
    double power = filteredLeft * filteredLeft;
    if (channels_ > 1) {
        const double filteredRight = highPass_[1].process(shelf_[1].process(right));
        power += filteredRight * filteredRight;
    }
    hopSum_ += power;
    if (++hopFill_ < hop_)
        return;
    hopFill_ = 0;
    window_[cursor_] = hopSum_;
    hopSum_ = 0;
    cursor_ = (cursor_ + 1) % 4;
    if (filled_ < 4)
        ++filled_;
    if (filled_ == 4 && static_cast<int>(blocks_.size()) < maximumBlocks_) {
        const double sum = window_[0] + window_[1] + window_[2] + window_[3];
        blocks_.push_back(static_cast<float>(sum / (4.0 * hop_)));
    }
}

double TrackAnalyzer::integratedGainDb() const
{
    auto loudness = [](double meanSquare) {
        return -0.691 + 10.0 * std::log10(meanSquare);
    };
    double absoluteSum = 0;
    int absoluteCount = 0;
    for (const float block : blocks_) {
        const double meanSquare = block;
        if (meanSquare <= 0 || loudness(meanSquare) < -70.0)
            continue;
        absoluteSum += meanSquare;
        ++absoluteCount;
    }
    if (absoluteCount == 0)
        return 0;
    const double relativeGate = loudness(absoluteSum / absoluteCount) - 10.0;
    double relativeSum = 0;
    int relativeCount = 0;
    for (const float block : blocks_) {
        const double meanSquare = block;
        if (meanSquare <= 0)
            continue;
        const double value = loudness(meanSquare);
        if (value < -70.0 || value < relativeGate)
            continue;
        relativeSum += meanSquare;
        ++relativeCount;
    }
    if (relativeCount == 0)
        return 0;
    // ReplayGain 2 aligns integrated loudness to -18 LUFS.
    return finiteOrZero(-18.0 - loudness(relativeSum / relativeCount));
}

TrackAnalysisResult TrackAnalyzer::finish()
{
    for (int channel = 0; channel < channels_; ++channel) {
        if (primed_[channel] < 3)
            continue;
        const float* history = history_[channel];
        considerInterval(history[0], history[1], history[2], history[2]);
    }
    TrackAnalysisResult result;
    result.truePeak = finiteOrZero(truePeak_);
    result.gainDb = integratedGainDb();
    float maximum = 0;
    for (int i = 0; i < trackAnalysisBins; ++i) {
        result.peaks[i] = peaks_[i];
        result.energy[i] = counts_[i] == 0 ? 0.f : static_cast<float>(std::sqrt(sums_[i] / counts_[i]));
        maximum = std::max(maximum, result.energy[i]);
    }
    if (maximum > 0) {
        for (float& value : result.energy)
            value /= maximum;
    }
    return result;
}

} // namespace echo
