#include "NativePlaybackTestFixture.h"

#include <algorithm>
#include <cmath>
#include <vector>

using namespace native_playback_tests;

namespace
{
void testSeekClockAndOwnership(NativePlaybackPipeline::OutputFormat format)
{
    PipelineFixture fixture;
    fixture.configure(format);
    fixture.pipeline.beginSession();
    const uint64_t oldGeneration = fixture.pipeline.generation();
    const std::vector<float> input(8, 0.1f);
    require(fixture.pipeline.push(input.data(), 8), "original source must queue");
    std::vector<uint32_t> dopOutput(16);
    std::vector<uint8_t> dsdOutput(32);
    if (format == NativePlaybackPipeline::OutputFormat::Dop)
        require(fixture.dop.renderInterleaved(dopOutput.data(), 16, 1) == 16, "original DoP must render");
    else
        require(fixture.dsd.renderInterleaved(dsdOutput.data(), 32, 1) == 32, "original DSD must render");
    require(fixture.pipeline.getSourceTrackFrames() > 0, "original playback must advance the clock");
    fixture.pipeline.requestStopForGeneration(oldGeneration);
    require(fixture.pipeline.replaceBufferedAudio(input.data(), 8, true) == 8, "seek buffer must replace");
    require(fixture.pipeline.getSourceTrackFrames() == 0, "seek must reset the media clock");
    require(fixture.pipeline.generation() != oldGeneration, "seek must advance source ownership");
    require(!fixture.pipeline.requestStopForGeneration(oldGeneration), "old teardown must not stop sought transport");
    require(!fixture.pipeline.pushForGeneration(input.data(), 1, oldGeneration), "old PCM must not enter sought transport");
    if (format == NativePlaybackPipeline::OutputFormat::Dop)
        require(fixture.dop.renderInterleaved(dopOutput.data(), 16, 1) == 0, "paused DoP seek must retain audio");
    else
        require(fixture.dsd.renderInterleaved(dsdOutput.data(), 32, 1) == 0, "paused DSD seek must retain audio");
    fixture.pipeline.setPaused(false);
    if (format == NativePlaybackPipeline::OutputFormat::Dop)
        require(fixture.dop.renderInterleaved(dopOutput.data(), 16, 1) == 16, "DoP seek must resume");
    else
        require(fixture.dsd.renderInterleaved(dsdOutput.data(), 32, 1) == 32, "DSD seek must resume");
}

void testPreroll(NativePlaybackPipeline::OutputFormat format, int taps)
{
    PipelineFixture expected;
    PipelineFixture sought;
    expected.configure(format, taps);
    sought.configure(format, taps);
    expected.pipeline.beginSession();
    sought.pipeline.beginSession(true);
    const int preroll = sought.pipeline.seekPrerollFrames();
    require(preroll > 0, "SDM seek must request FIR and pre-modulator history");
    if (taps > 4'096) require(preroll > 4'096, "long FIR must exercise multiple warmup blocks");
    constexpr int bodyFrames = 128;
    std::vector<float> input(static_cast<size_t>(preroll + bodyFrames));
    for (size_t frame = 0; frame < input.size(); ++frame)
        input[frame] = static_cast<float>(0.15 * std::sin(static_cast<double>(frame) * 0.07));
    require(expected.pipeline.push(input.data(), preroll), "reference history must queue");
    require(expected.pipeline.push(input.data() + preroll, bodyFrames), "reference body must queue");
    require(sought.pipeline.replaceBufferedAudioWithPreroll(input.data(), preroll + bodyFrames, preroll, true) == bodyFrames,
        "seek must acknowledge only post-target source frames");
    require(sought.pipeline.getSourceTrackFrames() == 0, "preroll must not advance the seek clock");
    sought.pipeline.setPaused(false);
    if (format == NativePlaybackPipeline::OutputFormat::Dop)
    {
        std::vector<uint32_t> reference(static_cast<size_t>((preroll + bodyFrames) * 2));
        std::vector<uint32_t> actual(bodyFrames * 2);
        require(expected.dop.renderInterleaved(reference.data(), static_cast<uint32_t>(reference.size()), 1) == reference.size(),
            "reference DoP must render");
        require(sought.dop.renderInterleaved(actual.data(), static_cast<uint32_t>(actual.size()), 1) == actual.size(),
            "sought DoP must render only the body");
        for (size_t frame = 0; frame < actual.size(); ++frame)
            require((actual[frame] & 0xffffu) == (reference[static_cast<size_t>(preroll * 2) + frame] & 0xffffu),
                "seek DoP payload must match the warmed product pipeline");
    }
    else
    {
        std::vector<uint8_t> reference(static_cast<size_t>((preroll + bodyFrames) * 4));
        std::vector<uint8_t> actual(bodyFrames * 4);
        require(expected.dsd.renderInterleaved(reference.data(), static_cast<uint32_t>(reference.size()), 1) == reference.size(),
            "reference DSD must render");
        require(sought.dsd.renderInterleaved(actual.data(), static_cast<uint32_t>(actual.size()), 1) == actual.size(),
            "sought DSD must render only the body");
        require(std::equal(actual.begin(), actual.end(), reference.begin() + preroll * 4),
            "seek DSD bits must match the warmed product pipeline");
    }
    require(sought.pipeline.getReadyFrames() == 0, "preroll must not leave extra transport audio");
}
} // namespace

void testNativeSdmSeek()
{
    for (const auto format : {NativePlaybackPipeline::OutputFormat::Dop, NativePlaybackPipeline::OutputFormat::NativeDsd})
    {
        testSeekClockAndOwnership(format);
        testPreroll(format, 3);
    }
    testPreroll(NativePlaybackPipeline::OutputFormat::Dop, 8'191);
}
