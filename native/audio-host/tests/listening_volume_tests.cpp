#include "listening_volume_tests.h"
#include "../src/PcmRingAudioSource.h"
#include "../src/RealtimeProgramPcmSink.h"

#include <array>
#include <cmath>
#include <stdexcept>
#include <vector>

namespace listening_volume_tests {
namespace {
constexpr int frames = 480;
constexpr int channels = 2;
using Block = std::array<float, frames * channels>;

void require(bool value, const char* message) {
    if (!value) throw std::runtime_error(message);
}

struct Capture final : echo::listen_together::RealtimeProgramPcmSink {
    Block block {};
    void onProgramPcm(const echo::FloatAudioBuffer& audio, int start, int count, int) noexcept override {
        for (int c = 0; c < channels; ++c)
            for (int f = 0; f < count && f < frames; ++f)
                block[c * frames + f] = audio.getSample(c, start + f);
    }
};

struct Result { std::array<Block, 10> shared {}, local {}; };

Result render(float volume, float rate, bool changing, bool attach = true) {
    Capture capture;
    echo::EqProcessor eq;
    echo::ChannelBalanceProcessor balance;
    PcmRingAudioSource source(channels, 16384, 0, 0, volume, eq, balance);
    source.prepareForNativeRender(frames, 48000);
    source.beginSession();
    if (attach) source.setRealtimeProgramPcmSink(&capture);
    require(source.getRateProcessor()->configure(rate, echo::SpeedMode::Nightcore), "volume test rate configuration");
    std::vector<float> input(12000 * channels);
    for (int i = 0; i < 12000; ++i) {
        input[i * 2] = static_cast<float>(0.2 * std::sin(i * 2.0 * 3.141592653589793 * 440 / 48000));
        input[i * 2 + 1] = static_cast<float>(0.15 * std::sin(i * 2.0 * 3.141592653589793 * 660 / 48000));
    }
    require(source.push(input.data(), 12000), "volume test source PCM must fit its bounded FIFO");
    echo::FloatAudioBuffer output(channels, frames + 8);
    Result result;
    for (int block = 0; block < 10; ++block) {
        if (changing && block == 4) source.setGain(0.0f);
        if (changing && block == 7) source.setGain(0.4f);
        output.clear();
        require(source.renderPlanar(output, 4, frames) == frames, "volume test must produce every output frame");
        result.shared[block] = capture.block;
        for (int c = 0; c < channels; ++c) {
            for (int f = 0; f < frames; ++f) result.local[block][c * frames + f] = output.getSample(c, 4 + f);
            require(output.getSample(c, 0) == 0 && output.getSample(c, frames + 7) == 0,
                "local gain must respect the callback sample range");
        }
    }
    source.setRealtimeProgramPcmSink(nullptr);
    return result;
}

void same(const Block& actual, const Block& expected, float gain, const char* message) {
    for (std::size_t i = 0; i < actual.size(); ++i)
        require(std::abs(actual[i] - expected[i] * gain) < 0.00001f, message);
}
}

void sharedVolumeIsIndependent() {
    for (const float rate : {1.0f, 0.5f, 1.5f, 2.0f}) {
        const auto reference = render(1.0f, rate, false);
        double energy = 0;
        for (float value : reference.shared.back()) energy += value * value;
        require(energy > 1.0, "shared reference PCM must be non-silent");
        for (const float gain : {0.0f, 0.25f, 0.5f, 1.0f}) {
            const auto shared = render(gain, rate, false);
            const auto withoutTap = render(gain, rate, false, false);
            for (int block = 2; block < 10; ++block) {
                same(shared.shared[block], reference.shared[block], 1, "host volume must not attenuate shared PCM");
                same(shared.local[block], reference.local[block], gain, "local volume must still apply exactly once");
                same(shared.local[block], withoutTap.local[block], 1, "attaching a shared tap must not change local output");
            }
        }
    }
}

void volumeChangesDoNotChangeSharedHistory() {
    for (const float rate : {1.0f, 0.5f, 1.5f, 2.0f}) {
        const auto reference = render(1.0f, rate, false);
        const auto changed = render(0.25f, rate, true);
        for (int block = 2; block < 10; ++block) {
            const float gain = block < 4 ? 0.25f : block < 7 ? 0.0f : 0.4f;
            same(changed.shared[block], reference.shared[block], 1, "mute/unmute must not alter shared resampler history");
            same(changed.local[block], reference.local[block], gain, "live local volume must apply after the shared tap");
        }
    }
}
}
