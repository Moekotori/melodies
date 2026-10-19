#pragma once

#include "HostCommon.h"
#include "AudioDaemon.h"
#include "RpcOutputWriter.h"
#include <chrono>
#include <filesystem>
#include <fstream>
#include <stdexcept>

namespace native_playback_tests
{
inline void require(bool condition, const char* message)
{
    if (!condition) throw std::runtime_error(message);
}

struct PipelineFixture
{
    echo::EqProcessor eq;
    echo::ChannelBalanceProcessor balance;
    PcmRingAudioSource pcm;
    DopRingSource dop;
    NativeDsdRingSource dsd;
    NativePlaybackPipeline pipeline;

    explicit PipelineFixture(int capacity = 16'384, int channels = 1)
        : pcm(channels, capacity, 0, 0, 1.0f, eq, balance),
          dop(channels, capacity, 0, 0), dsd(channels, capacity * 2, 0, 0),
          pipeline(pcm, dop, dsd, channels)
    {
        pcm.prepareForNativeRender(capacity, 176'400.0);
    }

    void configure(NativePlaybackPipeline::OutputFormat format, int firTaps = 3)
    {
        NativePlaybackPipeline::ProcessingConfig config;
        config.outputFormat = format;
        config.sdm.sourceSampleRate = 88'200;
        config.sdm.targetSampleRate = 176'400;
        config.sdm.oversampling.sourceSampleRate = 88'200;
        config.sdm.oversampling.targetSampleRate = 176'400;
        config.sdm.oversampling.stages = {
            {2, std::vector<float>(static_cast<size_t>(firTaps), 1.0f / firTaps)} };
        if (format == NativePlaybackPipeline::OutputFormat::Pcm)
        {
            config.echoSrc = config.sdm.oversampling;
            config.sdm.oversampling = {};
        }
        std::string error;
        if (!pipeline.configure(config, error)) throw std::runtime_error(error);
    }

    AudioDaemon::SourceHooks sourceHooks()
    {
        AudioDaemon::SourceHooks hooks;
        hooks.beginSession = [&] { pipeline.beginSession(); };
        hooks.continueSessionAfterDrain = [&] { pipeline.continueSessionAfterDrain(); };
        hooks.markInputEnded = [&] { pipeline.markInputEnded(); };
        hooks.requestStop = [&] { pipeline.requestStop(); };
        hooks.setPaused = [&](bool paused) { pipeline.setPaused(paused); };
        hooks.replaceBufferedAudio = [&](const float* input, int frames, bool paused) {
            return pipeline.replaceBufferedAudio(input, frames, paused);
        };
        hooks.replaceBufferedAudioWithPreroll = [&](const float* input, int frames, int preroll, bool paused) {
            return pipeline.replaceBufferedAudioWithPreroll(input, frames, preroll, paused);
        };
        hooks.push = [&](const float* input, int frames) { return pipeline.push(input, frames); };
        hooks.pushForGeneration = [&](const float* input, int frames, uint64_t generation) {
            return pipeline.pushForGeneration(input, frames, generation);
        };
        hooks.cancelAutomix = [&] { pipeline.cancelAutomix(); };
        hooks.cancelGapless = [&] { pipeline.cancelGapless(); };
        hooks.generation = [&] { return pipeline.generation(); };
        hooks.decoderSampleRateFor = [&](int rate) { return pipeline.decoderSampleRate(rate); };
        hooks.seekPrerollFrames = [&] { return pipeline.seekPrerollFrames(); };
        hooks.requestStopForGeneration = [&](uint64_t generation) { return pipeline.requestStopForGeneration(generation); };
        hooks.isDrained = [&] { return pipeline.isDrained(); };
        hooks.allowTailDrainAfterStop = [&] { pipeline.allowTailDrainAfterStop(); };
        hooks.requiresRenderedStopDrain = [&] {
            return pipeline.outputFormat() == NativePlaybackPipeline::OutputFormat::NativeDsd;
        };
        hooks.markInputEndedUntil = [&](std::chrono::steady_clock::time_point deadline) {
            return pipeline.markInputEndedUntil(deadline);
        };
        hooks.playbackClockFrames = [&] {
            return std::pair<uint64_t, uint64_t>{pipeline.getSourceTrackFrames(), pipeline.getDeviceOutputFrames()};
        };
        hooks.installDualTrackMix = [&](std::shared_ptr<DualTrackMixBuffer> mix) { pcm.installDualTrackMix(std::move(mix)); };
        return hooks;
    }
};

struct WavFixture
{
    std::filesystem::path path;
    WavFixture()
    {
        const auto root = std::filesystem::path(__FILE__).parent_path().parent_path().parent_path().parent_path()
            / "misc" / "native-seek-tests";
        std::filesystem::create_directories(root);
        path = root / ("src-" + std::to_string(std::chrono::steady_clock::now().time_since_epoch().count()) + ".wav");
        write();
    }
    void write() const
    {
        std::ofstream file(path, std::ios::binary | std::ios::trunc);
        const auto word = [&](uint32_t value, int bytes) {
            for (int byte = 0; byte < bytes; ++byte) file.put(static_cast<char>((value >> (byte * 8)) & 0xff));
        };
        constexpr uint32_t dataBytes = 88'200 * 2;
        file.write("RIFF", 4); word(36 + dataBytes, 4); file.write("WAVEfmt ", 8);
        word(16, 4); word(1, 2); word(1, 2); word(88'200, 4); word(176'400, 4);
        word(2, 2); word(16, 2); file.write("data", 4); word(dataBytes, 4);
        for (uint32_t frame = 0; frame < 88'200; ++frame) word(1'000, 2);
        require(file.good(), "test WAV must be written");
    }
    ~WavFixture() { std::error_code ignored; std::filesystem::remove(path, ignored); }
};

inline std::shared_ptr<RpcOutputWriter> discardRpcWriter()
{
    return std::make_shared<RpcOutputWriter>(-1, [](const char*, size_t bytes) {
        return static_cast<std::ptrdiff_t>(bytes);
    });
}
} // namespace native_playback_tests
