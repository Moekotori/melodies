#include "ListenTogetherAudioTransport.h"
#include "ListenTogetherOpusCodec.h"

#include <cmath>
#include <cstdlib>
#include <iostream>
#include <numeric>
#include <stdexcept>
#include <string>
#include <vector>

namespace {

void require(bool condition, const std::string& message)
{
    if (!condition)
        throw std::runtime_error(message);
}

std::vector<float> sineFrame()
{
    constexpr double pi = 3.14159265358979323846;
    std::vector<float> samples(echo::listen_together::kFramesPerPacket * 2);
    for (std::size_t frame = 0; frame < echo::listen_together::kFramesPerPacket; ++frame) {
        const auto sample = static_cast<float>(0.25 * std::sin(2.0 * pi * 440.0 * static_cast<double>(frame) / 48'000.0));
        samples[frame * 2] = sample;
        samples[frame * 2 + 1] = sample;
    }
    return samples;
}

double rootMeanSquare(const std::vector<float>& samples)
{
    const double energy = std::accumulate(samples.begin(), samples.end(), 0.0, [](double sum, float value) {
        return sum + static_cast<double>(value) * value;
    });
    return std::sqrt(energy / static_cast<double>(samples.size()));
}

void testRealtimeOpusRoundTrip()
{
    echo::listen_together::OpusEncoder encoder({160'000, 5});
    echo::listen_together::OpusDecoder decoder;
    const auto input = sineFrame();
    const auto encoded = encoder.encode(input.data(), static_cast<int>(echo::listen_together::kFramesPerPacket));
    require(!encoded.empty() && encoded.size() <= echo::listen_together::kMaxOpusPayloadBytes,
        "160 kbps encoder must return one MTU-safe Opus packet");
    require(encoder.frameSize() == static_cast<int>(echo::listen_together::kFramesPerPacket),
        "encoder must use 20 ms frames");

    const auto decoded = decoder.decode(encoded.data(), static_cast<int>(encoded.size()));
    require(decoded.size() == input.size(), "decoder must return one canonical stereo frame");
    const double rms = rootMeanSquare(decoded);
    require(rms > 0.05 && rms < 0.35, "decoded Opus audio must retain bounded non-silent energy");
}

void testEveryQualityHasABoundedPacketBudget()
{
    for (const int bitrate : {128'000, 256'000, 320'000}) {
        echo::listen_together::OpusEncoder encoder({bitrate, 5});
        echo::listen_together::OpusDecoder decoder;
        auto samples = sineFrame();
        for (int block = 0; block < 80; ++block) {
            for (std::size_t i = 0; i < samples.size(); ++i)
                samples[i] = static_cast<float>(0.8 * std::sin((i + block * samples.size()) * (block % 3 ? 0.13 : 0.731)));
            const auto packet = encoder.encode(samples.data(), 960);
            require(packet.size() <= static_cast<std::size_t>(bitrate / 400),
                "each rendition must respect its 20 ms packet budget, including transients");
            require(decoder.decode(packet.data(), static_cast<int>(packet.size())).size() == samples.size(),
                "bounded high-quality packets must remain decodable");
        }
    }
}

void testBitrateBoundary()
{
    bool rejected = false;
    try {
        echo::listen_together::OpusEncoder encoder({32'000, 5});
    } catch (const std::invalid_argument&) {
        rejected = true;
    }
    require(rejected, "music stream must reject a speech-only bitrate configuration");
}

} // namespace

int main()
{
    try {
        testRealtimeOpusRoundTrip();
        testBitrateBoundary();
        testEveryQualityHasABoundedPacketBudget();
        std::cout << "listen-together Opus codec tests passed\n";
        return EXIT_SUCCESS;
    } catch (const std::exception& error) {
        std::cerr << "listen-together Opus codec tests failed: " << error.what() << '\n';
        return EXIT_FAILURE;
    }
}
