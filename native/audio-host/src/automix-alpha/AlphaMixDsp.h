#pragma once
#include "AlphaBiquad.h"
#include "AlphaEqCurve.h"
#include "AlphaParameters.h"
#include "AlphaPlainBlend.h"
#include <array>

namespace echo::automix_alpha {
// Modified QPlayer AiMix, updated 2026-10-14 (Apache-2.0).
// Automatic routes, with channel-independent low-clash detection.
// State is owned by the native render lane, reset per prepared deck lease.
class MixDsp {
public:
    static constexpr int maximumChannels = 32;
    void configure(const Parameters& parameters, double rate, int frames) noexcept {
        parameters_ = parameters; rate_ = std::max(8000.0, rate);
        powerAlpha_ = smoothing(0.4); gainAlpha_ = smoothing(0.25);
        lowPowerAlpha_ = smoothing(0.5); lowGainAlpha_ = smoothing(0.2);
        powerA_ = powerB_ = lowPowerA_ = lowPowerB_ = 0;
        levelGain_ = duckGain_ = 1; sweepBucket_ = -1; bassHandoverHz_ = -1;
        curve_.configure(parameters.eqControls, frames);
        for (auto& channel : channels_) {
            channel = {};
            channel.lowA.lowPass(rate_, 200, 0.5); channel.lowB.lowPass(rate_, 200, 0.5);
            channel.midB.lowPass(rate_, 2000, 0.5);
            channel.highA.highPass(rate_, 20, 0.707); channel.highB.lowPass(rate_, 300, 0.707);
        }
    }

    template <typename Sample>
    void mixFrame(const Sample* a, const Sample* b, Sample* out, int count, double progress,
        double outgoingBaseGain = 1, double incomingBaseGain = 1) noexcept {
        count = std::clamp(count, 0, maximumChannels);
        const double p = std::clamp(progress, 0.0, 1.0);
        if (parameters_.style == Style::BassSwap) { bassSwap(a, b, out, count, p, outgoingBaseGain, incomingBaseGain); return; }
        if (parameters_.style == Style::ShowMode) { showMode(a, b, out, count, p, outgoingBaseGain, incomingBaseGain); return; }
        // The new mapped plain route hands outgoing bass over after 60%.
        // Reuse its otherwise idle high-pass state; retain no additional PCM.
        std::array<Sample, maximumChannels> outgoing {};
        if (parameters_.style == Style::PlainBlend && p > 0.6) {
            const double hz = plainBassHandoverHz(p);
            if (bassHandoverHz_ < 0 || std::abs(hz - bassHandoverHz_) > bassHandoverHz_ * 0.01) {
                bassHandoverHz_ = hz;
                for (int c = 0; c < count; ++c) channels_[c].highA.highPass(rate_, hz, 0.707);
            }
            // Java enables its filter abruptly. Ease engagement over 2% of the
            // overlap to keep continuous playback, including cancellation.
            const double wet = smooth((p - 0.6) / 0.02);
            for (int c = 0; c < count; ++c) {
                const double filtered = channels_[c].highA.process(a[c]);
                outgoing[c] = a[c] + (filtered - a[c]) * wet;
            }
            a = outgoing.data();
        }
        const bool tailRide = parameters_.style == Style::TailRide;
        const auto gains = tailRide ? std::array<double, 2> { std::sqrt(std::max(0.0, 1 - p * p)), p }
            : parameters_.style == Style::PlainBlend ? plainBlendGains(p)
            : std::array<double, 2> { std::cos(p * halfPi), std::sin(p * halfPi) };
        const double ga = gains[0] * outgoingBaseGain;
        const double gb = gains[1] * incomingBaseGain;
        if (parameters_.style == Style::LearnedEq) {
            auto gains = curve_.gainsAt(p);
            // ECHO commits an unfiltered incoming deck. Ease the last 10% to
            // unity so QPlayer's unconstrained terminal controls cannot step.
            const double release = smooth(static_cast<double>((p - 0.9) / 0.1));
            for (int c = 0; c < count; ++c) {
                auto& state = channels_[c];
                const double lo = state.lowB.process(b[c]);
                const double mid = state.midB.process(b[c] - lo), hi = b[c] - lo - mid;
                out[c] = a[c] * ga + (lo * (gains[0] + (1 - gains[0]) * release)
                    + mid * (gains[1] + (1 - gains[1]) * release)
                    + hi * (gains[2] + (1 - gains[2]) * release)) * gb;
            }
            return;
        }
        if (parameters_.style == Style::BeatCut) {
            for (int c = 0; c < count; ++c) out[c] = a[c] * ga + b[c] * gb;
            return;
        }
        double pa = 0, pb = 0;
        for (int c = 0; c < count; ++c) { pa += a[c] * a[c]; pb += b[c] * b[c]; }
        const double inv = count > 0 ? 1.0 / count : 0;
        powerA_ += (pa * inv - powerA_) * powerAlpha_;
        powerB_ += (pb * inv - powerB_) * powerAlpha_;
        constexpr double maxGain = 1.995262315;
        const double target = powerA_ > 1e-10 && powerB_ > 1e-10
            ? std::clamp(std::sqrt(powerA_ / powerB_), 1 / maxGain, maxGain) : 1;
        levelGain_ += (target - levelGain_) * gainAlpha_;
        const double strength = static_cast<double>(std::sin(3.141592653589793 * p));
        const double match = 1 + (levelGain_ - 1) * strength;
        // Pool low-frequency power across all channels. Tracking channel zero
        // alone misses right-panned bass and changes the mix when L/R swap.
        std::array<double, maximumChannels> lowsA {}, lowsB {};
        double lowFramePowerA = 0, lowFramePowerB = 0;
        for (int c = 0; c < count; ++c) {
            auto& state = channels_[c];
            lowsA[c] = state.lowA.process(a[c]); lowsB[c] = state.lowB.process(b[c]);
            lowFramePowerA += lowsA[c] * lowsA[c]; lowFramePowerB += lowsB[c] * lowsB[c];
        }
        lowPowerA_ += (lowFramePowerA * inv - lowPowerA_) * lowPowerAlpha_;
        lowPowerB_ += (lowFramePowerB * inv - lowPowerB_) * lowPowerAlpha_;
        const double lowPowerSum = lowPowerA_ + lowPowerB_;
        const double duckTarget = lowPowerSum > 1e-10
            ? std::clamp(std::max(lowPowerA_, lowPowerB_) / lowPowerSum, 0.5, 1.0) : 1;
        duckGain_ += (duckTarget - duckGain_) * lowGainAlpha_;
        const double duck = 1 + (duckGain_ - 1) * strength;
        for (int c = 0; c < count; ++c) {
            out[c] = ((a[c] - lowsA[c]) + lowsA[c] * duck) * ga
                + ((b[c] - lowsB[c]) + lowsB[c] * duck) * gb * match;
        }
    }
private:
    static constexpr double halfPi = 1.5707963267948966;
    static double smooth(double value) noexcept { const double x = std::clamp(value, 0.0, 1.0); return x * x * (3 - 2 * x); }
    double smoothing(double seconds) const noexcept { return static_cast<double>(1 - std::exp(-1 / (seconds * rate_))); }
    template <typename Sample>
    void bassSwap(const Sample* a, const Sample* b, Sample* out, int count, double p, double baseA, double baseB) noexcept {
        // MixTuning: together=.5, split=.5; AiMix: high-frequency stagger=.35.
        const double theta = p < 0.25 ? p / 0.25 * halfPi / 2
            : p < 0.75 ? halfPi / 2 : halfPi / 2 + (p - 0.75) / 0.25 * halfPi / 2;
        const double thetaLow = std::clamp((p - 0.25) / 0.5, 0.0, 1.0) * halfPi;
        const double stagger = std::min(1.0, p / 0.5) * halfPi;
        const double highA = static_cast<double>(0.65 * std::cos(theta) + 0.35 * std::cos(stagger)) * baseA;
        const double highB = static_cast<double>(0.65 * std::sin(theta) + 0.35 * std::sin(stagger)) * baseB;
        const double lowA = static_cast<double>(std::cos(thetaLow)) * baseA, lowB = static_cast<double>(std::sin(thetaLow)) * baseB;
        for (int c = 0; c < count; ++c) {
            auto& state = channels_[c];
            const double la = state.lowA.process(a[c]), lb = state.lowB.process(b[c]);
            out[c] = la * lowA + lb * lowB + (a[c] - la) * highA + (b[c] - lb) * highB;
        }
    }
    template <typename Sample>
    void showMode(const Sample* a, const Sample* b, Sample* out, int count, double p, double baseA, double baseB) noexcept {
        const int bucket = static_cast<int>(p * 1000);
        if (bucket != sweepBucket_) {
            sweepBucket_ = bucket;
            for (int c = 0; c < count; ++c) {
                channels_[c].highA.highPass(rate_, 20 + 3980 * p * p, 0.707);
                channels_[c].highB.lowPass(rate_, 300 + 17700 * p * p * p, 0.707);
            }
        }
        const double ga = static_cast<double>(std::sqrt(1 - p)) * baseA;
        const double gb = static_cast<double>(std::pow(p, 0.6)) * baseB;
        const double engage = smooth(static_cast<double>(p / 0.1)), release = smooth(static_cast<double>((p - 0.9) / 0.1));
        for (int c = 0; c < count; ++c) {
            const double filteredA = channels_[c].highA.process(a[c]), filteredB = channels_[c].highB.process(b[c]);
            out[c] = (a[c] + (filteredA - a[c]) * engage) * ga
                + (filteredB + (b[c] - filteredB) * release) * gb;
        }
    }
    struct Channel { Biquad lowA, lowB, midB, highA, highB; };
    std::array<Channel, maximumChannels> channels_ {};
    Parameters parameters_;
    EqCurve curve_;
    double rate_ = 48000;
    double powerAlpha_ = 0, gainAlpha_ = 0, lowPowerAlpha_ = 0, lowGainAlpha_ = 0;
    double powerA_ = 0, powerB_ = 0, lowPowerA_ = 0, lowPowerB_ = 0, levelGain_ = 1, duckGain_ = 1;
    int sweepBucket_ = -1;
    double bassHandoverHz_ = -1;
};
}
