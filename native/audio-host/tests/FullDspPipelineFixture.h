#pragma once
#include "NativePlaybackTestFixture.h"

namespace native_playback_tests
{
// The product constructor: every PCM processor has its own real runtime.
struct FullDspPipelineFixture
{
    echo::EqProcessor eq;
    echo::ConvolutionProcessor convolution;
    echo::ChannelBalanceProcessor balance;
    echo::DspHeadroomProcessor headroom;
    echo::ReplayGainProcessor replayGain;
    echo::CompressorProcessor compressor;
    echo::SpatialDspProcessor spatial;
    echo::PlaybackRateProcessor rate;
    echo::LevelMeterProcessor meter;
    echo::DspRackOrder rack;
    echo::WorkshopAudioEffectController workshop;
    echo::MultibandCompressorProcessor multiband;
    echo::DeEsserProcessor deEsser;
    PcmRingAudioSource pcm;
    DopRingSource dop;
    NativeDsdRingSource dsd;
    NativePlaybackPipeline pipeline;

    explicit FullDspPipelineFixture(int capacity = 16'384)
        : pcm(2, capacity, 0, 0, 1.0f, eq, convolution, balance, headroom,
              replayGain, compressor, spatial, rate, meter, &rack, &workshop, &multiband, &deEsser),
          dop(2, capacity, 0, 0),
          dsd(2, capacity * 2, 0, 0), pipeline(pcm, dop, dsd, 2,
              eq, convolution, balance, headroom, replayGain, compressor, spatial, rate, meter,
              &rack, &workshop, &multiband, &deEsser)
    {
        pcm.prepareForNativeRender(capacity, 176'400);
    }
};

struct ImpulseWavFixture
{
    std::filesystem::path path;
    ImpulseWavFixture()
    {
        const auto root = std::filesystem::path(__FILE__).parent_path().parent_path().parent_path().parent_path()
            / "misc" / "sdm-transaction-tests";
        std::filesystem::create_directories(root);
        path = root / ("ir-" + std::to_string(std::chrono::steady_clock::now().time_since_epoch().count()) + ".wav");
        std::ofstream file(path, std::ios::binary | std::ios::trunc);
        const auto word = [&](uint32_t value, int bytes) {
            for (int byte = 0; byte < bytes; ++byte) file.put(static_cast<char>((value >> (byte * 8)) & 0xff));
        };
        constexpr int frames = 1024, bytes = frames * 2;
        file.write("RIFF", 4); word(36 + bytes, 4); file.write("WAVEfmt ", 8);
        word(16, 4); word(1, 2); word(1, 2); word(88'200, 4); word(176'400, 4);
        word(2, 2); word(16, 2); file.write("data", 4); word(bytes, 4);
        for (int frame = 0; frame < frames; ++frame)
            word(frame == 0 ? 16384 : frame == 311 ? 4096 : frame == 900 ? 2048 : 0, 2);
        require(file.good(), "real convolution IR must be written");
    }
    ~ImpulseWavFixture() { std::error_code ignored; std::filesystem::remove(path, ignored); }
};
}
