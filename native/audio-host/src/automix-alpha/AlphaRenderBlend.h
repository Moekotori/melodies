#pragma once
#include "AlphaMixDsp.h"
#include <cstdint>

namespace echo::automix_alpha {
// Adapts QPlayer's raw two-track mixer to the host's already weighted Deck A.
// No additional PCM buffer: fixed per-frame scratch and filter state only.
class RenderBlend {
public:
    bool needsPrepare(uint64_t lease) const noexcept { return lease_ != lease; }
    void prepare(uint64_t lease, const Parameters& parameters, double rate, int frames) noexcept {
        if (lease_ == lease) return;
        lease_ = lease; lastProgress_ = 0;
        dsp_.configure(parameters, rate, frames);
    }
    template <typename Sample>
    void mixFrame(const Sample* weightedA, const Sample* b, Sample* output, int channels,
        double progress, double envelopeA, double envelopeB, double baseA, double baseB,
        bool recovering, double recoveryProgress) noexcept {
        if (recovering && recoveryProgress >= 1) {
            for (int c = 0; c < channels; ++c) output[c] = weightedA[c] + b[c] * envelopeB;
            return;
        }
        std::array<double, MixDsp::maximumChannels> a {}, incoming {}, mixed {};
        if (!recovering) lastProgress_ = progress;
        const double p = recovering ? lastProgress_ : progress;
        for (int c = 0; c < channels; ++c) {
            a[c] = envelopeA > 1e-6 ? weightedA[c] / envelopeA : 0;
            incoming[c] = b[c];
        }
        if (!recovering) {
            dsp_.mixFrame(a.data(), incoming.data(), mixed.data(), channels, p, baseA, baseB);
            for (int c = 0; c < channels; ++c) output[c] = mixed[c];
            return;
        }
        dsp_.mixFrame(a.data(), incoming.data(), mixed.data(), channels, p, baseA, baseB);
        const double wet = 1 - std::clamp(recoveryProgress, 0.0, 1.0);
        for (int c = 0; c < channels; ++c) {
            const double legacy = weightedA[c] + b[c] * envelopeB;
            output[c] = legacy + (mixed[c] - legacy) * wet;
        }
    }
    double lastProgress() const noexcept { return lastProgress_; }
private:
    MixDsp dsp_;
    uint64_t lease_ = 0;
    double lastProgress_ = 0;
};
}
