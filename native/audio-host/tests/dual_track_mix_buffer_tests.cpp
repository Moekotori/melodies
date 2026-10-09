#include "../src/DualTrackMixBuffer.h"
#include <cassert>
#include <cmath>

int main()
{
    DualTrackMixBuffer mix(48000, 2, 0.5f);
    std::vector<float> b(2048, -1.0f);
    echo::FloatAudioBuffer output(2, 1024);
    for (int channel = 0; channel < 2; ++channel)
        std::fill_n(output.getWritePointer(channel), 1024, 1.0f);
    assert(mix.push(b.data(), 1024));
    mix.mix(output, 0, 1024);
    assert(std::abs(output.getReadPointer(0)[0]) < 1e-6f);

    // A live ratio change ramps on the consuming thread, rather than in pre-read PCM.
    mix.setBalance(1.0f);
    for (int channel = 0; channel < 2; ++channel)
        std::fill_n(output.getWritePointer(channel), 1024, 1.0f);
    assert(mix.push(b.data(), 1024));
    mix.mix(output, 0, 1024);
    const auto* samples = output.getReadPointer(0);
    assert(std::abs(samples[0]) < 0.01f);
    assert(std::abs(samples[1023] + 0.70710678f) < 1e-5f);
    for (int frame = 1; frame < 1024; ++frame)
        assert(std::abs(samples[frame] - samples[frame - 1]) < 0.0031f);

    // Consume beyond the fixed ring's capacity to check wraparound without growth.
    for (int block = 0; block < 400; ++block) {
        assert(mix.push(b.data(), 1024));
        mix.mix(output, 0, 1024);
        assert(std::abs(output.getReadPointer(1)[1023] + 0.70710678f) < 1e-5f);
    }
    mix.cancel();
    assert(!mix.push(b.data(), 1024));
}
