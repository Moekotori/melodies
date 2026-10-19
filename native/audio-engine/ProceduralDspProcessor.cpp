#include "ProceduralDsp.h"
#include <complex>

namespace echo {
struct ProceduralDspProcessor::Runtime {
    ProceduralConfig config {};
    std::array<std::array<BiquadCoefficients, proceduralMaxBands>, proceduralMaxOps> coefficients {};
    std::array<std::array<std::array<BiquadState, proceduralMaxBands>, proceduralMaxChannels>, proceduralMaxOps> filters {};
    struct Delay { size_t offset = 0, length = 0, position = 0, filled = 0; };
    std::array<std::array<Delay, proceduralMaxChannels>, proceduralMaxOps> delays {};
    std::vector<double> delayPool;
    int64_t tail = 0, tailLimit = 0;

    void reset() noexcept {
        for (auto& op : filters) for (auto& channel : op) for (auto& filter : channel) filter.reset();
        for (auto& op : delays) for (auto& delay : op) { delay.position = 0; delay.filled = 0; }
        tail = 0;
    }
    void configure(const ProceduralConfig& next, double rate, int channelCount) noexcept {
        config = next; reset(); tailLimit = 0;
        size_t offset = 0;
        if (!config.enabled) return;
        for (int i = 0; i < config.count; ++i) {
            const auto& step = config.steps[i];
            if (!step.enabled) continue;
            int64_t stepTail = 0;
            if (step.kind == ProceduralKind::Delay) {
                const auto length = static_cast<size_t>(std::llround(rate * step.delayMs / 1000.0));
                for (int c = 0; c < channelCount; ++c) {
                    auto& d = delays[i][c]; d.length = 0;
                    if (!(step.channels & (uint32_t(1) << c))) continue;
                    if (offset + length <= delayPool.size()) { d.offset = offset; d.length = length; offset += length; }
                }
                stepTail = static_cast<int64_t>(length);
            } else if (step.kind == ProceduralKind::Eq || step.kind == ProceduralKind::Iir) {
                const int count = step.kind == ProceduralKind::Iir ? 1 : step.bandCount;
                for (int b = 0; b < count; ++b) {
                    const auto& band = step.bands[b];
                    auto& coeff = coefficients[i][b];
                    coeff = step.kind == ProceduralKind::Iir ? step.iir : !band.enabled ? BiquadCoefficients{} : band.type == 6
                        ? makeBandpassCoefficients(rate, band.hz, band.q)
                        : makeEqCoefficients(rate, band.hz, band.db, band.q, static_cast<EqFilterType>(band.type), true);
                    const auto discriminant = std::sqrt(std::complex<double>(coeff.a1 * coeff.a1 - 4 * coeff.a2, 0));
                    const double radius = std::max(std::abs((-coeff.a1 + discriminant) / 2.0), std::abs((-coeff.a1 - discriminant) / 2.0));
                    if (radius > 0 && radius < 1)
                        stepTail += static_cast<int64_t>(std::min(rate * 30, std::ceil(std::log(1.e-9) / std::log(radius))));
                }
            }
            tailLimit = std::min(static_cast<int64_t>(rate * 30), tailLimit + stepTail);
        }
    }
    void process(std::array<double, proceduralMaxChannels>& frame, int channelCount) noexcept {
        if (!config.enabled) return;
        bool input = false;
        for (int c = 0; c < channelCount; ++c) input = input || std::abs(frame[c]) > 1.e-12;
        tail = input ? tailLimit : std::max<int64_t>(0, tail - 1);
        for (int i = 0; i < config.count; ++i) {
            const auto& step = config.steps[i];
            if (!step.enabled) continue;
            if (step.kind == ProceduralKind::Mix) {
                const auto source = frame; // All routes read the same pre-operation frame.
                uint32_t written = 0;
                for (int r = 0; r < step.routeCount; ++r) {
                    const auto& route = step.routes[r];
                    if (route.output >= channelCount || !(step.channels & (uint32_t(1) << route.output))) continue;
                    const uint32_t bit = uint32_t(1) << route.output;
                    if (!(written & bit)) { frame[route.output] = 0; written |= bit; }
                    if (route.input < channelCount) frame[route.output] += source[route.input] * route.gain;
                }
                continue;
            }
            for (int c = 0; c < channelCount; ++c) {
                if (!(step.channels & (uint32_t(1) << c))) continue;
                auto& sample = frame[c];
                switch (step.kind) {
                    case ProceduralKind::Eq:
                        sample *= step.gain;
                        for (int b = 0; b < step.bandCount; ++b)
                            if (step.bands[b].enabled) sample = filters[i][c][b].processDouble(sample, coefficients[i][b]);
                        break;
                    case ProceduralKind::Iir: sample = filters[i][c][0].processDouble(sample, coefficients[i][0]); break;
                    case ProceduralKind::Gain: sample *= step.gain; break;
                    case ProceduralKind::Invert: sample = -sample; break;
                    case ProceduralKind::Mute: sample = 0; break;
                    case ProceduralKind::Delay: {
                        auto& d = delays[i][c];
                        if (!d.length) break;
                        const auto delayed = d.filled >= d.length ? delayPool[d.offset + d.position] : 0.f;
                        delayPool[d.offset + d.position] = sample;
                        sample = delayed;
                        d.position = (d.position + 1) % d.length;
                        d.filled = std::min(d.filled + 1, d.length);
                        break;
                    }
                    case ProceduralKind::Mix: break;
                }
                if (!std::isfinite(sample)) sample = 0;
            }
        }
    }
};
ProceduralDspProcessor::ProceduralDspProcessor(ProceduralDspController& c)
    : controller_(c), current_(std::make_unique<Runtime>()), previous_(std::make_unique<Runtime>()) {}
ProceduralDspProcessor::~ProceduralDspProcessor() = default;
void ProceduralDspProcessor::prepare(double sampleRate, int channels, double maximumChannelDelayMs) {
    sampleRate_ = std::clamp(sampleRate, 8000.0, 768000.0);
    channels_ = std::clamp(channels, 0, proceduralMaxChannels);
    // Four seconds summed across all operation/channel pairs. Allocated only
    // during prepare, released with the processor; 3 MiB total at 48 kHz.
    const auto capacity = static_cast<size_t>(std::ceil(sampleRate_ * std::clamp(maximumChannelDelayMs, 0.0, 4000.0) / 1000.0)) + proceduralMaxOps * proceduralMaxChannels;
    current_->delayPool.resize(capacity); previous_->delayPool.resize(capacity);
    fadeLength_ = std::max(1, static_cast<int>(sampleRate_ * 0.005));
    revision_ = UINT64_MAX;
    controller_.tryRead(pending_, revision_);
    current_->configure(pending_, sampleRate_, channels_);
    previous_->configure({}, sampleRate_, channels_);
    fade_ = 0;
}
void ProceduralDspProcessor::reset() noexcept { current_->reset(); previous_->reset(); fade_ = 0; }
bool ProceduralDspProcessor::isActive() const noexcept { return controller_.enabled() || current_->config.enabled || fade_ > 0; }
int64_t ProceduralDspProcessor::tailFramesRemaining() const noexcept { return std::max(current_->tail, fade_ > 0 ? previous_->tail : int64_t(0)); }
template <typename Sample>
void ProceduralDspProcessor::processBlock(PlanarAudioBuffer<Sample>& buffer, int start, int count) noexcept {
    // Do not replace the outgoing graph in the middle of a transition. Rapid
    // edits coalesce to the most recent complete publication after five ms.
    if (!fade_ && controller_.tryRead(pending_, revision_)) {
        current_.swap(previous_);
        current_->configure(pending_, sampleRate_, channels_);
        fade_ = fadeLength_;
    }
    if (!current_->config.enabled && !fade_) return;
    const int channels = std::min(channels_, buffer.getNumChannels());
    std::array<Sample*, proceduralMaxChannels> output {};
    for (int c = 0; c < channels; ++c) output[c] = buffer.getWritePointer(c, start);
    for (int s = 0; s < count; ++s) {
        std::array<double, proceduralMaxChannels> frame {};
        for (int c = 0; c < channels; ++c) frame[c] = output[c][s];
        auto outgoing = frame;
        current_->process(frame, channels);
        if (fade_ > 0) {
            previous_->process(outgoing, channels);
            const double wet = 1.0 - static_cast<double>(fade_ - 1) / fadeLength_;
            for (int c = 0; c < channels; ++c) frame[c] = outgoing[c] + (frame[c] - outgoing[c]) * wet;
            --fade_;
        }
        for (int c = 0; c < channels; ++c) output[c][s] = std::isfinite(frame[c]) ? static_cast<double>(std::clamp(frame[c], -1.e12, 1.e12)) : 0.f;
    }
}

template void ProceduralDspProcessor::processBlock<float>(PlanarAudioBuffer<float>&, int, int) noexcept;
template void ProceduralDspProcessor::processBlock<double>(PlanarAudioBuffer<double>&, int, int) noexcept;
} // namespace echo
