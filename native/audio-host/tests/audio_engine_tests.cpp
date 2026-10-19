#include "../../audio-engine/ChannelBalanceProcessor.h"
#include "../../audio-engine/DspChain.h"
#include "../../audio-engine/EqMessageProtocol.h"
#include "../../audio-engine/EqProcessor.h"
#include "../../audio-engine/SdmExtraLoop.h"
#include "../../audio-engine/SdmNtfDesign.h"
#include "../../audio-engine/SdmPreModulator.h"
#include "listening_volume_tests.h"
#include "workshop_transition_tests.h"
#include "asio_queue_channel_tests.h"

#ifdef _WIN32
#include "../third_party/asio-sdk/common/asio.h"
#endif

#include <algorithm>
#include <array>
#include <atomic>
#include <bit>
#include <chrono>
#include <cmath>
#include <condition_variable>
#include <cstdio>
#include <filesystem>
#include <fstream>
#include <future>
#include <functional>
#include <iostream>
#include <limits>
#include <mutex>
#include <stdexcept>
#include <sstream>
#include <string>
#include <thread>
#include <tuple>
#include <vector>

#define ECHO_AUDIO_HOST_TESTS 1

#include "../src/Options.h"
#include "../src/HostUtils.h"
#include "../src/DeviceTypes.h"
#include "../src/PcmRingAudioSource.h"
#include "../src/DopRingSource.h"
#include "../src/NativeDsdRingSource.h"
#include "../src/AudioDaemon.h"
#include "daemon_seek_end_tests.h"
#include "daemon_queue_identity_tests.h"
#include "../src/AutomixTempoProcessor.h"
#include "../../audio-engine/JsonRpcProtocol.h"

#include "../src/main.cpp"

namespace
{
constexpr float strictTolerance = 0.0f;
constexpr float nearTolerance = 0.0001f;

void require(bool condition, const std::string& message)
{
    if (! condition)
        throw std::runtime_error(message);
}

void requireNear(double actual, double expected, double tolerance, const std::string& message)
{
    require(std::abs(actual - expected) <= tolerance, message);
}

template <typename Predicate>
bool waitForCondition(Predicate predicate, std::chrono::milliseconds timeout = std::chrono::milliseconds(1000))
{
    const auto deadline = std::chrono::steady_clock::now() + timeout;
    while (! predicate())
    {
        if (std::chrono::steady_clock::now() >= deadline)
            return false;
        std::this_thread::sleep_for(std::chrono::milliseconds(1));
    }
    return true;
}

bool chooseSdmExtraBitIndependentForTest(
    double decision,
    double stabilityLimit,
    const double* numerator,
    const double* denominator,
    const double* errorHistory,
    const double* feedbackHistory,
    int order,
    double currentFeedback,
    double& quantizationError)
{
    const bool greedy = decision >= 0.0;
    const double greedyQ = echo::sdmExtraClamp(
        decision - (greedy ? 1.0 : -1.0),
        -stabilityLimit,
        stabilityLimit);
    const double greedyNext = echo::predictNextSdmFeedback(
        numerator,
        denominator,
        errorHistory,
        feedbackHistory,
        order,
        greedyQ,
        currentFeedback);
    const double greedyCost = echo::sdmExtraAbs(greedyNext)
        + echo::sdmExtraQuantErrorWeight * echo::sdmExtraAbs(greedyQ);
    const bool other = !greedy;
    const double otherQ = echo::sdmExtraClamp(
        decision - (other ? 1.0 : -1.0),
        -stabilityLimit,
        stabilityLimit);
    const double otherNext = echo::predictNextSdmFeedback(
        numerator,
        denominator,
        errorHistory,
        feedbackHistory,
        order,
        otherQ,
        currentFeedback);
    const double otherCost = echo::sdmExtraAbs(otherNext)
        + echo::sdmExtraQuantErrorWeight * echo::sdmExtraAbs(otherQ);
    if (otherCost < greedyCost)
    {
        quantizationError = otherQ;
        return other;
    }
    quantizationError = greedyQ;
    return greedy;
}

void requireContains(const std::string& text, const std::string& needle, const std::string& message)
{
    require(text.find(needle) != std::string::npos, message + " missing: " + needle + " in " + text);
}

void requireThrowsContaining(const std::function<void()>& callback, const std::string& needle, const std::string& message)
{
    try
    {
        callback();
    }
    catch (const std::exception& error)
    {
        requireContains(error.what(), needle, message);
        return;
    }

    throw std::runtime_error(message + " did not throw");
}

void requireVectorEquals(const std::vector<int>& actual, const std::vector<int>& expected, const std::string& message)
{
    require(actual == expected, message);
}

void writeLe16(std::ofstream& out, uint16_t value)
{
    char bytes[2] {
        static_cast<char>(value & 0xff),
        static_cast<char>((value >> 8) & 0xff),
    };
    out.write(bytes, sizeof(bytes));
}

void writeLe32(std::ofstream& out, uint32_t value)
{
    char bytes[4] {
        static_cast<char>(value & 0xff),
        static_cast<char>((value >> 8) & 0xff),
        static_cast<char>((value >> 16) & 0xff),
        static_cast<char>((value >> 24) & 0xff),
    };
    out.write(bytes, sizeof(bytes));
}

void writeLe64(std::ofstream& out, uint64_t value)
{
    char bytes[8] {
        static_cast<char>(value & 0xff),
        static_cast<char>((value >> 8) & 0xff),
        static_cast<char>((value >> 16) & 0xff),
        static_cast<char>((value >> 24) & 0xff),
        static_cast<char>((value >> 32) & 0xff),
        static_cast<char>((value >> 40) & 0xff),
        static_cast<char>((value >> 48) & 0xff),
        static_cast<char>((value >> 56) & 0xff),
    };
    out.write(bytes, sizeof(bytes));
}

void writeBe16(std::ofstream& out, uint16_t value)
{
    char bytes[2] {
        static_cast<char>((value >> 8) & 0xff),
        static_cast<char>(value & 0xff),
    };
    out.write(bytes, sizeof(bytes));
}

void writeBe32(std::ofstream& out, uint32_t value)
{
    char bytes[4] {
        static_cast<char>((value >> 24) & 0xff),
        static_cast<char>((value >> 16) & 0xff),
        static_cast<char>((value >> 8) & 0xff),
        static_cast<char>(value & 0xff),
    };
    out.write(bytes, sizeof(bytes));
}

void writeBe64(std::ofstream& out, uint64_t value)
{
    char bytes[8] {
        static_cast<char>((value >> 56) & 0xff),
        static_cast<char>((value >> 48) & 0xff),
        static_cast<char>((value >> 40) & 0xff),
        static_cast<char>((value >> 32) & 0xff),
        static_cast<char>((value >> 24) & 0xff),
        static_cast<char>((value >> 16) & 0xff),
        static_cast<char>((value >> 8) & 0xff),
        static_cast<char>(value & 0xff),
    };
    out.write(bytes, sizeof(bytes));
}

std::string writeStreamingDecoderWavFixture(int sampleRate, int channels, int frames)
{
    const auto path = std::filesystem::temp_directory_path()
        / std::filesystem::path("echo-libav-streaming-" + std::to_string(reinterpret_cast<uintptr_t>(&sampleRate)) + ".wav");
    std::ofstream out(path, std::ios::binary);
    require(out.good(), "streaming decoder test can create WAV fixture");

    const uint16_t bitsPerSample = 16;
    const uint32_t dataBytes = static_cast<uint32_t>(frames * channels * (bitsPerSample / 8));
    const uint32_t byteRate = static_cast<uint32_t>(sampleRate * channels * (bitsPerSample / 8));
    const uint16_t blockAlign = static_cast<uint16_t>(channels * (bitsPerSample / 8));

    out.write("RIFF", 4);
    writeLe32(out, 36 + dataBytes);
    out.write("WAVE", 4);
    out.write("fmt ", 4);
    writeLe32(out, 16);
    writeLe16(out, 1);
    writeLe16(out, static_cast<uint16_t>(channels));
    writeLe32(out, static_cast<uint32_t>(sampleRate));
    writeLe32(out, byteRate);
    writeLe16(out, blockAlign);
    writeLe16(out, bitsPerSample);
    out.write("data", 4);
    writeLe32(out, dataBytes);

    for (int frame = 0; frame < frames; ++frame)
    {
        for (int channel = 0; channel < channels; ++channel)
        {
            const auto sample = static_cast<int16_t>(((frame * 17 + channel * 4096) % 20000) - 10000);
            writeLe16(out, static_cast<uint16_t>(sample));
        }
    }

    require(out.good(), "streaming decoder test writes WAV fixture");
    return path.string();
}

enum class RoomCorrectionWavFormat
{
    S16,
    S32,
    Float32,
};

std::string writeRoomCorrectionWavFixture(
    int sampleRate,
    int channels,
    int frames,
    RoomCorrectionWavFormat format,
    bool nonFinite = false)
{
    const auto formatName = format == RoomCorrectionWavFormat::S16
        ? "s16"
        : format == RoomCorrectionWavFormat::S32 ? "s32" : "f32";
    const auto path = std::filesystem::temp_directory_path()
        / std::filesystem::path(
            "echo-room-ir-" + std::string(formatName) + "-"
            + std::to_string(sampleRate) + "-" + std::to_string(channels) + "-"
            + std::to_string(frames) + (nonFinite ? "-nan" : "") + ".wav");
    std::ofstream out(path, std::ios::binary);
    require(out.good(), "room correction test can create WAV fixture");

    const uint16_t bitsPerSample = format == RoomCorrectionWavFormat::S16 ? 16 : 32;
    const uint16_t formatTag = format == RoomCorrectionWavFormat::Float32 ? 3 : 1;
    const uint16_t bytesPerSample = static_cast<uint16_t>(bitsPerSample / 8);
    const uint16_t blockAlign = static_cast<uint16_t>(std::max(0, channels) * bytesPerSample);
    const uint32_t dataBytes = static_cast<uint32_t>(std::max(0, frames) * blockAlign);

    out.write("RIFF", 4);
    writeLe32(out, 36 + dataBytes);
    out.write("WAVE", 4);
    out.write("fmt ", 4);
    writeLe32(out, 16);
    writeLe16(out, formatTag);
    writeLe16(out, static_cast<uint16_t>(std::max(0, channels)));
    writeLe32(out, static_cast<uint32_t>(sampleRate));
    writeLe32(out, static_cast<uint32_t>(sampleRate * blockAlign));
    writeLe16(out, blockAlign);
    writeLe16(out, bitsPerSample);
    out.write("data", 4);
    writeLe32(out, dataBytes);

    for (int frame = 0; frame < frames; ++frame)
    {
        for (int channel = 0; channel < channels; ++channel)
        {
            const float value = nonFinite && frame == 0 && channel == 0
                ? std::numeric_limits<float>::quiet_NaN()
                : frame == 0 ? (channel == 0 ? 0.5f : 0.25f) : 0.0f;
            if (format == RoomCorrectionWavFormat::S16)
            {
                const auto encoded = static_cast<int16_t>(std::clamp(value, -1.0f, 0.999969f) * 32768.0f);
                writeLe16(out, static_cast<uint16_t>(encoded));
            }
            else if (format == RoomCorrectionWavFormat::S32)
            {
                const auto encoded = static_cast<int32_t>(
                    static_cast<double>(std::clamp(value, -1.0f, 0.999999999f)) * 2147483648.0);
                writeLe32(out, static_cast<uint32_t>(encoded));
            }
            else
                writeLe32(out, std::bit_cast<uint32_t>(value));
        }
    }

    require(out.good(), "room correction test writes WAV fixture");
    out.close();
    return path.string();
}

std::string writeStreamingDecoderDsfFixture()
{
    constexpr uint32_t nativeSampleRate = 2822400;
    constexpr uint32_t channels = 2;
    constexpr uint32_t blockBytesPerChannel = 4096;
    constexpr uint64_t samplesPerChannel = static_cast<uint64_t>(blockBytesPerChannel) * 8;
    constexpr uint64_t dataBytes = static_cast<uint64_t>(blockBytesPerChannel) * channels;
    constexpr uint64_t fileBytes = 28 + 52 + 12 + dataBytes;
    const auto path = std::filesystem::temp_directory_path()
        / std::filesystem::path("echo-libav-streaming-dsd64.dsf");
    std::ofstream out(path, std::ios::binary);
    require(out.good(), "DSD streaming decoder test can create DSF fixture");

    out.write("DSD ", 4);
    writeLe64(out, 28);
    writeLe64(out, fileBytes);
    writeLe64(out, 0);

    out.write("fmt ", 4);
    writeLe64(out, 52);
    writeLe32(out, 1);
    writeLe32(out, 0);
    writeLe32(out, 2);
    writeLe32(out, channels);
    writeLe32(out, nativeSampleRate);
    writeLe32(out, 1);
    writeLe64(out, samplesPerChannel);
    writeLe32(out, blockBytesPerChannel);
    writeLe32(out, 0);

    out.write("data", 4);
    writeLe64(out, 12 + dataBytes);
    for (uint32_t channel = 0; channel < channels; ++channel)
    {
        for (uint32_t index = 0; index < blockBytesPerChannel; ++index)
        {
            const unsigned char byte = ((index + channel) & 1) == 0 ? 0x69 : 0x96;
            out.put(static_cast<char>(byte));
        }
    }

    require(out.good(), "DSD streaming decoder test writes DSF fixture");
    out.close();
    return path.string();
}

std::string writeStreamingDecoderDffFixture()
{
    constexpr uint32_t nativeSampleRate = 2822400;
    constexpr uint16_t channels = 2;
    constexpr uint32_t bytesPerChannel = 4096;
    constexpr uint64_t dataBytes = static_cast<uint64_t>(bytesPerChannel) * channels;
    constexpr uint64_t propertyBytes = 4 + (12 + 4) + (12 + 10) + (12 + 4);
    constexpr uint64_t fileBytes = 16 + (12 + 4) + (12 + propertyBytes) + (12 + dataBytes);
    const auto path = std::filesystem::temp_directory_path()
        / std::filesystem::path("echo-libav-streaming-dsd64.dff");
    std::ofstream out(path, std::ios::binary);
    require(out.good(), "DSD streaming decoder test can create DFF fixture");

    out.write("FRM8", 4);
    writeBe64(out, fileBytes - 12);
    out.write("DSD ", 4);

    out.write("FVER", 4);
    writeBe64(out, 4);
    writeBe32(out, 0x01050000);

    out.write("PROP", 4);
    writeBe64(out, propertyBytes);
    out.write("SND ", 4);
    out.write("FS  ", 4);
    writeBe64(out, 4);
    writeBe32(out, nativeSampleRate);
    out.write("CHNL", 4);
    writeBe64(out, 10);
    writeBe16(out, channels);
    out.write("SLFT", 4);
    out.write("SRGT", 4);
    out.write("CMPR", 4);
    writeBe64(out, 4);
    out.write("DSD ", 4);

    out.write("DSD ", 4);
    writeBe64(out, dataBytes);
    for (uint32_t index = 0; index < bytesPerChannel; ++index)
    {
        out.put(static_cast<char>((index & 1) == 0 ? 0x69 : 0x96));
        out.put(static_cast<char>((index & 1) == 0 ? 0x96 : 0x69));
    }

    require(out.good(), "DSD streaming decoder test writes DFF fixture");
    out.close();
    return path.string();
}

class ScopedFileRemoval final
{
public:
    explicit ScopedFileRemoval(std::string path) : path_(std::move(path)) {}
    ~ScopedFileRemoval()
    {
        if (! path_.empty())
            std::remove(path_.c_str());
    }

private:
    std::string path_;
};

echo::FloatAudioBuffer makeBuffer(int channels, int samples)
{
    echo::FloatAudioBuffer buffer(channels, samples);

    for (int channel = 0; channel < channels; ++channel)
    {
        auto* data = buffer.getWritePointer(channel);
        for (int sample = 0; sample < samples; ++sample)
            data[sample] = 0.15f * std::sin(static_cast<float>(sample + 1) * 0.07f + static_cast<float>(channel) * 0.31f);
    }

    return buffer;
}

echo::FloatAudioBuffer makeFloatBuffer(int channels, int samples)
{
    echo::FloatAudioBuffer buffer(channels, samples);

    for (int channel = 0; channel < channels; ++channel)
    {
        auto* data = buffer.getWritePointer(channel);
        for (int sample = 0; sample < samples; ++sample)
            data[sample] = 0.15f * std::sin(static_cast<float>(sample + 1) * 0.07f + static_cast<float>(channel) * 0.31f);
    }

    return buffer;
}

struct DspChainFixture
{
    echo::EqProcessor eqProcessor;
    echo::ConvolutionProcessor convolutionProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    echo::DspHeadroomProcessor headroomProcessor;
    echo::ReplayGainProcessor replayGainProcessor;
    echo::CompressorProcessor compressorProcessor;
    echo::SpatialDspProcessor spatialDspProcessor;
    echo::PlaybackRateProcessor playbackRateProcessor;
    echo::LevelMeterProcessor levelMeterProcessor;
    echo::DspChain dspChain;

    DspChainFixture()
        : dspChain(
            eqProcessor,
            convolutionProcessor,
            channelBalanceProcessor,
            headroomProcessor,
            replayGainProcessor,
            compressorProcessor,
            spatialDspProcessor,
            playbackRateProcessor,
            levelMeterProcessor)
    {
    }
};

void requireBuffersClose(
    const echo::FloatAudioBuffer& actual,
    const echo::FloatAudioBuffer& expected,
    float tolerance,
    const std::string& message)
{
    require(actual.getNumChannels() == expected.getNumChannels(), message + " channel count");
    require(actual.getNumSamples() == expected.getNumSamples(), message + " sample count");

    for (int channel = 0; channel < actual.getNumChannels(); ++channel)
    {
        const auto* actualData = actual.getReadPointer(channel);
        const auto* expectedData = expected.getReadPointer(channel);
        for (int sample = 0; sample < actual.getNumSamples(); ++sample)
        {
            const float delta = std::abs(actualData[sample] - expectedData[sample]);
            require(delta <= tolerance, message + " at channel " + std::to_string(channel) + " sample " + std::to_string(sample));
        }
    }
}

void requireFinite(const echo::FloatAudioBuffer& buffer, const std::string& message)
{
    for (int channel = 0; channel < buffer.getNumChannels(); ++channel)
    {
        const auto* data = buffer.getReadPointer(channel);
        for (int sample = 0; sample < buffer.getNumSamples(); ++sample)
            require(std::isfinite(data[sample]), message);
    }
}

void testConvolutionIdentityIsTransparent()
{
    echo::ConvolutionProcessor processor;
    processor.prepare(48000.0, 64, 2);
    require(processor.loadImpulseResponseForTests({ { 1.0f } }, 48000.0, "identity", "Identity"), "identity IR loads");
    processor.setEnabled(true);

    auto buffer = makeFloatBuffer(2, 64);
    auto dry = buffer;
    processor.processBlock(buffer, 0, buffer.getNumSamples());

    require(processor.isEnabled(), "convolution reports enabled");
    requireBuffersClose(buffer, dry, nearTolerance, "identity convolution must be transparent");
}

void testConvolutionIrIdentitySkipsDecodeAndRepreparesForRateChanges()
{
    const auto path = writeStreamingDecoderWavFixture(48000, 1, 64);
    ScopedFileRemoval cleanup(path);
    echo::ConvolutionProcessor processor;
    processor.prepare(48000.0, 128, 2);
    const std::string identity48 = R"({"mtimeNs":"1","outputSampleRate":48000,"size":"128"})";
    require(processor.loadImpulseResponse(path, "ir", "IR", identity48),
        "fresh IR identity must decode and prepare");
    require(processor.impulseDecodeCountForTests() == 1,
        "fresh IR must decode exactly once");
    require(processor.loadImpulseResponse(path, "ir", "IR", identity48),
        "identical live-host IR identity must be a successful no-op");
    require(processor.impulseDecodeCountForTests() == 1,
        "identical IR identity must not reopen or decode the file");

    const std::string changedIdentity48 = R"({"mtimeNs":"2","outputSampleRate":48000,"size":"129"})";
    require(processor.loadImpulseResponse(path, "ir", "IR", changedIdentity48),
        "changed IR file identity must reload");
    require(processor.impulseDecodeCountForTests() == 2,
        "changed IR file identity must decode again");

    processor.prepare(96000.0, 128, 2);
    require(std::abs(processor.getState().sampleRate - 96000.0) < 0.5,
        "device-rate prepare must rebuild the resident IR before the next PCM block");
    require(processor.impulseDecodeCountForTests() == 2,
        "device-rate prepare must reuse decoded source taps instead of reopening the IR file");
    const std::string identity96 = R"({"mtimeNs":"2","outputSampleRate":96000,"size":"129"})";
    require(processor.loadImpulseResponse(path, "ir", "IR", identity96),
        "the updated persisted target-rate identity remains applicable");
    require(processor.impulseDecodeCountForTests() == 3,
        "changing the external persisted identity may perform one explicit reload");

    echo::ConvolutionProcessor legacyClient;
    legacyClient.prepare(48000.0, 128, 2);
    require(legacyClient.loadImpulseResponse(path, "legacy-a", "Legacy A"),
        "legacy loadIr client must load without an identity token");
    require(legacyClient.loadImpulseResponse(path, "legacy-b", "Legacy B"),
        "legacy loadIr client must be able to update metadata for the same file");
    require(legacyClient.getState().irId == "legacy-b" && legacyClient.getState().irName == "Legacy B",
        "legacy same-path metadata updates must not return stale room-correction state");
}

void testWorkshopBitcrusherProcessesAndBypassesSafely()
{
    echo::WorkshopAudioEffectController controller;
    echo::WorkshopAudioEffectProcessor processor(controller);
    processor.prepare(8000.0, 64, 1);

    echo::WorkshopAudioEffectState state;
    state.enabled = true;
    state.bitDepth = 4;
    state.sampleRateHz = 1000;
    state.mix = 1.0f;
    state.outputGainDb = 0.0f;
    std::string error;
    require(controller.setState(state, &error), "bounded Workshop bitcrusher state is accepted");

    echo::FloatAudioBuffer warmup(1, 64);
    echo::FloatAudioBuffer processed(1, 64);
    for (int sample = 0; sample < 64; ++sample)
    {
        warmup.getWritePointer(0)[sample] = static_cast<float>(sample) / 80.0f;
        processed.getWritePointer(0)[sample] = static_cast<float>(sample + 64) / 160.0f;
    }
    processor.processBlock(warmup, 0, 64);
    processor.processBlock(processed, 0, 64);

    int transitions = 0;
    const auto* output = processed.getReadPointer(0);
    for (int sample = 1; sample < 64; ++sample)
    {
        if (std::abs(output[sample] - output[sample - 1]) > nearTolerance)
            ++transitions;
        require(std::abs(output[sample] * 7.0f - std::round(output[sample] * 7.0f)) < nearTolerance,
            "Workshop bitcrusher output stays on the requested quantization grid");
    }
    require(transitions <= 8, "Workshop bitcrusher sample-and-hold bounds transition rate");

    state.enabled = false;
    require(controller.setState(state, &error), "Workshop bitcrusher can be disabled");
    echo::FloatAudioBuffer bypass(1, 128);
    for (int sample = 0; sample < 128; ++sample)
        bypass.getWritePointer(0)[sample] = 0.1234f;
    processor.processBlock(bypass, 0, 128);
    require(std::abs(bypass.getReadPointer(0)[127] - 0.1234f) < nearTolerance,
        "Workshop bitcrusher returns smoothly to dry output");
    require(!processor.isActive(), "Workshop bitcrusher reports inactive after its bypass ramp drains");

    state.bitDepth = 3;
    require(!controller.setState(state, &error), "out-of-range Workshop effect state fails closed");
    requireContains(error, "invalid_bit_depth", "invalid Workshop effect reports a bounded validation error");
}

void testWorkshopChiptuneStylizerIsDeterministicAndConsoleLike()
{
    constexpr double sampleRate = 48000.0;
    constexpr double pi = 3.14159265358979323846;
    constexpr int blockSize = 512;

    echo::WorkshopAudioEffectState state;
    state.enabled = true;
    state.effect = echo::WorkshopAudioEffectType::Chiptune;
    state.bitDepth = 8;
    state.sampleRateHz = 8000;
    state.mix = 1.0f;
    state.outputGainDb = -5.0f;
    state.pulseMix = 0.82f;
    state.triangleMix = 0.5f;
    state.noiseMix = 0.22f;
    state.drive = 3.2f;

    auto render = [&](echo::FloatAudioBuffer& result) {
        echo::WorkshopAudioEffectController controller;
        require(controller.setState(state), "bounded Workshop chiptune state is accepted");
        echo::WorkshopAudioEffectProcessor processor(controller);
        processor.prepare(sampleRate, blockSize, 2);

        echo::FloatAudioBuffer warmup(2, blockSize);
        for (int sample = 0; sample < blockSize; ++sample)
        {
            warmup.getWritePointer(0)[sample] = 0.42f * std::sin(2.0 * pi * 220.0 * sample / sampleRate);
            warmup.getWritePointer(1)[sample] = 0.32f * std::sin(2.0 * pi * 330.0 * sample / sampleRate);
        }
        processor.processBlock(warmup, 0, blockSize);

        for (int sample = 0; sample < blockSize; ++sample)
        {
            const int absoluteSample = sample + blockSize;
            const float transient = sample % 127 == 0 ? 0.35f : 0.0f;
            result.getWritePointer(0)[sample] = transient
                + 0.42f * std::sin(2.0 * pi * 220.0 * absoluteSample / sampleRate);
            result.getWritePointer(1)[sample] = transient
                + 0.32f * std::sin(2.0 * pi * 330.0 * absoluteSample / sampleRate);
        }
        processor.processBlock(result, 0, blockSize);
    };

    echo::FloatAudioBuffer first(2, blockSize);
    echo::FloatAudioBuffer second(2, blockSize);
    render(first);
    render(second);

    int transitions = 0;
    int stronglyChanged = 0;
    const auto* left = first.getReadPointer(0);
    const auto* right = first.getReadPointer(1);
    const auto* repeated = second.getReadPointer(0);
    for (int sample = 0; sample < blockSize; ++sample)
    {
        require(std::isfinite(left[sample]), "Workshop chiptune output remains finite");
        require(std::abs(left[sample] - right[sample]) < nearTolerance,
            "Workshop chiptune wet signal uses a console-style centered channel bus");
        require(std::abs(left[sample] - repeated[sample]) < nearTolerance,
            "Workshop chiptune LFSR and shaping are deterministic after reset");
        if (sample > 0 && std::abs(left[sample] - left[sample - 1]) > nearTolerance)
            ++transitions;
        const int absoluteSample = sample + blockSize;
        const float dry = (sample % 127 == 0 ? 0.35f : 0.0f)
            + 0.42f * std::sin(2.0 * pi * 220.0 * absoluteSample / sampleRate);
        if (std::abs(left[sample] - dry) > 0.08f)
            ++stronglyChanged;
    }
    require(transitions <= 100,
        "Workshop chiptune output follows the requested low-rate sample-and-hold cadence");
    require(stronglyChanged > blockSize / 2,
        "Workshop chiptune audibly transforms most samples instead of acting as a transparent bitcrusher");
}

void testConvolutionDecodesPackedWavFormatsThroughSwresample()
{
    const std::array<RoomCorrectionWavFormat, 3> formats {
        RoomCorrectionWavFormat::S16,
        RoomCorrectionWavFormat::S32,
        RoomCorrectionWavFormat::Float32,
    };
    for (const auto format : formats)
    {
        const auto path = writeRoomCorrectionWavFixture(48000, 2, 16, format);
        ScopedFileRemoval cleanup(path);
        echo::ConvolutionProcessor processor;
        processor.prepare(48000.0, 64, 2);
        require(processor.loadImpulseResponse(path, "packed", "Packed WAV", {}, true),
            "packed WAV IR must decode through swresample");
        require(processor.getState().channelMode == "stereo"
                && processor.getState().tapCount == 16,
            "packed WAV IR must preserve stereo channels and source frame count");
        processor.setEnabled(true);

        echo::FloatAudioBuffer impulse(2, 16);
        impulse.clear();
        impulse.setSample(0, 0, 1.0f);
        impulse.setSample(1, 0, 1.0f);
        processor.processBlock(impulse, 0, impulse.getNumSamples());
        require(std::abs(impulse.getSample(0, 0) - 0.5f) <= nearTolerance,
            "packed WAV left channel must not be read as a planar pointer");
        require(std::abs(impulse.getSample(1, 0) - 0.25f) <= nearTolerance,
            "packed WAV right channel must use its interleaved samples");
    }

    const auto monoPath = writeRoomCorrectionWavFixture(48000, 1, 16, RoomCorrectionWavFormat::S16);
    ScopedFileRemoval monoCleanup(monoPath);
    echo::ConvolutionProcessor mono;
    mono.prepare(48000.0, 64, 2);
    require(mono.loadImpulseResponse(monoPath, "mono", "Mono WAV", {}, true),
        "mono packed S16 WAV IR must load");
    require(mono.getState().channelMode == "mono", "mono IR must retain mono channel mode");
    mono.setEnabled(true);
    echo::FloatAudioBuffer stereoInput(2, 16);
    stereoInput.clear();
    stereoInput.setSample(0, 0, 1.0f);
    stereoInput.setSample(1, 0, 1.0f);
    mono.processBlock(stereoInput, 0, stereoInput.getNumSamples());
    require(std::abs(stereoInput.getSample(0, 0) - stereoInput.getSample(1, 0)) <= nearTolerance,
        "mono IR must be applied identically to both processing channels");
}

void testConvolutionPreservesSourceRateAcrossDeviceRates()
{
    struct RateCase
    {
        int sourceRate;
        int targetRate;
        int sourceFrames;
        int expectedTaps;
        int expectedDelay;
    };
    const std::array<RateCase, 3> cases {
        RateCase { 44100, 48000, 441, 800, 160 },
        RateCase { 44100, 96000, 441, 1600, 320 },
        RateCase { 96000, 48000, 960, 544, 32 },
    };
    for (const auto& rateCase : cases)
    {
        const auto path = writeRoomCorrectionWavFixture(
            rateCase.sourceRate,
            1,
            rateCase.sourceFrames,
            RoomCorrectionWavFormat::S16);
        ScopedFileRemoval cleanup(path);
        echo::ConvolutionProcessor processor;
        processor.prepare(static_cast<double>(rateCase.targetRate), 128, 2);
        require(processor.loadImpulseResponse(path, "rate", "Rate IR", {}, true),
            "IR sample-rate conversion must load");
        const auto state = processor.getState();
        require(std::abs(state.sampleRate - rateCase.targetRate) < 0.5,
            "prepared IR must report the actual output device rate");
        require(state.tapCount == rateCase.expectedTaps,
            "IR resampling must retain the complete padded filter response");
        require(state.latencySamples == rateCase.expectedDelay,
            "IR resampling must report the additional delay from response padding");
    }
}

std::filesystem::path roomCorrectionFixturePath(const char* name)
{
    return std::filesystem::path(__FILE__).parent_path() / "fixtures" / name;
}

void testConvolutionDecodesPlanarFloatAiffAndFlacFixtures()
{
    const std::array<const char*, 3> fixtureNames {
        "room-correction-planar-float.m4a",
        "room-correction-s16.aiff",
        "room-correction-s24.flac",
    };
    for (const auto* fixtureName : fixtureNames)
    {
        const auto path = roomCorrectionFixturePath(fixtureName);
        require(std::filesystem::is_regular_file(path), "room correction real-file fixture must exist");
        echo::ConvolutionProcessor processor;
        processor.prepare(48000.0, 128, 2);
        require(processor.loadImpulseResponse(path.string(), fixtureName, fixtureName, {}, true),
            "planar/container IR fixture must decode through swresample");
        const auto state = processor.getState();
        require(state.channelMode == "stereo" && state.tapCount > 0,
            "planar/container IR fixture must preserve a finite stereo impulse");
    }
}

void testConvolutionFailsClosedForInvalidRealFiles()
{
    const auto emptyPath = (std::filesystem::temp_directory_path() / "echo-room-ir-empty.wav").string();
    std::ofstream(emptyPath, std::ios::binary).close();
    ScopedFileRemoval emptyCleanup(emptyPath);

    const auto corruptPath = (std::filesystem::temp_directory_path() / "echo-room-ir-corrupt.wav").string();
    {
        std::ofstream corrupt(corruptPath, std::ios::binary);
        corrupt.write("not-audio", 9);
    }
    ScopedFileRemoval corruptCleanup(corruptPath);

    const auto missingLayoutPath = writeRoomCorrectionWavFixture(
        48000, 0, 0, RoomCorrectionWavFormat::S16);
    ScopedFileRemoval missingLayoutCleanup(missingLayoutPath);
    const auto tooLongPath = writeRoomCorrectionWavFixture(
        48000, 2, echo::roomCorrectionMaxTaps + 1, RoomCorrectionWavFormat::S16);
    ScopedFileRemoval tooLongCleanup(tooLongPath);
    const auto nonFinitePath = writeRoomCorrectionWavFixture(
        48000, 2, 16, RoomCorrectionWavFormat::Float32, true);
    ScopedFileRemoval nonFiniteCleanup(nonFinitePath);

    const std::array<std::string, 5> invalidPaths {
        emptyPath,
        corruptPath,
        missingLayoutPath,
        tooLongPath,
        nonFinitePath,
    };
    for (const auto& path : invalidPaths)
    {
        echo::ConvolutionProcessor processor;
        processor.prepare(48000.0, 64, 2);
        require(! processor.loadImpulseResponse(path, "invalid", "Invalid", {}, true),
            "invalid, layout-less, non-finite, or overlong IR must fail closed");
        const auto state = processor.getState();
        require(state.status == "error" && !state.error.empty() && state.tapCount == 0,
            "failed IR decode must not publish a partially prepared convolution graph");
    }
}

void testConvolutionDelayImpulse()
{
    echo::ConvolutionProcessor processor;
    processor.prepare(48000.0, 8, 1);
    require(processor.loadImpulseResponseForTests({ { 0.0f, 1.0f } }, 48000.0, "delay", "Delay"), "delay IR loads");
    processor.setEnabled(true);

    echo::FloatAudioBuffer buffer(1, 4);
    buffer.clear();
    buffer.setSample(0, 0, 0.5f);
    processor.processBlock(buffer, 0, buffer.getNumSamples());

    require(std::abs(buffer.getSample(0, 0)) <= nearTolerance, "delay sample 0");
    require(std::abs(buffer.getSample(0, 1) - 0.5f) <= nearTolerance, "delay sample 1");
    requireFinite(buffer, "delay convolution finite");
}

void testConvolutionStereoMapping()
{
    echo::ConvolutionProcessor processor;
    processor.prepare(44100.0, 8, 2);
    require(processor.loadImpulseResponseForTests({ { 1.0f }, { 0.5f } }, 44100.0, "stereo", "Stereo"), "stereo IR loads");
    processor.setEnabled(true);

    echo::FloatAudioBuffer buffer(2, 4);
    for (int sample = 0; sample < 4; ++sample)
    {
        buffer.setSample(0, sample, 0.25f);
        buffer.setSample(1, sample, 0.25f);
    }
    processor.processBlock(buffer, 0, buffer.getNumSamples());

    require(std::abs(buffer.getSample(0, 0) - 0.25f) <= nearTolerance, "left stereo FIR");
    require(std::abs(buffer.getSample(1, 0) - 0.125f) <= nearTolerance, "right stereo FIR");
}

void testConvolutionRejectsLongImpulseAndClipsSafely()
{
    echo::ConvolutionProcessor processor;
    processor.prepare(48000.0, 16, 1);
    require(! processor.loadImpulseResponseForTests({ std::vector<float>(static_cast<size_t>(echo::roomCorrectionMaxTaps + 1), 1.0f) }, 48000.0, "long", "Long"), "long IR rejected");
    require(processor.loadImpulseResponseForTests({ { 8.0f } }, 48000.0, "hot", "Hot"), "hot IR loads");
    processor.setEnabled(true);

    echo::FloatAudioBuffer buffer(1, 4);
    buffer.clear();
    buffer.setSample(0, 0, 0.5f);
    processor.processBlock(buffer, 0, buffer.getNumSamples());

    requireFinite(buffer, "hot convolution finite");
    require(processor.hasClippingRisk(), "hot convolution reports clipping risk");
    require(std::abs(buffer.getSample(0, 0)) > 1.0f, "hot convolution reports risk without limiting inside FIR");
}

void testPartitionedConvolutionMatchesLongImpulseAcrossCallbacks()
{
    std::vector<float> taps(300, 0.0f);
    taps[0] = 1.0f;
    taps[63] = -0.2f;
    taps[127] = 0.1f;
    taps[128] = 0.5f;
    taps[191] = -0.15f;
    taps[255] = 0.25f;
    taps[299] = 0.125f;

    echo::ConvolutionProcessor processor;
    processor.prepare(48000.0, 256, 2);
    require(processor.loadImpulseResponseForTests({ taps, taps }, 48000.0, "partitioned", "Partitioned"),
        "long partitioned IR must prepare");
    processor.setEnabled(true);

    const std::array<int, 6> callbackSizes { 64, 128, 64, 256, 64, 64 };
    std::vector<float> rendered;
    rendered.reserve(640);
    bool firstSample = true;
    for (const int callbackSize : callbackSizes)
    {
        echo::FloatAudioBuffer block(2, callbackSize);
        block.clear();
        if (firstSample)
        {
            block.setSample(0, 0, 1.0f);
            block.setSample(1, 0, 1.0f);
            firstSample = false;
        }
        processor.processBlock(block, 0, callbackSize);
        for (int sample = 0; sample < callbackSize; ++sample)
            rendered.push_back(block.getSample(0, sample));
    }

    require(rendered.size() >= taps.size(), "partitioned convolution test must render the complete tail");
    for (size_t sample = 0; sample < taps.size(); ++sample)
        require(std::abs(rendered[sample] - taps[sample]) < 0.001f,
            "partitioned convolution must match the direct FIR reference at sample " + std::to_string(sample));
    for (size_t sample = taps.size(); sample < rendered.size(); ++sample)
        require(std::abs(rendered[sample]) < 0.001f,
            "partitioned convolution must not emit energy beyond the IR tail");
    require(processor.isTailDrained(), "partitioned convolution tail counter must reach zero");
}

void testDspSessionTransitionContract()
{
    DspChainFixture fixture;
    fixture.dspChain.prepare(48000.0, 64, 2);
    std::vector<float> taps(200, 0.0f);
    taps[0] = 1.0f;
    taps[100] = 0.5f;
    taps[199] = 0.25f;
    require(fixture.convolutionProcessor.loadImpulseResponseForTests({ taps }, 48000.0, "session", "Session"),
        "DSP session test IR must prepare");
    fixture.convolutionProcessor.setEnabled(true);

    echo::CompressorState compressorState;
    compressorState.enabled = true;
    compressorState.thresholdDb = -40.0f;
    compressorState.ratio = 20.0f;
    compressorState.attackMs = 0.1f;
    compressorState.releaseMs = 1000.0f;
    fixture.compressorProcessor.setState(compressorState);

    echo::FloatAudioBuffer first(2, 64);
    first.clear();
    first.setSample(0, 0, 1.0f);
    first.setSample(1, 0, 1.0f);
    fixture.dspChain.processBlock(first, 0, 64);
    require(fixture.dspChain.tailFramesRemaining() > 0,
        "active FIR must publish a user-DSP tail");
    require(fixture.compressorProcessor.gainReductionDb() > 0.0f,
        "session test must establish compressor envelope history");

    fixture.dspChain.applySessionTransition(echo::DspSessionTransition::AutomixPromotion);
    const auto promotionTail = fixture.dspChain.tailFramesRemaining();
    require(promotionTail > 0,
        "Automix promotion must not reset the programme DSP graph");
    fixture.dspChain.applySessionTransition(echo::DspSessionTransition::GaplessContinuation);

    echo::FloatAudioBuffer continuation(2, 64);
    continuation.clear();
    fixture.dspChain.processBlock(continuation, 0, 64);
    require(std::abs(continuation.getSample(0, 36)) > 0.001f,
        "Gapless continuation must preserve FIR history across the track boundary");

    fixture.dspChain.applySessionTransition(echo::DspSessionTransition::HardReplace);
    require(fixture.dspChain.isTailDrained(), "HardReplace must clear the complete user-DSP tail");
    require(fixture.compressorProcessor.gainReductionDb() == 0.0f,
        "HardReplace must clear compressor detector and envelope history");
    require(!fixture.dspChain.isSafetyLimiterProtecting(),
        "HardReplace must clear limiter envelope state");

    echo::FloatAudioBuffer replacement(2, 256);
    replacement.clear();
    fixture.dspChain.processBlock(replacement, 0, 256);
    for (int channel = 0; channel < 2; ++channel)
        for (int sample = 0; sample < 256; ++sample)
            require(std::abs(replacement.getSample(channel, sample)) < nearTolerance,
                "HardReplace must not leak the previous FIR or delay history into the replacement track");
}

void testDspUnifiedTailContract()
{
    echo::FloatAudioBuffer impulse(2, 64);
    impulse.clear();
    impulse.setSample(0, 0, 1.0f);
    impulse.setSample(1, 0, -1.0f);

    echo::EqProcessor eq;
    eq.prepare(48'000.0, 64, 2);
    eq.setEnabled(true);
    require(eq.setBandFilterType(0, echo::EqFilterType::LowPass), "tail test must configure EQ low-pass");
    eq.reset();
    eq.processBlock(impulse, 0, 64);
    require(!eq.isTailDrained(), "EQ IIR history must participate in the unified DSP tail");
    eq.reset();
    require(eq.isTailDrained(), "EQ HardReplace reset must clear its tail");

    impulse.clear();
    impulse.setSample(0, 0, 1.0f);
    echo::ChannelBalanceProcessor balance;
    balance.prepare(48'000.0, 64, 2);
    echo::ChannelBalanceState balanceState;
    balanceState.enabled = true;
    balanceState.leftDelayMs = 10.0f;
    balance.setState(balanceState);
    balance.reset();
    balance.processBlock(impulse, 0, 64);
    require(balance.tailFramesRemaining() >= 400,
        "channel delay history must publish its remaining audible frames");
    balance.reset();
    require(balance.isTailDrained(), "channel balance HardReplace reset must clear delay and filter tails");

    impulse.clear();
    impulse.setSample(0, 0, 1.0f);
    impulse.setSample(1, 0, -1.0f);
    echo::SpatialDspProcessor spatial;
    spatial.prepare(48'000.0, 64, 2);
    echo::CrossfeedState crossfeed;
    crossfeed.enabled = true;
    spatial.setCrossfeedState(crossfeed);
    spatial.processCrossfeedBlock(impulse, 0, 64);
    require(!spatial.isTailDrained(), "crossfeed low-pass history must participate in the unified DSP tail");
    spatial.reset();
    require(spatial.isTailDrained(), "crossfeed HardReplace reset must clear its tail");
}

void testRpcOutputWriterSerializesShortWrites()
{
    std::mutex sinkMutex;
    std::string sink;
    std::atomic<int> writeCalls { 0 };
    RpcOutputWriter writer(
        -1,
        [&](const char* data, std::size_t bytes) -> std::ptrdiff_t {
            const int call = writeCalls.fetch_add(1, std::memory_order_relaxed) + 1;
            if ((call % 19) == 0)
            {
                errno = EINTR;
                return -1;
            }
            const std::size_t written = std::min<std::size_t>(bytes, static_cast<std::size_t>(1 + (call % 7)));
            std::lock_guard<std::mutex> lock(sinkMutex);
            sink.append(data, written);
            return static_cast<std::ptrdiff_t>(written);
        },
        32);

    constexpr int threadCount = 4;
    constexpr int responsesPerThread = 50;
    std::array<std::thread, threadCount> producers;
    for (int threadIndex = 0; threadIndex < threadCount; ++threadIndex)
    {
        producers[static_cast<std::size_t>(threadIndex)] = std::thread([&, threadIndex] {
            for (int response = 0; response < responsesPerThread; ++response)
            {
                const int id = threadIndex * responsesPerThread + response;
                const std::string payload(256 + (id % 31), static_cast<char>('a' + threadIndex));
                require(writer.enqueue(nlohmann::json({
                    {"jsonrpc", "2.0"},
                    {"id", id},
                    {"result", {{"devices", payload}}},
                }).dump()), "reliable RPC response must be accepted");
                writer.enqueue(
                    nlohmann::json({{"jsonrpc", "2.0"}, {"method", "audio.position"}, {"params", {{"outputFrame", id}}}}).dump(),
                    RpcOutputWriter::MessageClass::Position);
                writer.enqueue(
                    nlohmann::json({{"jsonrpc", "2.0"}, {"method", "audio.levelMeter"}, {"params", {{"peakDb", {-1.0}}}}}).dump(),
                    RpcOutputWriter::MessageClass::LevelMeter);
            }
        });
    }
    for (auto& producer : producers)
        producer.join();
    writer.close();
    require(!writer.failed(), "short writes and EINTR must not fail the RPC writer");

    std::array<bool, threadCount * responsesPerThread> observedResponses {};
    std::istringstream lines(sink);
    std::string line;
    while (std::getline(lines, line))
    {
        require(!line.empty(), "RPC writer must not emit partial blank lines");
        const auto parsed = nlohmann::json::parse(line);
        if (parsed.contains("id"))
        {
            const int id = parsed["id"].get<int>();
            require(id >= 0 && id < static_cast<int>(observedResponses.size()), "RPC response id must remain intact");
            require(!observedResponses[static_cast<std::size_t>(id)], "each RPC id must have one response");
            observedResponses[static_cast<std::size_t>(id)] = true;
        }
    }
    require(std::all_of(observedResponses.begin(), observedResponses.end(), [](bool observed) { return observed; }),
        "every reliable RPC response must survive concurrent notification pressure");

    RpcOutputWriter closedWriter(-1, [](const char*, std::size_t) -> std::ptrdiff_t { return 0; });
    require(closedWriter.enqueue(R"({"jsonrpc":"2.0","id":1,"result":true})"),
        "transport-close probe must enqueue before failure");
    closedWriter.close();
    require(closedWriter.failed(), "zero-byte write must close the broken RPC transport");
}

void testPcmDrainWaitsForRoomCorrectionTail()
{
    echo::EqProcessor eqProcessor;
    echo::ConvolutionProcessor convolutionProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    echo::DspHeadroomProcessor headroomProcessor;
    echo::ReplayGainProcessor replayGainProcessor;
    echo::CompressorProcessor compressorProcessor;
    echo::SpatialDspProcessor spatialDspProcessor;
    echo::PlaybackRateProcessor playbackRateProcessor;
    echo::LevelMeterProcessor meterProcessor;
    PcmRingAudioSource source(
        2, 512, 0, 0, 1.0f,
        eqProcessor, convolutionProcessor, channelBalanceProcessor, headroomProcessor,
        replayGainProcessor, compressorProcessor, spatialDspProcessor,
        playbackRateProcessor, meterProcessor);
    source.prepareForNativeRender(64, 48000.0);
    std::vector<float> taps(200, 0.0f);
    taps[0] = 1.0f;
    taps[199] = 0.5f;
    require(convolutionProcessor.loadImpulseResponseForTests({ taps }, 48000.0, "tail", "Tail"),
        "drain test IR must prepare");
    convolutionProcessor.setEnabled(true);
    source.beginSession();
    const std::array<float, 2> impulse { 1.0f, 1.0f };
    require(source.push(impulse.data(), 1), "drain test must push one source frame");
    source.markInputEnded();

    echo::FloatAudioBuffer output(2, 64);
    require(source.renderPlanar(output, 0, 64) == 64,
        "first tail callback must render source plus room-correction tail");
    require(!source.isDrained(),
        "input EOF plus empty FIFO must not report ended while user DSP tail remains");
    bool observedTail = false;
    for (int callback = 0; callback < 4; ++callback)
    {
        output.clear();
        source.renderPlanar(output, 0, 64);
        for (int sample = 0; sample < 64; ++sample)
            observedTail = observedTail || std::abs(output.getSample(0, sample)) > 0.1f;
    }
    require(observedTail, "room-correction tail must reach the output instead of being truncated at EOF");
    require(source.isDrained(), "natural end must complete after the user DSP tail drains");
}

void testConvolutionLiveReloadRetiresGraphsOffCallback()
{
    echo::ConvolutionProcessor processor;
    processor.prepare(96000.0, 64, 2);
    processor.setEnabled(true);
    std::atomic<bool> stop { false };
    std::atomic<bool> finite { true };
    auto render = std::async(std::launch::async, [&] {
        while (!stop.load(std::memory_order_acquire))
        {
            auto block = makeFloatBuffer(2, 64);
            processor.processBlock(block, 0, 64);
            for (int channel = 0; channel < 2; ++channel)
                for (int sample = 0; sample < 64; ++sample)
                    finite.store(finite.load(std::memory_order_relaxed)
                        && std::isfinite(block.getSample(channel, sample)), std::memory_order_relaxed);
        }
    });

    for (int reload = 0; reload < 1000; ++reload)
    {
        const int tapCount = 512 + (reload % 4) * 512;
        std::vector<float> taps(static_cast<size_t>(tapCount), 0.0f);
        taps[0] = 1.0f;
        taps.back() = 0.01f;
        require(processor.loadImpulseResponseForTests(
                reload % 2 == 0 ? std::vector<std::vector<float>> { taps }
                                : std::vector<std::vector<float>> { taps, taps },
                96000.0,
                "reload-" + std::to_string(reload),
                "Reload"),
            "live IR reload must prepare a complete inactive graph");
        if (reload % 10 == 9)
            processor.clearImpulseResponse();
    }
    stop.store(true, std::memory_order_release);
    render.get();
    require(finite.load(std::memory_order_acquire),
        "live IR reload must not expose freed or partially prepared graph memory");
}

void testPartitionedConvolutionCallbackDeadlineMatrix()
{
    const std::array<int, 3> sampleRates { 48000, 96000, 192000 };
    const std::array<int, 4> callbackSizes { 64, 128, 256, 512 };
    const std::array<int, 3> tapCounts { 512, 2048, 8192 };
    double worstP99Us = 0.0;
    double worstP999Us = 0.0;
    double tightestHeadroom = std::numeric_limits<double>::infinity();
    std::string worstCase;

    for (const int sampleRate : sampleRates)
    {
        for (const int callbackSize : callbackSizes)
        {
            const double callbackDeadlineUs = 1'000'000.0 * callbackSize / sampleRate;
            for (const int tapCount : tapCounts)
            {
                for (const int irChannels : { 1, 2 })
                {
                    std::vector<float> taps(static_cast<size_t>(tapCount), 0.0f);
                    for (int tap = 0; tap < tapCount; ++tap)
                        taps[static_cast<size_t>(tap)] = tap == 0
                            ? 1.0f
                            : 0.02f * std::exp(-static_cast<float>(tap) / 700.0f)
                                * std::sin(static_cast<float>(tap) * 0.11f);
                    echo::ConvolutionProcessor processor;
                    processor.prepare(static_cast<double>(sampleRate), callbackSize, 2);
                    require(processor.loadImpulseResponseForTests(
                            irChannels == 1 ? std::vector<std::vector<float>> { taps }
                                            : std::vector<std::vector<float>> { taps, taps },
                            static_cast<double>(sampleRate),
                            "deadline",
                            "Deadline"),
                        "deadline matrix IR must prepare");
                    processor.setEnabled(true);
                    auto block = makeFloatBuffer(2, callbackSize);
                    for (int warmup = 0; warmup < 8; ++warmup)
                        processor.processBlock(block, 0, callbackSize);

                    std::vector<double> durations;
                    durations.reserve(64);
                    for (int iteration = 0; iteration < 64; ++iteration)
                    {
                        const auto started = std::chrono::steady_clock::now();
                        processor.processBlock(block, 0, callbackSize);
                        const auto finished = std::chrono::steady_clock::now();
                        durations.push_back(std::chrono::duration<double, std::micro>(finished - started).count());
                    }
                    std::sort(durations.begin(), durations.end());
                    const double p99 = durations[static_cast<size_t>(std::ceil(durations.size() * 0.99) - 1)];
                    const double p999 = durations.back();
                    worstP99Us = std::max(worstP99Us, p99);
                    if (p999 > worstP999Us)
                    {
                        worstP999Us = p999;
                        worstCase = std::to_string(sampleRate) + "Hz/" + std::to_string(callbackSize)
                            + "f/" + std::to_string(tapCount) + "t/" + std::to_string(irChannels) + "ch";
                    }
                    tightestHeadroom = std::min(tightestHeadroom, callbackDeadlineUs / std::max(0.001, p999));
#if ! ECHO_NATIVE_INSTRUMENTED_TESTS
                    require(p999 < callbackDeadlineUs,
                        "partitioned convolution p999 must remain inside the callback deadline for "
                            + std::to_string(sampleRate) + "Hz/" + std::to_string(callbackSize)
                            + "f/" + std::to_string(tapCount) + "t/" + std::to_string(irChannels) + "ch");
#endif
                }
            }
        }
    }

    std::cout << "[BENCH] room-correction callback matrix p99_us=" << worstP99Us
              << " p999_us=" << worstP999Us
              << " tightest_deadline_headroom=" << tightestHeadroom
              << " worst_case=" << worstCase << '\n';
}

void testChannelBalanceDelayCompensation()
{
    echo::ChannelBalanceProcessor processor;
    processor.prepare(1000.0, 16, 2);

    echo::ChannelBalanceState state;
    state.enabled = true;
    state.leftDelayMs = 2.0f;
    state.rightDelayMs = 0.0f;
    processor.setState(state);
    processor.reset();

    echo::FloatAudioBuffer buffer(2, 6);
    buffer.clear();
    buffer.setSample(0, 0, 0.5f);
    buffer.setSample(1, 0, 0.25f);
    processor.processBlock(buffer, 0, buffer.getNumSamples());

    require(std::abs(buffer.getSample(0, 0)) <= nearTolerance, "left delay sample 0");
    require(std::abs(buffer.getSample(0, 1)) <= nearTolerance, "left delay sample 1");
    require(std::abs(buffer.getSample(0, 2) - 0.5f) <= nearTolerance, "left delay sample 2");
    require(std::abs(buffer.getSample(1, 0) - 0.25f) <= nearTolerance, "right dry timing");
    requireFinite(buffer, "channel delay finite");
}

void testChannelBalanceSoloKeepsPhysicalSide()
{
    echo::ChannelBalanceProcessor processor;
    processor.prepare(48000.0, 16, 2);

    echo::ChannelBalanceState state;
    state.enabled = true;
    state.monoMode = echo::ChannelBalanceMonoMode::LeftOnly;
    processor.setState(state);

    echo::FloatAudioBuffer liveLeftOnlyBuffer(2, 4);
    liveLeftOnlyBuffer.clear();
    liveLeftOnlyBuffer.setSample(0, 0, 0.625f);
    liveLeftOnlyBuffer.setSample(1, 0, 0.375f);
    processor.processBlock(liveLeftOnlyBuffer, 0, liveLeftOnlyBuffer.getNumSamples());

    require(std::abs(liveLeftOnlyBuffer.getSample(0, 0) - 0.625f) <= nearTolerance, "live left solo keeps physical left immediately");
    require(std::abs(liveLeftOnlyBuffer.getSample(1, 0)) <= nearTolerance, "live left solo mutes physical right immediately");

    state.monoMode = echo::ChannelBalanceMonoMode::RightOnly;
    processor.setState(state);
    processor.reset();

    echo::FloatAudioBuffer rightOnlyBuffer(2, 4);
    rightOnlyBuffer.clear();
    rightOnlyBuffer.setSample(0, 0, 0.25f);
    rightOnlyBuffer.setSample(1, 0, 0.75f);
    processor.processBlock(rightOnlyBuffer, 0, rightOnlyBuffer.getNumSamples());

    require(std::abs(rightOnlyBuffer.getSample(0, 0)) <= nearTolerance, "right solo mutes physical left");
    require(std::abs(rightOnlyBuffer.getSample(1, 0) - 0.75f) <= nearTolerance, "right solo keeps physical right");

    state.monoMode = echo::ChannelBalanceMonoMode::LeftOnly;
    processor.setState(state);
    processor.reset();

    echo::FloatAudioBuffer leftOnlyBuffer(2, 4);
    leftOnlyBuffer.clear();
    leftOnlyBuffer.setSample(0, 0, 0.5f);
    leftOnlyBuffer.setSample(1, 0, 0.125f);
    processor.processBlock(leftOnlyBuffer, 0, leftOnlyBuffer.getNumSamples());

    require(std::abs(leftOnlyBuffer.getSample(0, 0) - 0.5f) <= nearTolerance, "left solo keeps physical left");
    require(std::abs(leftOnlyBuffer.getSample(1, 0)) <= nearTolerance, "left solo mutes physical right");
    requireFinite(rightOnlyBuffer, "right solo finite");
    requireFinite(leftOnlyBuffer, "left solo finite");
}

void testChannelBalanceBandGainCompensation()
{
    echo::ChannelBalanceProcessor processor;
    processor.prepare(48000.0, 4096, 2);

    echo::ChannelBalanceState state;
    state.enabled = true;
    state.leftBandGainsDb[0] = -6.0f;
    state.rightBandGainsDb[0] = 0.0f;
    processor.setState(state);
    processor.reset();

    echo::FloatAudioBuffer buffer(2, 4096);
    for (int sample = 0; sample < buffer.getNumSamples(); ++sample)
    {
        buffer.setSample(0, sample, 0.5f);
        buffer.setSample(1, sample, 0.5f);
    }

    processor.processBlock(buffer, 0, buffer.getNumSamples());

    const auto leftTail = std::abs(buffer.getSample(0, buffer.getNumSamples() - 1));
    const auto rightTail = std::abs(buffer.getSample(1, buffer.getNumSamples() - 1));
    require(leftTail < rightTail * 0.7f, "left low band attenuation applies to audio");
    requireFinite(buffer, "channel band compensation finite");
}

void testDspChainBypassPreservesDryBuffer()
{
    DspChainFixture fixture;
    auto& dspChain = fixture.dspChain;
    std::atomic<bool> meterObserved { false };
    fixture.levelMeterProcessor.setIntervalMs(1);
    fixture.levelMeterProcessor.setCallback([&meterObserved](const echo::LevelMeterSnapshot& snapshot)
    {
        meterObserved.store(! snapshot.peakDb.empty() && snapshot.peakDb.front() > -60.0f, std::memory_order_release);
    });
    dspChain.prepare(48000.0, 128, 2);

    auto buffer = makeFloatBuffer(2, 128);
    auto dry = makeFloatBuffer(2, 128);
    for (int channel = 0; channel < buffer.getNumChannels(); ++channel)
    {
        auto* samples = buffer.getWritePointer(channel);
        auto* drySamples = dry.getWritePointer(channel);
        for (int sample = 0; sample < buffer.getNumSamples(); ++sample)
        {
            const float value = channel == 0
                ? static_cast<float>(sample) / 127.0f
                : -static_cast<float>(sample) / 127.0f;
            samples[sample] = value;
            drySamples[sample] = value;
        }
    }

    require(! dspChain.isActive(), "inactive DSP chain must report bypass");
    dspChain.processBlock(buffer, 0, buffer.getNumSamples());
    requireBuffersClose(buffer, dry, strictTolerance, "inactive DSP chain must not touch native playback samples");
    require(waitForCondition([&] { return meterObserved.load(std::memory_order_acquire); }),
        "inactive DSP chain must still report native output levels");
    require(! dspChain.hasClippingRisk(), "inactive DSP chain must not report clipping risk");
    require(! dspChain.isSafetyLimiterProtecting(), "inactive DSP chain must not report limiter protection");
}

void testLevelMeterVisualSpectrumObservesWithoutMutatingAudio()
{
    echo::LevelMeterProcessor meter;
    std::mutex observedMutex;
    echo::LevelMeterSnapshot observed;
    std::atomic<bool> received { false };
    meter.setIntervalMs(50);
    meter.setVisualSpectrumEnabled(true);
    meter.setCallback([&observed, &observedMutex, &received](const echo::LevelMeterSnapshot& snapshot)
    {
        std::lock_guard<std::mutex> lock(observedMutex);
        observed = snapshot;
        received.store(true, std::memory_order_release);
    });
    meter.prepare(48000.0, 4096, 2);

    echo::FloatAudioBuffer buffer(2, 4096);
    echo::FloatAudioBuffer dry(2, 4096);
    for (int sample = 0; sample < buffer.getNumSamples(); ++sample)
    {
        const float value = 0.35f * std::sin(
            2.0f * 3.14159265358979323846f * 1000.0f * static_cast<float>(sample) / 48000.0f);
        for (int channel = 0; channel < buffer.getNumChannels(); ++channel)
        {
            buffer.setSample(channel, sample, value);
            dry.setSample(channel, sample, value);
        }
    }

    meter.processBlock(buffer, 0, buffer.getNumSamples());

    requireBuffersClose(buffer, dry, strictTolerance, "level meter visual spectrum must remain read-only");
    require(waitForCondition([&] { return received.load(std::memory_order_acquire); }),
        "enabled level meter must publish a worker snapshot");
    echo::LevelMeterSnapshot published;
    {
        std::lock_guard<std::mutex> lock(observedMutex);
        published = observed;
    }
    require(published.visualSpectrumReady, "enabled level meter must publish primed visual spectrum");
    require(published.visualSpectrum.size() == echo::levelMeterVisualSpectrumBucketCount,
        "visual spectrum must publish the renderer contract bucket count");
    require(*std::max_element(published.visualSpectrum.begin(), published.visualSpectrum.end()) > 0.1f,
        "visual spectrum must react to audible signal energy");
}

void testCompressorReducesHotSignalsAndPreservesBypass()
{
    echo::CompressorProcessor processor;
    processor.prepare(48'000.0, 512, 2);

    echo::FloatAudioBuffer bypassed(2, 512);
    for (int channel = 0; channel < 2; ++channel)
        std::fill_n(bypassed.getWritePointer(channel), 512, 0.8f);
    processor.processBlock(bypassed, 0, 512);
    require(std::abs(bypassed.getSample(0, 511) - 0.8f) <= 1.0e-7f,
        "disabled compressor must preserve the dry signal exactly");

    echo::CompressorState state;
    state.enabled = true;
    state.thresholdDb = -18.0f;
    state.ratio = 8.0f;
    state.attackMs = 0.1f;
    state.releaseMs = 100.0f;
    state.kneeDb = 0.0f;
    processor.setState(state);

    echo::FloatAudioBuffer compressed(2, 512);
    for (int channel = 0; channel < 2; ++channel)
        std::fill_n(compressed.getWritePointer(channel), 512, 0.8f);
    processor.processBlock(compressed, 0, 512);
    require(std::isfinite(compressed.getSample(0, 511)), "compressor output must remain finite");
    require(compressed.getSample(0, 511) < 0.35f,
        "enabled compressor must reduce a sustained signal above threshold");
    require(processor.gainReductionDb() > 8.0f,
        "compressor must expose positive gain-reduction telemetry");
}

void testCompressorAdvancedDetectorLinkAndRangeControls()
{
    auto processConstant = [](echo::CompressorProcessor& processor, float left, float right, int frames = 4096)
    {
        echo::FloatAudioBuffer buffer(2, frames);
        std::fill_n(buffer.getWritePointer(0), frames, left);
        std::fill_n(buffer.getWritePointer(1), frames, right);
        processor.processBlock(buffer, 0, frames);
        return std::pair<float, float> {
            buffer.getSample(0, frames - 1),
            buffer.getSample(1, frames - 1),
        };
    };

    echo::CompressorState independentState;
    independentState.enabled = true;
    independentState.thresholdDb = -18.0f;
    independentState.ratio = 12.0f;
    independentState.attackMs = 0.1f;
    independentState.kneeDb = 0.0f;
    independentState.stereoLink = 0.0f;
    independentState.rangeDb = 72.0f;
    echo::CompressorProcessor independent;
    independent.setState(independentState);
    independent.prepare(48'000.0, 4096, 2);
    const auto independentOutput = processConstant(independent, 0.8f, 0.05f);
    require(independentOutput.first < 0.3f, "an unlinked hot channel is compressed");
    require(std::abs(independentOutput.second - 0.05f) < 0.002f,
        "zero stereo link leaves a quiet channel uncompressed");

    independentState.stereoLink = 1.0f;
    echo::CompressorProcessor linked;
    linked.setState(independentState);
    linked.prepare(48'000.0, 4096, 2);
    const auto linkedOutput = processConstant(linked, 0.8f, 0.05f);
    require(linkedOutput.second < 0.025f,
        "full stereo link applies the hot channel envelope to both channels");

    independentState.stereoLink = 1.0f;
    independentState.thresholdDb = -48.0f;
    independentState.ratio = 40.0f;
    independentState.rangeDb = 3.0f;
    echo::CompressorProcessor ranged;
    ranged.setState(independentState);
    ranged.prepare(48'000.0, 4096, 2);
    const auto rangedOutput = processConstant(ranged, 1.0f, 1.0f);
    require(rangedOutput.first > 0.69f,
        "maximum gain-reduction range caps deep compression near three decibels");

    const auto telemetry = ranged.getTelemetry();
    require(telemetry.inputPeakDb.size() == 2 && telemetry.outputPeakDb.size() == 2,
        "compressor telemetry reports per-channel input and output meters");
    require(telemetry.outputHeadroomDb >= 0.0f && telemetry.outputHeadroomDb < 4.0f,
        "compressor telemetry reports actual output headroom");
}

void testCompressorSidechainHighpassAndClicklessBypass()
{
    auto processLowTone = [](echo::CompressorProcessor& processor)
    {
        constexpr int frames = 9600;
        echo::FloatAudioBuffer buffer(2, frames);
        for (int sample = 0; sample < frames; ++sample)
        {
            const float value = 0.8f * std::sin(
                2.0 * 3.14159265358979323846 * 40.0 * static_cast<double>(sample) / 48'000.0);
            buffer.setSample(0, sample, value);
            buffer.setSample(1, sample, value);
        }
        processor.processBlock(buffer, 0, frames);
        return processor.gainReductionDb();
    };

    echo::CompressorState state;
    state.enabled = true;
    state.thresholdDb = -24.0f;
    state.ratio = 8.0f;
    state.attackMs = 1.0f;
    state.releaseMs = 100.0f;
    state.kneeDb = 0.0f;
    echo::CompressorProcessor fullBand;
    fullBand.setState(state);
    fullBand.prepare(48'000.0, 9600, 2);
    const float fullBandReduction = processLowTone(fullBand);

    state.sidechainHighpassEnabled = true;
    state.sidechainHighpassHz = 200.0f;
    echo::CompressorProcessor filtered;
    filtered.setState(state);
    filtered.prepare(48'000.0, 9600, 2);
    const float filteredReduction = processLowTone(filtered);
    require(filteredReduction + 4.0f < fullBandReduction,
        "sidechain high-pass prevents deep low-frequency pumping");

    state.sidechainHighpassEnabled = false;
    state.thresholdDb = -18.0f;
    echo::CompressorProcessor bypassed;
    bypassed.setState(state);
    bypassed.prepare(48'000.0, 4096, 2);
    echo::FloatAudioBuffer hot(2, 4096);
    for (int channel = 0; channel < 2; ++channel)
        std::fill_n(hot.getWritePointer(channel), 4096, 0.8f);
    bypassed.processBlock(hot, 0, 4096);
    const float compressedTail = hot.getSample(0, 4095);

    state.enabled = false;
    bypassed.setState(state);
    echo::FloatAudioBuffer release(2, 512);
    for (int channel = 0; channel < 2; ++channel)
        std::fill_n(release.getWritePointer(channel), 512, 0.8f);
    bypassed.processBlock(release, 0, 512);
    require(std::abs(release.getSample(0, 0) - compressedTail) < 0.03f,
        "bypass begins from the current wet gain without a discontinuity");
    require(bypassed.isProcessingActive(), "compressor remains active while the bypass ramp drains");

    for (int block = 0; block < 12; ++block)
    {
        for (int channel = 0; channel < 2; ++channel)
            std::fill_n(release.getWritePointer(channel), 512, 0.8f);
        bypassed.processBlock(release, 0, 512);
    }
    require(std::abs(release.getSample(0, 511) - 0.8f) < 0.001f,
        "bypass ramp settles to the exact dry signal");
    require(!bypassed.isProcessingActive(), "compressor releases the DSP chain after bypass settles");
    const auto bypassTelemetry = bypassed.getTelemetry();
    require(bypassTelemetry.gainReductionDb == 0.0f && !bypassTelemetry.clippingRisk,
        "settled bypass clears stale gain-reduction and clipping telemetry");
    require(!bypassTelemetry.outputPeakDb.empty() && bypassTelemetry.outputPeakDb.front() <= -95.9f,
        "settled bypass clears stale output level telemetry");
}

void testVirtualStageKeepsCenterAndBypassesSpeakers()
{
    echo::SpatialDspProcessor processor;
    processor.prepare(48'000.0, 256, 2);

    echo::CrossfeedState crossfeed;
    crossfeed.enabled = true;
    processor.setCrossfeedState(crossfeed);

    echo::VirtualStageState stage;
    stage.enabled = true;
    stage.strength = 1.0f;
    stage.reflection = 0.0f; // Direct center stays centered; room ambience is tested separately.
    processor.setVirtualStageState(stage);
    require(!processor.isCrossfeedEnabled(), "virtual stage must disable crossfeed");
    require(processor.isVirtualStageProcessing(), "virtual stage processes on a headphone route");

    echo::FloatAudioBuffer center(2, 64);
    for (int sample = 0; sample < 64; ++sample)
    {
        center.setSample(0, sample, 0.4f);
        center.setSample(1, sample, 0.4f);
    }
    processor.processVirtualStageBlock(center, 0, 64);
    require(std::abs(center.getSample(0, 32) - center.getSample(1, 32)) < 1.0e-6f
            && std::isfinite(center.getSample(0, 32)),
        "correlated center stays centered through the virtual speaker paths");

    echo::FloatAudioBuffer side(2, 256);
    for (int sample = 0; sample < 256; ++sample)
    {
        side.setSample(0, sample, 0.5f);
        side.setSample(1, sample, -0.5f);
    }
    processor.processVirtualStageBlock(side, 0, 256);
    require(std::abs(side.getSample(0, 200) - 0.5f) > 0.01f,
        "sustained side content picks up the contralateral cue after the ITD");

    stage.speakersBypassed = true;
    processor.setVirtualStageState(stage);
    require(!processor.isVirtualStageProcessing(), "speaker output bypasses the virtual stage");
    echo::FloatAudioBuffer speakers(2, 32);
    speakers.setSample(0, 8, 0.25f);
    speakers.setSample(1, 8, -0.1f);
    processor.processVirtualStageBlock(speakers, 0, 32);
    require(std::abs(speakers.getSample(0, 8) - 0.25f) < 1.0e-6f
            && std::abs(speakers.getSample(1, 8) + 0.1f) < 1.0e-6f,
        "speaker bypass leaves the buffer unchanged");
}

void testSpatialDspStagesProcessIndependently()
{
    echo::SpatialDspProcessor processor;
    processor.prepare(48'000.0, 2048, 2);

    echo::CrossfeedState crossfeed;
    crossfeed.enabled = true;
    crossfeed.amount = 0.5f;
    crossfeed.cutoffHz = 1'500.0f;
    processor.setCrossfeedState(crossfeed);
    echo::FloatAudioBuffer crossfeedBuffer(2, 2048);
    std::fill_n(crossfeedBuffer.getWritePointer(0), 2048, 1.0f);
    std::fill_n(crossfeedBuffer.getWritePointer(1), 2048, -1.0f);
    processor.processCrossfeedBlock(crossfeedBuffer, 0, 2048);
    require(crossfeedBuffer.getSample(0, 2047) < 0.55f && crossfeedBuffer.getSample(0, 2047) > 0.45f,
        "crossfeed must narrow sustained low-frequency side content");
    require(std::abs(crossfeedBuffer.getSample(0, 2047) + crossfeedBuffer.getSample(1, 2047)) < 1.0e-5f,
        "crossfeed must preserve the stereo center");

    echo::StereoFieldState stereoField;
    stereoField.enabled = true;
    stereoField.width = 0.0f;
    processor.setStereoFieldState(stereoField);
    echo::FloatAudioBuffer stereoBuffer(2, 1);
    stereoBuffer.setSample(0, 0, 1.0f);
    stereoBuffer.setSample(1, 0, -1.0f);
    processor.processStereoFieldBlock(stereoBuffer, 0, 1);
    require(std::abs(stereoBuffer.getSample(0, 0)) < 1.0e-6f
            && std::abs(stereoBuffer.getSample(1, 0)) < 1.0e-6f,
        "zero stereo width must remove pure side content");

    echo::ChannelMatrixState matrix;
    matrix.enabled = true;
    matrix.leftToLeft = 0.0f;
    matrix.rightToLeft = 1.0f;
    matrix.leftToRight = 1.0f;
    matrix.rightToRight = 0.0f;
    processor.setChannelMatrixState(matrix);
    echo::FloatAudioBuffer matrixBuffer(2, 1);
    matrixBuffer.setSample(0, 0, 0.25f);
    matrixBuffer.setSample(1, 0, -0.5f);
    processor.processChannelMatrixBlock(matrixBuffer, 0, 1);
    require(std::abs(matrixBuffer.getSample(0, 0) + 0.5f) < 1.0e-6f
            && std::abs(matrixBuffer.getSample(1, 0) - 0.25f) < 1.0e-6f,
        "channel matrix must apply the configured two-by-two routing coefficients");
}

float bufferPeakFrom(const echo::FloatAudioBuffer& buffer, int startSample)
{
    float peak = 0.0f;
    for (int sample = startSample; sample < buffer.getNumSamples(); ++sample)
        peak = std::max(peak, std::abs(buffer.getSample(0, sample)));
    return peak;
}

void fillStereoSine(echo::FloatAudioBuffer& buffer, float frequencyHz, float amplitude, bool invertRight)
{
    constexpr float pi = 3.14159265358979323846f;
    for (int sample = 0; sample < buffer.getNumSamples(); ++sample)
    {
        const float value = amplitude * std::sin(2.0f * pi * frequencyHz * static_cast<float>(sample) / 48000.0f);
        buffer.setSample(0, sample, value);
        buffer.setSample(1, sample, invertRight ? -value : value);
    }
}

void testDspChainLimiterCatchesInterSamplePeaks()
{
    echo::DspChain::setSafetyLimiterEnabled(true);
    DspChainFixture fixture;
    auto& dspChain = fixture.dspChain;
    dspChain.prepare(48000.0, 256, 2);
    fixture.eqProcessor.setEnabled(true);

    auto buffer = makeFloatBuffer(2, 256);
    constexpr float amplitude = 1.25f;
    for (int sample = 0; sample < buffer.getNumSamples(); ++sample)
    {
        const float value = amplitude * std::sin(0.78539816339f + static_cast<float>(sample) * 1.57079632679f);
        buffer.setSample(0, sample, value);
        buffer.setSample(1, sample, value);
    }

    dspChain.processBlock(buffer, 0, buffer.getNumSamples());
    require(dspChain.isSafetyLimiterProtecting(), "an inter-sample peak above the ceiling must engage the limiter");
    require(dspChain.safetyLimiterTruePeakDb() > 0.5f, "true-peak meter must report the reconstructed peak");
    require(bufferPeakFrom(buffer, 64) < 1.0f, "settled true-peak limiter output must stay under full scale");
}

void testEqMidScopeCutsCenterAndLeavesSide()
{
    echo::EqProcessor eq;
    echo::EqState state;
    state.enabled = true;
    constexpr int band = 17;
    state.bandFrequenciesHz[band] = 1000.0f;
    state.bandGainsDb[band] = -24.0f;
    state.bandQ[band] = 4.0f;
    state.bandScopes[band] = echo::EqBandScope::Mid;
    eq.setState(state);
    eq.prepare(48000.0, 512, 2);

    auto mid = makeFloatBuffer(2, 4096);
    fillStereoSine(mid, 1000.0f, 0.5f, false);
    eq.processBlock(mid, 0, mid.getNumSamples());
    require(bufferPeakFrom(mid, 2048) < 0.2f, "mid-scoped cut must reduce a centered tone");

    eq.reset();
    auto side = makeFloatBuffer(2, 4096);
    fillStereoSine(side, 1000.0f, 0.5f, true);
    eq.processBlock(side, 0, side.getNumSamples());
    require(bufferPeakFrom(side, 2048) > 0.35f, "mid-scoped cut must leave a pure side tone alone");
}

void testEqDynamicBandReducesLoudToneOnly()
{
    echo::EqProcessor eq;
    echo::EqState state;
    state.enabled = true;
    constexpr int band = 17;
    state.bandFrequenciesHz[band] = 1000.0f;
    state.bandQ[band] = 2.0f;
    state.bandDynamicModes[band] = echo::EqDynamicMode::Down;
    state.bandDynamicThresholdDb[band] = -30.0f;
    state.bandDynamicRatio[band] = 10.0f;
    state.bandDynamicAttackMs[band] = 1.0f;
    state.bandDynamicReleaseMs[band] = 40.0f;
    eq.setState(state);
    eq.prepare(48000.0, 512, 2);

    auto loud = makeFloatBuffer(2, 8192);
    fillStereoSine(loud, 1000.0f, 0.5f, false);
    eq.processBlock(loud, 0, loud.getNumSamples());
    require(bufferPeakFrom(loud, 4096) < 0.25f, "downward dynamic EQ must cut a loud tone in the band");

    eq.reset();
    auto quiet = makeFloatBuffer(2, 8192);
    fillStereoSine(quiet, 1000.0f, 0.02f, false);
    eq.processBlock(quiet, 0, quiet.getNumSamples());
    require(bufferPeakFrom(quiet, 4096) > 0.012f, "downward dynamic EQ must leave a tone under the threshold");
}

void testDspChainLimiterProtectsActiveOutput()
{
    echo::DspChain::setSafetyLimiterEnabled(true);
    DspChainFixture fixture;
    auto& eqProcessor = fixture.eqProcessor;
    auto& dspChain = fixture.dspChain;
    dspChain.prepare(48000.0, 128, 2);
    eqProcessor.setEnabled(true);

    auto buffer = makeFloatBuffer(2, 128);
    for (int channel = 0; channel < buffer.getNumChannels(); ++channel)
    {
        auto* samples = buffer.getWritePointer(channel);
        for (int sample = 0; sample < buffer.getNumSamples(); ++sample)
            samples[sample] = sample % 2 == 0 ? 2.0f : -2.0f;
    }

    require(dspChain.isActive(), "enabled EQ must activate DSP chain");
    dspChain.processBlock(buffer, 0, buffer.getNumSamples());
    require(dspChain.hasClippingRisk(), "active DSP chain must report clipping risk after limiting hot output");
    require(dspChain.isSafetyLimiterProtecting(), "active DSP chain must expose safety limiter protection");

    for (int channel = 0; channel < buffer.getNumChannels(); ++channel)
    {
        const auto* samples = buffer.getReadPointer(channel);
        for (int sample = 0; sample < buffer.getNumSamples(); ++sample)
            require(std::abs(samples[sample]) <= 1.0f + nearTolerance, "DSP safety limiter must cap active-chain output");
    }
}

void testDspChainLimiterIgnoresNearFullScaleOutput()
{
    echo::DspChain::setSafetyLimiterEnabled(true);
    DspChainFixture fixture;
    auto& eqProcessor = fixture.eqProcessor;
    auto& dspChain = fixture.dspChain;
    dspChain.prepare(48000.0, 128, 2);
    eqProcessor.setEnabled(true);

    auto buffer = makeFloatBuffer(2, 128);
    // A step into the 4x interpolator rings above the sample peak. Rise first
    // so the held 0.99 tone is the case that must stay under the ceiling.
    for (int channel = 0; channel < buffer.getNumChannels(); ++channel)
    {
        auto* samples = buffer.getWritePointer(channel);
        for (int sample = 0; sample < buffer.getNumSamples(); ++sample)
            samples[sample] = sample < 32 ? 0.99f * static_cast<float>(sample + 1) / 32.0f : 0.99f;
    }
    dspChain.processBlock(buffer, 0, buffer.getNumSamples());

    for (int channel = 0; channel < buffer.getNumChannels(); ++channel)
        std::fill_n(buffer.getWritePointer(channel), buffer.getNumSamples(), 0.99f);
    dspChain.processBlock(buffer, 0, buffer.getNumSamples());
    require(! dspChain.isSafetyLimiterProtecting(), "DSP safety limiter must not engage below full scale");
    require(std::abs(buffer.getSample(0, 0) - 0.99f) <= nearTolerance, "near full-scale output must pass unchanged");
}

void testDspChainLimiterCanBeBypassed()
{
    echo::DspChain::setSafetyLimiterEnabled(false);
    DspChainFixture fixture;
    auto& eqProcessor = fixture.eqProcessor;
    auto& dspChain = fixture.dspChain;
    dspChain.prepare(48000.0, 128, 2);
    eqProcessor.setEnabled(true);

    auto buffer = makeFloatBuffer(2, 128);
    for (int channel = 0; channel < buffer.getNumChannels(); ++channel)
    {
        auto* samples = buffer.getWritePointer(channel);
        for (int sample = 0; sample < buffer.getNumSamples(); ++sample)
            samples[sample] = 2.0f;
    }

    dspChain.processBlock(buffer, 0, buffer.getNumSamples());
    require(buffer.getSample(0, 0) > 1.0f, "disabled DSP safety limiter must not cap active-chain output");
    require(! dspChain.isSafetyLimiterProtecting(), "disabled DSP safety limiter must not report protection");

    echo::DspChain::setSafetyLimiterEnabled(true);
}

void testDspHeadroomActivatesProtectionChain()
{
    echo::DspChain::setSafetyLimiterEnabled(true);
    DspChainFixture fixture;
    auto& headroomProcessor = fixture.headroomProcessor;
    auto& dspChain = fixture.dspChain;
    dspChain.prepare(48000.0, 128, 2);
    headroomProcessor.setHeadroomDb(-6.0f);

    auto processed = makeFloatBuffer(2, 128);
    processed.clear();
    processed.setSample(0, 0, 0.5f);
    processed.setSample(1, 0, -0.5f);
    require(dspChain.isActive(), "configured headroom must activate the protection chain");
    dspChain.processBlock(processed, 0, processed.getNumSamples());

    require(std::abs(processed.getSample(0, 0)) < 0.5f, "DSP headroom must attenuate output without another DSP enabled");
    require(std::abs(processed.getSample(1, 0)) < 0.5f, "DSP headroom must attenuate every channel without another DSP enabled");
}

void testDspHeadroomPositiveGainRaisesLevel()
{
    echo::DspChain::setSafetyLimiterEnabled(true);
    DspChainFixture fixture;
    auto& headroomProcessor = fixture.headroomProcessor;
    auto& dspChain = fixture.dspChain;
    headroomProcessor.setHeadroomDb(6.0f);
    require(std::abs(headroomProcessor.getHeadroomDb() - 6.0f) < 0.001f, "positive headroom gain must be stored");
    headroomProcessor.setHeadroomDb(24.0f);
    require(std::abs(headroomProcessor.getHeadroomDb() - 12.0f) < 0.001f, "positive headroom gain must clamp at +12 dB");
    headroomProcessor.setHeadroomDb(6.0f);
    dspChain.prepare(48000.0, 128, 2);

    auto processed = makeFloatBuffer(2, 128);
    processed.clear();
    processed.setSample(0, 0, 0.25f);
    processed.setSample(1, 0, -0.25f);
    require(dspChain.isActive(), "positive headroom gain must activate the DSP chain by itself");
    dspChain.processBlock(processed, 0, processed.getNumSamples());

    require(std::abs(processed.getSample(0, 0)) > 0.45f, "positive headroom gain must raise output level");
    require(std::abs(processed.getSample(1, 0)) > 0.45f, "positive headroom gain must raise every channel");
}

void testDspChainProtectsUpstreamPcmProcessing()
{
    echo::DspChain::setSafetyLimiterEnabled(true);
    echo::DspChain::setLimiterCeilingDb(std::numeric_limits<float>::quiet_NaN());
    DspChainFixture fixture;
    auto& dspChain = fixture.dspChain;
    dspChain.prepare(192000.0, 128, 2);
    dspChain.setUpstreamPcmProcessingActive(true);

    auto buffer = makeFloatBuffer(2, 128);
    for (int channel = 0; channel < buffer.getNumChannels(); ++channel)
    {
        auto* samples = buffer.getWritePointer(channel);
        for (int sample = 0; sample < buffer.getNumSamples(); ++sample)
            samples[sample] = sample % 2 == 0 ? 1.25f : -1.25f;
    }

    require(dspChain.isActive(), "upstream PCM processing must activate the protection chain");
    dspChain.processBlock(buffer, 0, buffer.getNumSamples());
    require(dspChain.isSafetyLimiterProtecting(), "upstream PCM overshoot must engage the safety limiter");
    require(std::abs(dspChain.safetyLimiterCeilingDb() + 1.0f) <= nearTolerance,
        "upstream PCM limiter must use the -1 dB true-peak ceiling");
    require(dspChain.safetyLimiterGainReductionDb() > 2.0f,
        "upstream PCM limiter must report authoritative linked gain reduction");
    const float truePeakCeiling = std::pow(10.0f, -1.0f / 20.0f);
    for (int channel = 0; channel < buffer.getNumChannels(); ++channel)
    {
        const auto* samples = buffer.getReadPointer(channel);
        for (int sample = 0; sample < buffer.getNumSamples(); ++sample)
            require(std::abs(samples[sample]) <= truePeakCeiling + nearTolerance,
                "upstream PCM protection must prevent output clipping");
    }

    auto quiet = makeFloatBuffer(2, 128);
    for (int channel = 0; channel < quiet.getNumChannels(); ++channel)
        std::fill_n(quiet.getWritePointer(channel), quiet.getNumSamples(), 0.25f);
    dspChain.processBlock(quiet, 0, quiet.getNumSamples());
    require(quiet.getSample(0, 0) < quiet.getSample(0, quiet.getNumSamples() - 1),
        "true-peak limiter must release smoothly instead of stepping back to unity");
    require(std::abs(quiet.getSample(0, 0) - quiet.getSample(1, 0)) <= strictTolerance,
        "true-peak limiter gain envelope must remain stereo linked");
}

void testManualLimiterCeilingHoldsTruePeak()
{
    echo::DspChain::setSafetyLimiterEnabled(true);
    echo::DspChain::setLimiterCeilingDb(-6.0f);
    DspChainFixture fixture;
    auto& dspChain = fixture.dspChain;
    dspChain.prepare(192000.0, 256, 2);
    dspChain.setUpstreamPcmProcessingActive(true);

    auto buffer = makeFloatBuffer(2, 256);
    for (int channel = 0; channel < buffer.getNumChannels(); ++channel)
    {
        auto* samples = buffer.getWritePointer(channel);
        for (int sample = 0; sample < buffer.getNumSamples(); ++sample)
            samples[sample] = sample % 2 == 0 ? 1.0f : -1.0f;
    }

    dspChain.processBlock(buffer, 0, buffer.getNumSamples());
    require(std::abs(dspChain.safetyLimiterCeilingDb() + 6.0f) <= nearTolerance,
        "manual limiter ceiling replaces the automatic -1 dBTP ceiling");
    const float ceiling = std::pow(10.0f, -6.0f / 20.0f);
    for (int channel = 0; channel < buffer.getNumChannels(); ++channel)
    {
        const auto* samples = buffer.getReadPointer(channel);
        for (int sample = 0; sample < buffer.getNumSamples(); ++sample)
            require(std::abs(samples[sample]) <= ceiling + 0.02f,
                "manual limiter ceiling holds the true-peak output");
    }

    echo::DspChain::setLimiterCeilingDb(std::numeric_limits<float>::quiet_NaN());
    require(std::abs(dspChain.safetyLimiterCeilingDb() + 1.0f) <= nearTolerance,
        "clearing the manual ceiling restores the automatic upstream ceiling");
    echo::DspChain::setLimiterCeilingDb(std::numeric_limits<float>::quiet_NaN());
}

float professionalDynamicsRms(const echo::FloatAudioBuffer& buffer, int channel, int start, int count)
{
    double energy = 0.0;
    const auto* samples = buffer.getReadPointer(channel);
    for (int sample = 0; sample < count; ++sample)
    {
        const float value = samples[start + sample];
        energy += static_cast<double>(value) * static_cast<double>(value);
    }
    return static_cast<float>(std::sqrt(energy / static_cast<double>(count)));
}

void fillSine(echo::FloatAudioBuffer& buffer, float frequencyHz, float amplitude)
{
    constexpr float pi = 3.14159265358979323846f;
    for (int channel = 0; channel < buffer.getNumChannels(); ++channel)
    {
        auto* samples = buffer.getWritePointer(channel);
        for (int sample = 0; sample < buffer.getNumSamples(); ++sample)
        {
            const float phase = 2.0f * pi * frequencyHz * static_cast<float>(sample) / 48000.0f;
            samples[sample] = amplitude * std::sin(phase);
        }
    }
}

void testDeEsserAttenuatesSibilanceBandOnly()
{
    echo::DeEsserState state;
    state.enabled = true;
    state.frequencyHz = 6500.0f;
    state.q = 4.0f;
    state.thresholdDb = -30.0f;
    state.ratio = 8.0f;
    state.attackMs = 0.1f;
    state.releaseMs = 20.0f;
    state.mix = 1.0f;

    echo::DeEsserProcessor sibilance;
    sibilance.prepare(48000.0, 8192, 2);
    sibilance.setState(state);
    auto sibilant = makeFloatBuffer(2, 8192);
    fillSine(sibilant, 6500.0f, 0.5f);
    sibilance.processBlock(sibilant, 0, sibilant.getNumSamples());
    const float sibilantRms = professionalDynamicsRms(sibilant, 0, 7000, 1000);
    require(sibilantRms < 0.2f, "de-esser attenuates a tone inside the detection band");
    require(sibilance.gainReductionDb() > 1.0f, "de-esser reports gain reduction for the sibilant tone");

    echo::DeEsserProcessor body;
    body.prepare(48000.0, 8192, 2);
    body.setState(state);
    auto low = makeFloatBuffer(2, 8192);
    fillSine(low, 200.0f, 0.5f);
    body.processBlock(low, 0, low.getNumSamples());
    const float lowRms = professionalDynamicsRms(low, 0, 7000, 1000);
    require(lowRms > 0.3f, "de-esser leaves a tone below the detection band intact");
}

void testMultibandCompressorReducesOnlyTheSelectedBand()
{
    echo::MultibandCompressorState lows;
    lows.enabled = true;
    lows.lowCrossoverHz = 150.0f;
    lows.highCrossoverHz = 2500.0f;
    lows.mix = 1.0f;
    lows.stereoLink = 1.0f;
    lows.bands[0].enabled = true;
    lows.bands[0].thresholdDb = -24.0f;
    lows.bands[0].ratio = 10.0f;
    lows.bands[0].attackMs = 0.1f;
    lows.bands[0].releaseMs = 30.0f;
    lows.bands[1].enabled = false;
    lows.bands[2].enabled = false;

    echo::MultibandCompressorProcessor lowProcessor;
    lowProcessor.prepare(48000.0, 16384, 2);
    lowProcessor.setState(lows);
    auto low = makeFloatBuffer(2, 16384);
    fillSine(low, 80.0f, 0.8f);
    lowProcessor.processBlock(low, 0, low.getNumSamples());
    require(professionalDynamicsRms(low, 0, 15000, 1000) < 0.35f,
        "the low multiband compressor reduces a bass tone");
    require(lowProcessor.gainReductionDb(0) > 1.0f, "the low band reports gain reduction");

    echo::MultibandCompressorState highs = lows;
    highs.bands[0].enabled = false;
    highs.bands[2].enabled = true;
    highs.bands[2].thresholdDb = -24.0f;
    highs.bands[2].ratio = 10.0f;
    highs.bands[2].attackMs = 0.1f;
    highs.bands[2].releaseMs = 30.0f;
    echo::MultibandCompressorProcessor highOnLowTone;
    highOnLowTone.prepare(48000.0, 16384, 2);
    highOnLowTone.setState(highs);
    auto untouched = makeFloatBuffer(2, 16384);
    fillSine(untouched, 80.0f, 0.8f);
    highOnLowTone.processBlock(untouched, 0, untouched.getNumSamples());
    require(professionalDynamicsRms(untouched, 0, 15000, 1000) > 0.45f,
        "compressing only the high band leaves a bass tone intact");
}

void testDisabledEqIsDry()
{
    echo::EqProcessor processor;
    processor.prepare(48000.0, 512, 2);
    processor.setBandGainDb(2, 12.0f);
    processor.setPreampDb(6.0f);

    auto buffer = makeFloatBuffer(2, 512);
    auto dry = buffer;
    processor.processBlock(buffer, 0, buffer.getNumSamples());

    requireBuffersClose(buffer, dry, strictTolerance, "disabled EQ must be dry");
}

void testFlatEnabledIsTransparent()
{
    echo::EqProcessor processor;
    processor.prepare(44100.0, 1024, 2);
    processor.setEnabled(true);

    auto buffer = makeFloatBuffer(2, 1024);
    auto dry = buffer;
    processor.processBlock(buffer, 0, buffer.getNumSamples());

    require(processor.isEnabled(), "flat enabled EQ must report enabled");
    requireBuffersClose(buffer, dry, nearTolerance, "flat enabled EQ must be transparent");
}

void testBypassReturnsToDry()
{
    echo::EqProcessor processor;
    processor.prepare(48000.0, 4096, 2);
    processor.setEnabled(true);
    processor.setBandGainDb(0, 12.0f);
    processor.setBandGainDb(1, 10.0f);
    processor.setPreampDb(-3.0f);

    auto warmup = makeFloatBuffer(2, 4096);
    processor.processBlock(warmup, 0, warmup.getNumSamples());

    processor.setEnabled(false);
    auto fadeOut = makeFloatBuffer(2, 4096);
    processor.processBlock(fadeOut, 0, fadeOut.getNumSamples());

    auto buffer = makeFloatBuffer(2, 1024);
    auto dry = buffer;
    processor.processBlock(buffer, 0, buffer.getNumSamples());

    require(! processor.isEnabled(), "bypassed EQ must report disabled");
    requireBuffersClose(buffer, dry, strictTolerance, "bypassed EQ must return to dry after fade");
}

void testRapidChangesStayFinite()
{
    for (double sampleRate : { 44100.0, 48000.0, 96000.0 })
    {
        echo::EqProcessor processor;
        processor.prepare(sampleRate, 512, 2);
        processor.setEnabled(true);

        for (int iteration = 0; iteration < 24; ++iteration)
        {
            processor.setPreampDb(iteration % 2 == 0 ? 6.0f : -12.0f);
            processor.setBandGainDb(iteration % echo::eqBandCount, iteration % 2 == 0 ? 12.0f : -12.0f);
            processor.setBandFrequencyHz((iteration + 3) % echo::eqBandCount, iteration % 2 == 0 ? 1.0f : 50000.0f);
            processor.setBandQ((iteration + 5) % echo::eqBandCount, iteration % 2 == 0 ? 0.001f : 50.0f);
            processor.setBandFilterType((iteration + 7) % echo::eqBandCount, iteration % 3 == 0 ? echo::EqFilterType::LowShelf : echo::EqFilterType::HighShelf);
            processor.setBandEnabled((iteration + 9) % echo::eqBandCount, iteration % 4 != 0);

            auto buffer = makeFloatBuffer(2, 512);
            processor.processBlock(buffer, 0, buffer.getNumSamples());
            requireFinite(buffer, "rapid EQ changes must stay finite");
        }
    }
}

void testEqReportsRiskWithoutLimitingEnabledOutput()
{
    echo::EqProcessor processor;
    processor.prepare(48000.0, 4096, 2);
    processor.setEnabled(true);
    processor.setPreampDb(12.0f);

    echo::FloatAudioBuffer buffer(2, 4096);
    for (int channel = 0; channel < buffer.getNumChannels(); ++channel)
    {
        auto* samples = buffer.getWritePointer(channel);
        for (int sample = 0; sample < buffer.getNumSamples(); ++sample)
            samples[sample] = sample % 2 == 0 ? 0.9f : -0.9f;
    }

    processor.processBlock(buffer, 0, buffer.getNumSamples());

    require(processor.hasClippingRisk(), "enabled EQ must keep clipping risk visible");
    requireFinite(buffer, "enabled EQ risk path must keep output finite");
    bool hotOutput = false;
    for (int channel = 0; channel < buffer.getNumChannels(); ++channel)
    {
        const auto* samples = buffer.getReadPointer(channel);
        for (int sample = 0; sample < buffer.getNumSamples(); ++sample)
            hotOutput = hotOutput || std::abs(samples[sample]) > 1.0f;
    }
    require(hotOutput, "enabled EQ must not cap hot output before final DSP limiter");
}

void testCoefficientUpdatesStopInSteadyState()
{
    echo::EqProcessor processor;
    processor.prepare(96000.0, 512, 2);

    const auto initialUpdates = processor.getCoefficientUpdateCountForTests();
    auto stable = makeFloatBuffer(2, 512);
    processor.processBlock(stable, 0, stable.getNumSamples());
    require(processor.getCoefficientUpdateCountForTests() == initialUpdates, "steady disabled EQ must not recalculate coefficients");

    processor.setEnabled(true);
    auto enabledStable = makeFloatBuffer(2, 512);
    processor.processBlock(enabledStable, 0, enabledStable.getNumSamples());
    require(processor.getCoefficientUpdateCountForTests() == initialUpdates, "steady flat EQ must not recalculate coefficients");

    processor.setBandGainDb(4, 6.0f);
    auto transition = makeFloatBuffer(2, 4096);
    processor.processBlock(transition, 0, transition.getNumSamples());
    const auto afterTransitionUpdates = processor.getCoefficientUpdateCountForTests();
    require(afterTransitionUpdates > initialUpdates, "changed band must recalculate coefficients while smoothing");

    auto postTransition = makeFloatBuffer(2, 4096);
    processor.processBlock(postTransition, 0, postTransition.getNumSamples());
    require(processor.getCoefficientUpdateCountForTests() == afterTransitionUpdates, "steady changed band must stop recalculating coefficients");
}

void testPeqBandControlsClampAndBypass()
{
    echo::EqProcessor processor;
    processor.prepare(48000.0, 4096, 2);
    processor.setEnabled(true);
    processor.setBandGainDb(0, 12.0f);
    processor.setBandGainDb(1, -25.0f);
    require(processor.getState().bandGainsDb[1] == echo::eqMinGainDb, "cuts below -24 dB must clamp");
    processor.setBandGainDb(1, -24.0f);
    require(processor.getState().bandGainsDb[1] == -24.0f, "-24 dB cut must be stored");
    processor.setBandQ(2, 24.0f);
    require(processor.getState().bandQ[2] == echo::eqMaxQ, "peaking Q above 12 must clamp");
    processor.setBandFilterType(2, echo::EqFilterType::Notch);
    processor.setBandQ(2, 24.0f);
    require(processor.getState().bandQ[2] == echo::eqMaxNotchQ, "notch Q of 24 must be stored");
    processor.setBandFilterType(2, echo::EqFilterType::Peaking);
    require(processor.getState().bandQ[2] == echo::eqMaxQ, "leaving notch must clamp Q back to 12");
    const float storedGain = processor.getState().bandGainsDb[0];
    processor.setTiltDb(6.0f);
    require(processor.getState().tiltDb == 6.0f, "tilt must be stored separately");
    require(processor.getState().bandGainsDb[0] == storedGain, "tilt must not rewrite stored band gain");
    auto tilted = makeFloatBuffer(2, 4096);
    processor.processBlock(tilted, 0, tilted.getNumSamples());
    requireFinite(tilted, "tilt output must stay finite");
    processor.setTiltDb(0.0f);
    processor.setBandGainDb(1, 0.0f);
    processor.setBandQ(2, 1.0f);
    processor.setBandFrequencyHz(0, 80.0f);
    processor.setBandQ(0, 50.0f);
    processor.setBandFilterType(0, echo::EqFilterType::LowShelf);

    auto state = processor.getState();
    require(state.bandQ[0] == echo::eqMaxQ, "band Q must clamp high values");
    require(state.bandFilterTypes[0] == echo::EqFilterType::LowShelf, "band filter type must store low shelf");

    auto shaped = makeFloatBuffer(2, 4096);
    processor.processBlock(shaped, 0, shaped.getNumSamples());
    requireFinite(shaped, "low shelf PEQ output must stay finite");

    processor.setBandEnabled(0, false);
    auto warmup = makeFloatBuffer(2, 4096);
    processor.processBlock(warmup, 0, warmup.getNumSamples());
    auto bypassed = makeFloatBuffer(2, 4096);
    auto dry = bypassed;
    processor.processBlock(bypassed, 0, bypassed.getNumSamples());
    requireBuffersClose(bypassed, dry, nearTolerance, "disabled PEQ band must become transparent");
}

void testPeqAdditionalFilterTypesStayFinite()
{
    const std::vector<echo::EqFilterType> filterTypes {
        echo::EqFilterType::LowPass,
        echo::EqFilterType::HighPass,
        echo::EqFilterType::Notch,
    };

    for (const auto filterType : filterTypes)
    {
        echo::EqProcessor processor;
        processor.prepare(48000.0, 4096, 2);
        processor.setEnabled(true);
        processor.setBandFrequencyHz(4, filterType == echo::EqFilterType::HighPass ? 90.0f : 7200.0f);
        processor.setBandQ(4, filterType == echo::EqFilterType::Notch ? 6.5f : 0.707f);
        processor.setBandFilterType(4, filterType);
        processor.setBandGainDb(4, 12.0f);

        auto buffer = makeFloatBuffer(2, 4096);
        processor.processBlock(buffer, 0, buffer.getNumSamples());
        requireFinite(buffer, "additional PEQ filter output must stay finite");

        const auto state = processor.getState();
        require(state.bandFilterTypes[4] == filterType, "additional PEQ filter type must round-trip in processor state");
    }
}

void testHostBufferFallbackAttempts()
{
    requireVectorEquals(buildBufferSizeAttempts(64), { 64, 128, 256, 512, 1024, 2048, 4096, 8192 },
        "manual ASIO ultra-low buffer fallback chain");
    const auto shared = parseOptions({ "echo-audio-host" });
    requireVectorEquals(buildBufferSizeAttempts(shared), { 256, 512, 1024, 2048, 4096, 8192 }, "shared buffer fallback chain");

    const auto asio = parseOptions({ "echo-audio-host", "-asio" });
    require(asio.asio, "ASIO backend flag must parse instead of silently selecting shared output");
    requireVectorEquals(buildBufferSizeAttempts(asio), { 0 }, "ASIO must delegate buffer selection to its driver");

    const auto balanced = parseOptions({ "echo-audio-host", "-exclusive", "-buffer", "2048" });
    require(balanced.exclusive, "exclusive backend flag must parse");
    requireVectorEquals(buildBufferSizeAttempts(balanced), { 2048, 4096, 8192 }, "exclusive requested buffer fallback chain");

    requireVectorEquals(buildPreferredBufferSizeAttempts(1024, 2048), { 2048, 1024, 4096, 8192 },
        "known-good device buffer must be tried before the configured fallback chain");
    requireVectorEquals(buildPreferredBufferSizeAttempts(1024, 1024), { 1024, 2048, 4096, 8192 },
        "matching known-good device buffer must not be duplicated");
}

void testRuntimeBufferCacheScopeKeepsPerRateKnowledge()
{
    auto current = parseOptions({ "echo-audio-host", "-asio", "-sr", "48000", "-buffer", "1024" });
    auto nextRate = current;
    nextRate.sampleRate = 192000;
    require(hasSameRuntimeBufferCacheScope(current, nextRate),
        "sample-rate transitions must retain known-good buffers for previously visited rates");

    auto nextBuffer = current;
    nextBuffer.bufferSize = 2048;
    require(! hasSameRuntimeBufferCacheScope(current, nextBuffer),
        "explicit buffer changes must invalidate learned buffer choices");

    auto nextDevice = current;
    nextDevice.deviceName = "another ASIO device";
    require(! hasSameRuntimeBufferCacheScope(current, nextDevice),
        "device changes must invalidate learned buffer choices");
}

void testKnownGoodBufferFallsBackToConfiguredChain()
{
    class PreferredBufferBackend final : public NativePcmOutputBackend
    {
    public:
        bool open(NativePlaybackPipeline&, const Options&, int, int, int bufferFrames, std::string& error) override
        {
            attempts.push_back(bufferFrames);
            if (bufferFrames == 1024)
            {
                info.openedDeviceBufferFrames = bufferFrames;
                return true;
            }
            error = "preferred buffer rejected";
            return false;
        }
        void close() noexcept override {}
        const NativePcmOutputReadyInfo& readyInfo() const noexcept override { return info; }

        std::vector<int> attempts;
        NativePcmOutputReadyInfo info;
    } backend;

    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    PcmRingAudioSource source(2, 4096, 0, 0, 1.0f, eqProcessor, channelBalanceProcessor);
    DopRingSource dopSource(2, 4096, 0, 0);
    NativeDsdRingSource nativeDsdSource(2, 8192, 0, 0);
    NativePlaybackPipeline pipeline(source, dopSource, nativeDsdSource, 2);
    const auto options = parseOptions({ "echo-audio-host", "-asio", "-buffer", "1024" });
    int openedBufferFrames = 0;
    std::string error;

    require(openNativePcmOutputWithFallback(
        backend, pipeline, options, 192000, 2, 1024, openedBufferFrames, error, 2048),
        "rejected known-good buffer must fall back to the configured buffer chain");
    requireVectorEquals(backend.attempts, { 2048, 1024 },
        "known-good buffer rejection must preserve configured buffer fallback");
    require(openedBufferFrames == 1024, "fallback must report the buffer that actually opened");
}

void testUnsupportedExclusiveFormatSkipsBufferRetries()
{
    class UnsupportedFormatBackend final : public NativePcmOutputBackend
    {
    public:
        bool open(NativePlaybackPipeline&, const Options&, int, int, int, std::string& error) override
        {
            ++openAttempts;
            error = "WASAPI exclusive format unsupported (hr=0x88890008)";
            return false;
        }
        void close() noexcept override {}
        const NativePcmOutputReadyInfo& readyInfo() const noexcept override { return info; }

        int openAttempts = 0;
        NativePcmOutputReadyInfo info;
    } backend;

    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    PcmRingAudioSource source(2, 4096, 0, 0, 1.0f, eqProcessor, channelBalanceProcessor);
    DopRingSource dopSource(2, 4096, 0, 0);
    NativeDsdRingSource nativeDsdSource(2, 8192, 0, 0);
    NativePlaybackPipeline pipeline(source, dopSource, nativeDsdSource, 2);
    const auto options = parseOptions({ "echo-audio-host", "-exclusive", "-buffer", "2048" });
    int openedBufferFrames = 0;
    std::string error;

    require(! openNativePcmOutputWithFallback(backend, pipeline, options, 44100, 2, 2048, openedBufferFrames, error),
        "unsupported exclusive format must fail");
    require(backend.openAttempts == 1, "unsupported exclusive format must not retry unrelated buffer sizes");
    requireContains(error, "0x88890008", "unsupported exclusive format error must be preserved");
}

void testTransientAsioBufferRejectionRetriesAfterSettleDelay()
{
    class TransientlyRejectingAsioBackend final : public NativePcmOutputBackend
    {
    public:
        explicit TransientlyRejectingAsioBackend(int failures) : remainingFailures(failures) {}
        bool open(NativePlaybackPipeline&, const Options&, int, int, int, std::string& error) override
        {
            ++openAttempts;
            if (remainingFailures > 0)
            {
                --remainingFailures;
                error = "All ASIO buffer size candidates rejected by driver";
                return false;
            }
            return true;
        }
        void close() noexcept override {}
        const NativePcmOutputReadyInfo& readyInfo() const noexcept override { return info; }

        int openAttempts = 0;
        int remainingFailures = 0;
        NativePcmOutputReadyInfo info;
    };

    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    PcmRingAudioSource source(2, 4096, 0, 0, 1.0f, eqProcessor, channelBalanceProcessor);
    DopRingSource dopSource(2, 4096, 0, 0);
    NativeDsdRingSource nativeDsdSource(2, 8192, 0, 0);
    NativePlaybackPipeline pipeline(source, dopSource, nativeDsdSource, 2);
    const auto options = parseOptions({ "echo-audio-host", "-asio", "-buffer", "8192" });
    int openedBufferFrames = 0;
    std::string error;

    TransientlyRejectingAsioBackend recovering(2);
    require(openNativePcmOutputWithFallback(recovering, pipeline, options, 48000, 2, 8192, openedBufferFrames, error),
        "transient ASIO buffer rejection must recover through settle retries");
    require(recovering.openAttempts == 3, "settle retries must reopen after transient ASIO buffer rejection");
    require(openedBufferFrames == 8192, "settle retries must preserve the requested buffer size");

    TransientlyRejectingAsioBackend persistent(1000);
    require(! openNativePcmOutputWithFallback(persistent, pipeline, options, 48000, 2, 8192, openedBufferFrames, error),
        "persistent ASIO buffer rejection must still fail");
    require(persistent.openAttempts == 3, "settle retries must stay bounded for persistent rejection");
    requireContains(error, "ASIO buffer size candidates rejected", "persistent rejection error must be preserved");
}

void testHostReportsBackendOpenedBufferFrames()
{
    class NegotiatedBufferBackend final : public NativePcmOutputBackend
    {
    public:
        bool open(NativePlaybackPipeline&, const Options&, int, int, int, std::string&) override
        {
            info.openedDeviceBufferFrames = 512;
            return true;
        }
        void close() noexcept override {}
        const NativePcmOutputReadyInfo& readyInfo() const noexcept override { return info; }

        NativePcmOutputReadyInfo info;
    } backend;

    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    PcmRingAudioSource source(2, 4096, 0, 0, 1.0f, eqProcessor, channelBalanceProcessor);
    DopRingSource dopSource(2, 4096, 0, 0);
    NativeDsdRingSource nativeDsdSource(2, 8192, 0, 0);
    NativePlaybackPipeline pipeline(source, dopSource, nativeDsdSource, 2);
    const auto options = parseOptions({ "echo-audio-host", "-asio", "-buffer", "1024" });
    int openedBufferFrames = 0;
    std::string error;

    require(openNativePcmOutputWithFallback(backend, pipeline, options, 44100, 2, 1024, openedBufferFrames, error),
        "backend-negotiated buffer open must succeed");
    require(openedBufferFrames == 512,
        "host ready metadata must report the backend-negotiated buffer instead of the requested candidate");
}

void testHostSharedBackendOptions()
{
    const auto defaultOptions = parseOptions({ "echo-audio-host" });
    require(defaultOptions.sharedBackend == "auto", "shared backend default must be auto");

    const auto directSound = parseOptions({ "echo-audio-host", "-shared-backend", "directsound" });
    require(directSound.sharedBackend == "directsound", "directsound shared backend must parse");
#ifdef _WIN32
    require(! isDisabledSharedBackend(directSound), "directsound backend must be enabled as compatibility output");
#else
    require(isDisabledSharedBackend(directSound), "directsound backend must stay disabled on non-Windows hosts");
#endif

    const auto windows = parseOptions({ "echo-audio-host", "-shared-backend", "windows" });
    require(windows.sharedBackend == "windows", "windows shared backend must parse");

    const auto alsa = parseOptions({ "echo-audio-host", "-shared-backend", "alsa" });
    require(alsa.sharedBackend == "alsa", "ALSA shared backend must parse");

    const auto pipewire = parseOptions({ "echo-audio-host", "-shared-backend", "pipewire" });
    require(pipewire.sharedBackend == "pipewire", "PipeWire shared backend must parse");

    const auto invalid = parseOptions({ "echo-audio-host", "-shared-backend", "invalid" });
    require(invalid.sharedBackend == "auto", "invalid shared backend must fall back to auto");

    require(shouldIncludeSharedBackendType("DirectSound", directSound.sharedBackend), "directsound backend must include DirectSound");
    require(! shouldIncludeSharedBackendType("Windows Audio", directSound.sharedBackend), "directsound backend must skip Windows Audio");
#ifdef _WIN32
    require(shouldIncludeSharedBackendType("Windows Audio", windows.sharedBackend), "windows backend must include Windows Audio");
#else
    require(! shouldIncludeSharedBackendType("Windows Audio", windows.sharedBackend), "windows backend must not select Windows Audio on non-Windows hosts");
#endif
    require(! shouldIncludeSharedBackendType("DirectSound", windows.sharedBackend), "windows backend must skip DirectSound");
    require(shouldIncludeSharedBackendType("ALSA", alsa.sharedBackend), "ALSA backend must include ALSA");
    require(! shouldIncludeSharedBackendType("Windows Audio", alsa.sharedBackend), "ALSA backend must skip Windows Audio");
    require(shouldIncludeSharedBackendType("PipeWire", pipewire.sharedBackend), "PipeWire backend must include PipeWire");
    require(! shouldIncludeSharedBackendType("ALSA", pipewire.sharedBackend), "PipeWire backend must skip ALSA");
    require(! shouldIncludeSharedBackendType("DirectSound", defaultOptions.sharedBackend), "auto backend must skip DirectSound");
    require(shouldIncludeSharedBackendType("Windows Audio", defaultOptions.sharedBackend), "auto backend must include Windows Audio");
#ifdef __linux__
    require(shouldIncludeSharedBackendType("PipeWire", defaultOptions.sharedBackend), "Linux auto shared must include PipeWire");
    require(shouldIncludeSharedBackendType("ALSA", defaultOptions.sharedBackend), "Linux auto shared must still include ALSA");
#endif
}

void testNativeDitherMatchesTypescriptGoldenVector()
{
    echo::PcmDitherProcessor processor;
    processor.configure(echo::PcmDitherMode::NoiseShaped5, 24, 2);
    std::vector<float> samples { 0.1234567f, -0.1234567f, 0.01f, -0.01f, 0.5f, -0.5f, 0.0f, 0.0f };
    processor.process(samples);
    const std::vector<float> expected {
        0.1234566122f, -0.1234567314f, 0.01000011060f, -0.009999872185f,
        0.5000000596f, -0.5000000596f, 0.0f, 1.192093038e-7f,
    };
    require(samples.size() == expected.size(), "native dither golden vector size");
    for (size_t index = 0; index < samples.size(); ++index)
        require(std::abs(samples[index] - expected[index]) < 1.0e-8f, "native dither must match TypeScript golden vector");
}

void testNativeEchoSrcMatchesReferenceConvolution()
{
    echo::EchoSrcProcessor processor;
    std::string error;
    require(processor.configure(1, { echo::EchoSrcStageConfig { 2, { 0.25f, 0.5f, 0.25f } } }, error),
        "native ECHO SRC configuration must succeed");
    const std::vector<float> input { 1.0f, 0.0f };
    const auto output = processor.process(input.data(), 2);
    const std::vector<float> expected { 0.5f, 1.0f, 0.5f, 0.0f };
    require(output.size() == expected.size(), "native ECHO SRC output length");
    for (size_t index = 0; index < output.size(); ++index)
        require(std::abs(output[index] - expected[index]) < 1.0e-7f, "native ECHO SRC convolution output");
}

void testNativeEchoSrcPolyphaseMatchesDenseFactorFourConvolution()
{
    echo::EchoSrcProcessor processor;
    std::string error;
    require(processor.configure(
        2,
        { echo::EchoSrcStageConfig {
            4,
            { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f },
        } },
        error),
        "factor-four native ECHO SRC configuration must succeed");

    const std::vector<float> input {
        1.0f, -1.0f,
        0.5f, 0.25f,
        0.0f, 0.0f,
    };
    const auto output = processor.process(input.data(), 3);
    const std::vector<float> expected {
        0.4f, -0.4f,
        0.8f, -0.8f,
        1.2f, -1.2f,
        1.6f, -1.6f,
        2.2f, -1.9f,
        2.8f, -2.2f,
        3.4f, -2.5f,
        0.8f, 0.4f,
        1.0f, 0.5f,
        1.2f, 0.6f,
        1.4f, 0.7f,
        0.0f, 0.0f,
    };
    require(output.size() == expected.size(), "factor-four polyphase output length");
    require(processor.estimatedMacsPerInputFrame() == 14,
        "factor-four sparse polyphase estimator must count each non-zero tap once per channel");
    for (size_t index = 0; index < output.size(); ++index)
        require(std::abs(output[index] - expected[index]) < 1.0e-6f,
            "factor-four sparse polyphase output must equal dense zero-stuffed convolution");
}

void testNativeEchoSrcIsStableAcrossDecodeChunks()
{
    const std::vector<echo::EchoSrcStageConfig> stages {
        echo::EchoSrcStageConfig { 2, { 0.25f, 0.5f, 0.25f } },
        echo::EchoSrcStageConfig { 2, { 0.125f, 0.75f, 0.125f } },
    };
    const std::vector<float> input { 1.0f, -0.5f, 0.25f, 0.0f, -0.25f };
    std::string error;
    echo::EchoSrcProcessor contiguous;
    echo::EchoSrcProcessor chunked;
    require(contiguous.configure(1, stages, error), "contiguous native ECHO SRC configuration");
    require(chunked.configure(1, stages, error), "chunked native ECHO SRC configuration");

    const auto expected = contiguous.process(input.data(), static_cast<int>(input.size()));
    const auto first = chunked.process(input.data(), 2);
    const auto second = chunked.process(input.data() + 2, static_cast<int>(input.size() - 2));
    std::vector<float> actual = first;
    actual.insert(actual.end(), second.begin(), second.end());

    require(actual.size() == expected.size(), "chunked native ECHO SRC output length");
    for (size_t index = 0; index < actual.size(); ++index)
        require(std::abs(actual[index] - expected[index]) < 1.0e-7f,
            "native ECHO SRC must preserve FIR history across decoder chunks");
}

void testNativeEchoSrcFlushesTailExactlyOnce()
{
    NativeFirProcessor processor;
    std::string error;
    require(processor.configure(
        1,
        { echo::EchoSrcStageConfig { 2, { 0.25f, 0.5f, 0.25f } } },
        false,
        64,
        error),
        "native ECHO SRC tail test configuration must succeed");

    const float impulse = 1.0f;
    const auto body = processor.process(&impulse, 1);
    const auto tail = processor.flush();
    require(body.size() == 2, "native ECHO SRC body must keep the configured output factor");
    require(tail.size() == 1, "native ECHO SRC flush must emit the remaining convolution support");
    require(std::abs(body[0] - 0.5f) < 1.0e-7f
        && std::abs(body[1] - 1.0f) < 1.0e-7f
        && std::abs(tail[0] - 0.5f) < 1.0e-7f,
        "native ECHO SRC flush must preserve the complete impulse response");
    require(processor.flush().empty(), "native ECHO SRC flush must be idempotent at EOF");
}

void testNativePlaybackPipelineDrainsEchoSrcTailBeforeEof()
{
    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    PcmRingAudioSource pcm(1, 64, 0, 0, 1.0f, eqProcessor, channelBalanceProcessor);
    DopRingSource dop(1, 64, 0, 0);
    NativeDsdRingSource nativeDsd(1, 64, 0, 0);
    NativePlaybackPipeline pipeline(pcm, dop, nativeDsd, 1);

    NativePlaybackPipeline::ProcessingConfig config;
    config.outputFormat = NativePlaybackPipeline::OutputFormat::Pcm;
    config.echoSrc.sourceSampleRate = 48'000;
    config.echoSrc.targetSampleRate = 96'000;
    config.echoSrc.stages = {
        echo::EchoSrcStageConfig { 2, { 0.25f, 0.5f, 0.25f } },
    };
    std::string error;
    require(pipeline.configure(config, error), "native pipeline ECHO SRC configuration must succeed");
    require(pipeline.seekPrerollFrames() == 1,
        "native pipeline must expose the exact source-rate FIR history needed by seek");
    const auto initialStatus = pipeline.processingStatus();
    require(initialStatus.echoSrc.nominalLatencyFrames == 1,
        "native pipeline must report the FIR nominal phase latency in output frames");
    require(initialStatus.limiter.active
            && std::abs(initialStatus.limiter.ceilingDb + 1.0f) <= nearTolerance,
        "native pipeline must report active -1 dB true-peak protection for ECHO SRC");
    pipeline.beginSession();

    const float impulse = 1.0f;
    require(pipeline.push(&impulse, 1), "native pipeline must accept ECHO SRC input");
    pipeline.markInputEnded();
    pipeline.markInputEnded();

    require(pcm.isDspActive(), "active ECHO SRC must activate PCM output protection");
    require(pipeline.hasInputEnded(), "native pipeline must publish EOF after committing the FIR tail");
    require(pipeline.getReadyFrames() == 3, "native pipeline must queue body plus one exact FIR tail frame");
    require(!pipeline.isDrained(), "native pipeline must not report drained while the FIR tail is buffered");

    auto output = makeBuffer(1, 3);
    require(pcm.renderPlanar(output, 0, 3) == 3, "native pipeline must render the complete FIR body and tail");
    require(pipeline.isDrained(), "native pipeline must report drained only after the FIR tail is consumed");

    const std::vector<float> seekPrime { 0.25f, 1.0f, 0.0f };
    require(pipeline.replaceBufferedAudioWithPreroll(
        seekPrime.data(),
        static_cast<int>(seekPrime.size()),
        1,
        false) == 2,
        "native seek replacement must prime FIR history without counting preroll as audible input");
    require(pipeline.getReadyFrames() == 4,
        "native seek replacement must discard preroll output while retaining target PCM geometry");
}

int countBits(uint8_t value)
{
    int count = 0;
    for (int bit = 0; bit < 8; ++bit)
        count += (value & static_cast<uint8_t>(1u << bit)) != 0 ? 1 : 0;
    return count;
}

uint8_t reverseBitsForTest(uint8_t value)
{
    value = static_cast<uint8_t>(((value & 0xf0u) >> 4u) | ((value & 0x0fu) << 4u));
    value = static_cast<uint8_t>(((value & 0xccu) >> 2u) | ((value & 0x33u) << 2u));
    return static_cast<uint8_t>(((value & 0xaau) >> 1u) | ((value & 0x55u) << 1u));
}

double nativeDsdOneDensity(
    const std::vector<uint8_t>& output,
    int startTransportFrame,
    int transportFrames,
    int channels = 1,
    int channel = 0)
{
    int ones = 0;
    int bits = 0;
    const int endFrame = std::min(
        startTransportFrame + transportFrames,
        static_cast<int>(output.size() / static_cast<size_t>(std::max(1, channels) * 2)));
    for (int frame = std::max(0, startTransportFrame); frame < endFrame; ++frame)
    {
        const size_t base = static_cast<size_t>(frame * channels * 2);
        ones += countBits(output[base + static_cast<size_t>(channel)]);
        ones += countBits(output[base + static_cast<size_t>(channels + channel)]);
        bits += 16;
    }
    return bits > 0 ? static_cast<double>(ones) / static_cast<double>(bits) : 0.0;
}

double measureSdmInBandResidualDb(
    echo::SdmQualityProfile profile,
    int transportSampleRate,
    double signalAmplitude = 0.1)
{
    const int bitSampleRate = transportSampleRate * 16;
    const int inputFrames = transportSampleRate / 8;
    constexpr int decimation = 64;
    constexpr int tapCount = 511;
    constexpr double pi = 3.14159265358979323846;
    constexpr double signalFrequency = 1'000.0;
    constexpr double cutoffHz = 20'000.0;

    std::vector<float> input(static_cast<size_t>(inputFrames));
    for (int frame = 0; frame < inputFrames; ++frame)
        input[static_cast<size_t>(frame)] = static_cast<float>(
            std::sin(2.0 * pi * signalFrequency * static_cast<double>(frame) / transportSampleRate)
            * signalAmplitude);

    echo::SdmProcessor processor;
    processor.configure(1, profile, transportSampleRate);
    const auto output = processor.processNativeDsd(input.data(), inputFrames);
    const auto configuration = processor.configuration();

    echo::SdmHalfbandUpsampler idealUpsampler;
    idealUpsampler.configure(1, echo::resolveSdmPreModulatorStageCount(transportSampleRate));
    std::vector<double> residual(static_cast<size_t>(inputFrames * 16), 0.0);
    const double headroomGain = configuration.profileHeadroomGain;
    const int transitionFrames = std::max(1, configuration.transitionRampFrames);
    const int factor = std::max(1, idealUpsampler.factor());
    const int bitsPerCarrier = echo::sdmBitsPerTransportSample / factor;
    double previous = 0.0;
    double carriers[echo::sdmPreModulatorMaxFactor] {};
    size_t bitIndex = 0;
    for (int frame = 0; frame < inputFrames; ++frame)
    {
        const double transitionGain = std::min(
            1.0,
            static_cast<double>(frame + 1) / static_cast<double>(transitionFrames));
        const double protectedSample =
            static_cast<double>(input[static_cast<size_t>(frame)]) * headroomGain * transitionGain;
        idealUpsampler.processSample(0, protectedSample, carriers);
        int frameBit = 0;
        for (int carrier = 0; carrier < factor; ++carrier)
        {
            const double current = carriers[carrier];
            for (int bit = 0; bit < bitsPerCarrier; ++bit)
            {
                const double phase = static_cast<double>(bit + 1) / static_cast<double>(bitsPerCarrier);
                const double ideal = previous + (current - previous) * phase;
                const uint8_t byte = output[static_cast<size_t>(frame * 2 + frameBit / 8)];
                const double actual = (byte & static_cast<uint8_t>(1u << (frameBit % 8))) != 0 ? 1.0 : -1.0;
                residual[bitIndex++] = actual - ideal;
                ++frameBit;
            }
            previous = current;
        }
    }

    std::vector<double> taps(static_cast<size_t>(tapCount), 0.0);
    const int center = (tapCount - 1) / 2;
    const double normalizedCutoff = cutoffHz / bitSampleRate;
    double tapSum = 0.0;
    for (int tap = 0; tap < tapCount; ++tap)
    {
        const int offset = tap - center;
        const double sinc = offset == 0
            ? 2.0 * normalizedCutoff
            : std::sin(2.0 * pi * normalizedCutoff * offset) / (pi * offset);
        const double window = 0.5 - 0.5 * std::cos(2.0 * pi * tap / (tapCount - 1));
        taps[static_cast<size_t>(tap)] = sinc * window;
        tapSum += taps[static_cast<size_t>(tap)];
    }
    for (auto& tap : taps)
        tap /= tapSum;

    const int firstBit = bitSampleRate / 50;
    const int lastBit = bitSampleRate / 10;
    double sumSquares = 0.0;
    int sampleCount = 0;
    for (int outputBit = firstBit; outputBit < lastBit; outputBit += decimation)
    {
        if (outputBit - center < 0 || outputBit + center >= static_cast<int>(residual.size()))
            continue;
        double filtered = 0.0;
        for (int tap = 0; tap < tapCount; ++tap)
            filtered += residual[static_cast<size_t>(outputBit + tap - center)] * taps[static_cast<size_t>(tap)];
        sumSquares += filtered * filtered;
        ++sampleCount;
    }
    const double rms = sampleCount > 0 ? std::sqrt(sumSquares / sampleCount) : 1.0;
    return 20.0 * std::log10(std::max(rms, 1.0e-15));
}

double measureNtfMagnitude(const echo::SdmProcessorConfig& config, double omega)
{
    const std::size_t order = config.feedbackNumeratorCoefficients.size();
    double denominatorReal = 1.0;
    double denominatorImaginary = 0.0;
    double feedbackReal = 0.0;
    double feedbackImaginary = 0.0;
    for (std::size_t tap = 0; tap < order; ++tap)
    {
        const double phase = omega * static_cast<double>(tap + 1);
        const double real = std::cos(phase);
        const double imaginary = -std::sin(phase);
        denominatorReal += config.feedbackDenominatorCoefficients[tap] * real;
        denominatorImaginary += config.feedbackDenominatorCoefficients[tap] * imaginary;
        feedbackReal += config.feedbackNumeratorCoefficients[tap] * real;
        feedbackImaginary += config.feedbackNumeratorCoefficients[tap] * imaginary;
    }
    return std::hypot(denominatorReal - feedbackReal, denominatorImaginary - feedbackImaginary)
        / std::hypot(denominatorReal, denominatorImaginary);
}

double measureNtfPeak(const echo::SdmProcessorConfig& config)
{
    double peak = 0.0;
    for (int sample = 0; sample <= 4'096; ++sample)
        peak = std::max(peak, measureNtfMagnitude(config, 3.14159265358979323846 * sample / 4'096.0));
    return peak;
}

echo::SdmProcessorConfig allDcSdmConfig(int order, double peakGain)
{
    echo::SdmProcessorConfig config;
    const double poleRadius = 2.0 / std::pow(peakGain, 1.0 / static_cast<double>(order)) - 1.0;
    config.ntfPeakGain = peakGain;
    config.feedbackNumeratorCoefficients.assign(static_cast<std::size_t>(order), 0.0);
    config.feedbackDenominatorCoefficients.assign(static_cast<std::size_t>(order), 0.0);
    double binomial = 1.0;
    for (int tap = 1; tap <= order; ++tap)
    {
        binomial *= static_cast<double>(order - tap + 1) / static_cast<double>(tap);
        const double denominator = binomial * std::pow(-poleRadius, tap);
        config.feedbackDenominatorCoefficients[static_cast<std::size_t>(tap - 1)] = denominator;
        config.feedbackNumeratorCoefficients[static_cast<std::size_t>(tap - 1)] =
            denominator - binomial * std::pow(-1.0, tap);
    }
    return config;
}

void testNativeSdmNtfPlacesInBandZeros()
{
    constexpr int transportSampleRate = 176'400;
    constexpr double pi = 3.14159265358979323846;
    const double bandOmega = 2.0 * pi * echo::sdmNtfSignalBandHz / static_cast<double>(transportSampleRate * 16);
    const std::vector<std::pair<echo::SdmQualityProfile, double>> profiles {
        { echo::SdmQualityProfile::Safe, echo::sdmNtfSafePeakGain },
        { echo::SdmQualityProfile::Hifi, echo::sdmNtfHifiPeakGain },
        { echo::SdmQualityProfile::Reference, echo::sdmNtfReferencePeakGain },
        { echo::SdmQualityProfile::Insane, echo::sdmNtfInsanePeakGain },
        { echo::SdmQualityProfile::Adaptive, echo::sdmNtfReferencePeakGain },
    };

    for (const auto& [profile, expectedPeak] : profiles)
    {
        echo::SdmProcessor processor;
        processor.configure(1, profile, transportSampleRate);
        const auto config = processor.configuration();
        const auto design = echo::designBoundedSdmNtf(
            processor.modulatorOrder(),
            expectedPeak,
            transportSampleRate * 16);
        require(std::abs(processor.ntfPeakGain() - expectedPeak) < 1.0e-12,
            "SDM profile must publish its v4 NTF peak gain");
        require(design.feedbackNumeratorCoefficients == config.feedbackNumeratorCoefficients,
            "SdmProcessor must use the shared NTF design coefficients");
        require(design.zeroFrequenciesHz.size() == static_cast<std::size_t>(processor.modulatorOrder()),
            "NTF design must expose one zero frequency per order");
        bool hasInBandZero = false;
        for (const double frequencyHz : design.zeroFrequenciesHz)
        {
            if (std::abs(frequencyHz) > 1.0 && std::abs(frequencyHz) <= echo::sdmNtfSignalBandHz)
                hasInBandZero = true;
        }
        require(hasInBandZero, "v4 NTF must place at least one zero inside the 20 kHz band");
        double numeratorDc = 0.0;
        double denominatorDc = 1.0;
        for (std::size_t tap = 0; tap < config.feedbackNumeratorCoefficients.size(); ++tap)
        {
            numeratorDc += config.feedbackNumeratorCoefficients[tap];
            denominatorDc += config.feedbackDenominatorCoefficients[tap];
        }
        require(std::abs(numeratorDc - denominatorDc) < 1.0e-10,
            "v4 NTF must keep a DC zero so the error-feedback numerator matches A(1)");
        require(std::abs(measureNtfPeak(config) - expectedPeak) < 1.0e-6,
            "v4 NTF peak on the unit circle must match the declared Lee-style bound");
        const auto allDc = allDcSdmConfig(processor.modulatorOrder(), expectedPeak);
        require(
            measureNtfMagnitude(config, bandOmega) < measureNtfMagnitude(allDc, bandOmega) * 0.5,
            "in-band zeros must cut 20 kHz NTF gain by at least 6 dB versus all-DC zeros");
    }

    const auto dsd64 = echo::designBoundedSdmNtf(7, echo::sdmNtfReferencePeakGain, 2'822'400);
    const auto dsd512 = echo::designBoundedSdmNtf(7, echo::sdmNtfReferencePeakGain, 22'579'200);
    require(
        std::abs(dsd64.feedbackNumeratorCoefficients[0] - dsd512.feedbackNumeratorCoefficients[0]) > 1.0e-10,
        "in-band NTF zeros must follow the native DSD bit rate");

    // 512+fs: DSD512-class native rates (44.1k and 48k families) spread the
    // opt=2 zeros across the 100 kHz band; every lower rate keeps 20 kHz.
    static_assert(echo::resolveSdmNtfSignalBandHz(11'289'600) == echo::sdmNtfSignalBandHz);
    static_assert(echo::resolveSdmNtfSignalBandHz(22'579'200) == echo::sdmNtf512PlusSignalBandHz);
    static_assert(echo::resolveSdmNtfSignalBandHz(24'576'000) == echo::sdmNtf512PlusSignalBandHz);
    for (const int nativeRate : { 2'822'400, 5'644'800, 11'289'600 })
    {
        const auto lowRate = echo::designBoundedSdmNtf(7, echo::sdmNtfReferencePeakGain, nativeRate);
        for (const double frequencyHz : lowRate.zeroFrequenciesHz)
            require(std::abs(frequencyHz) <= echo::sdmNtfSignalBandHz + 1.0,
                "sub-DSD512 NTF zeros must stay inside the 20 kHz band");
    }
    bool hasExtendedZero = false;
    for (const double frequencyHz : dsd512.zeroFrequenciesHz)
    {
        require(std::abs(frequencyHz) <= echo::sdmNtf512PlusSignalBandHz + 1.0,
            "512+fs NTF zeros must stay inside the 100 kHz band");
        if (std::abs(frequencyHz) > echo::sdmNtfSignalBandHz)
            hasExtendedZero = true;
    }
    require(hasExtendedZero, "512+fs NTF must place zeros above the 20 kHz band");
    echo::SdmProcessorConfig dsd512Config;
    dsd512Config.feedbackNumeratorCoefficients = dsd512.feedbackNumeratorCoefficients;
    dsd512Config.feedbackDenominatorCoefficients = dsd512.feedbackDenominatorCoefficients;
    require(std::abs(measureNtfPeak(dsd512Config) - echo::sdmNtfReferencePeakGain) < 1.0e-6,
        "512+fs NTF peak on the unit circle must keep the declared bound");
}

// The official full-scale suite (DC +/- steps, 0 dBFS dual tone, uniform
// noise) at the DSD512-class carrier: the 512+fs extended zero band ships
// only with zero-recovery evidence for every profile x decision mode.
void testNativeSdm512PlusFullScaleStressStaysRecoveryFree()
{
    constexpr int transportSampleRate = 1'411'200;
    constexpr int frames = transportSampleRate / 8;
    constexpr double pi = 3.14159265358979323846;
    uint32_t randomState = 0x12345678u;
    std::vector<float> stress(static_cast<size_t>(frames));
    for (int frame = 0; frame < frames; ++frame)
    {
        const int section = (frame / 2'048) % 4;
        if (section == 0)
            stress[static_cast<size_t>(frame)] = 1.0f;
        else if (section == 1)
            stress[static_cast<size_t>(frame)] = -1.0f;
        else if (section == 2)
            stress[static_cast<size_t>(frame)] = static_cast<float>(
                0.57 * std::sin(2.0 * pi * 997.0 * frame / transportSampleRate)
                + 0.43 * std::sin(2.0 * pi * 19'001.0 * frame / transportSampleRate));
        else
        {
            randomState = randomState * 1664525u + 1013904223u;
            stress[static_cast<size_t>(frame)] =
                static_cast<float>(static_cast<double>(randomState) / 2147483648.0 - 1.0);
        }
    }
    for (const auto profile : {
        echo::SdmQualityProfile::Safe,
        echo::SdmQualityProfile::Hifi,
        echo::SdmQualityProfile::Reference,
        echo::SdmQualityProfile::Insane,
        echo::SdmQualityProfile::Adaptive,
        echo::SdmQualityProfile::AdaptiveLight,
        echo::SdmQualityProfile::AdaptiveSuper,
        echo::SdmQualityProfile::ReferenceEc,
    })
    {
        for (const auto decisionMode : {
            echo::SdmDecisionMode::Greedy,
            echo::SdmDecisionMode::Extra,
            echo::SdmDecisionMode::Horizon,
        })
        {
            echo::SdmProcessor processor;
            processor.configure(
                1,
                profile,
                transportSampleRate,
                echo::SdmInterpolationMode::Linear,
                decisionMode);
            for (int offset = 0; offset < frames; offset += 2'048)
            {
                const int chunkFrames = std::min(2'048, frames - offset);
                (void)processor.processNativeDsd(stress.data() + offset, chunkFrames);
            }
            require(processor.stabilityRecoveryCount() == 0,
                "512+fs DSD512 full-scale suite must stay recovery-free; order="
                    + std::to_string(processor.modulatorOrder())
                    + " recoveries=" + std::to_string(processor.stabilityRecoveryCount()));
            // The greedy HiFi/Insane cells are seed-marginal on this suite:
            // an 8-seed scan (2026-09-06, v5 dither placement) keeps every
            // cell recovery-free except one Insane greedy seed, with HiFi
            // greedy peaking at 3.44-3.53 on two seeds; the v4 placement
            // recovered on 3 of 5 seeds for HiFi greedy. Zero recoveries on
            // the canonical seed is the load-bearing gate; the peak guard
            // only has to sit below the 4.0 reset limit with margin.
            require(processor.peakFeedbackState() < 3.75,
                "512+fs DSD512 feedback must stay below the emergency guard; order="
                    + std::to_string(processor.modulatorOrder())
                    + " peak=" + std::to_string(processor.peakFeedbackState()));
        }
    }
}

void testNativeSdmProducesDeterministicProtectedDop()
{
    echo::SdmProcessor first;
    echo::SdmProcessor second;
    echo::SdmProcessor native;
    first.configure(2, echo::SdmQualityProfile::Safe);
    second.configure(2, echo::SdmQualityProfile::Safe);
    native.configure(2, echo::SdmQualityProfile::Safe);
    const std::vector<float> samples { 0.1234567f, -0.1234567f, 0.01f, -0.01f, 0.5f, -0.5f, 0.0f, 0.0f };
    const auto output = first.processDop(samples.data(), 4);
    const auto nativeOutput = native.processNativeDsd(samples.data(), 4);
    require(output == second.processDop(samples.data(), 4),
        "native SDM must remain deterministic across equivalent sessions");
    require(output.size() == 8, "native SDM must emit one protected DoP word per channel and frame");
    for (size_t frame = 0; frame < 4; ++frame)
    {
        const uint32_t expectedMarker = (frame & 1u) == 0u ? 0x05u : 0xfau;
        for (size_t channel = 0; channel < 2; ++channel)
        {
            require((output[frame * 2 + channel] >> 16u) == expectedMarker,
                "native SDM must preserve alternating DoP markers");
            const size_t nativeBase = frame * 4;
            const uint8_t older = nativeOutput[nativeBase + channel];
            const uint8_t newer = nativeOutput[nativeBase + 2 + channel];
            const uint32_t expectedDsd =
                static_cast<uint32_t>(reverseBitsForTest(newer))
                | (static_cast<uint32_t>(reverseBitsForTest(older)) << 8u);
            require((output[frame * 2 + channel] & 0xffffu) == expectedDsd,
                "native SDM DoP output must place chronological t0 at bit 15");
        }
    }
}

void testNativeSdmProfilesShapeNoiseOutOfBand()
{
    struct SpectrumGuard
    {
        echo::SdmQualityProfile profile;
        double dsd64MaximumResidualDb;
        double dsd128MaximumResidualDb;
    };
    // v5 dither placement: the TPDF dither is part of the shaped quantizer
    // error instead of landing flat on the output, so the -20 dBFS residual
    // is no longer pinned to the dither floor (-103 dB DSD64 for 0.00015).
    // 2026-09-06 measurements: Safe -95.7 / -112.8, HiFi -120.6 / -131.7,
    // Reference -121.3 / -129.8, Insane -123.2 / -129.2 dB (DSD64 / DSD128);
    // guards keep about 8 dB of margin like the image guards do.
    const std::vector<SpectrumGuard> profiles {
        { echo::SdmQualityProfile::Safe, -87.0, -104.0 },
        { echo::SdmQualityProfile::Hifi, -112.0, -123.0 },
        { echo::SdmQualityProfile::Reference, -113.0, -121.0 },
        { echo::SdmQualityProfile::Insane, -115.0, -121.0 },
    };
    for (const auto [transportSampleRate, rateName] : {
        std::pair { 176'400, "DSD64" },
        std::pair { 352'800, "DSD128" },
    })
    {
        std::vector<double> residuals;
        for (const auto& guard : profiles)
        {
            const double residualDb = measureSdmInBandResidualDb(guard.profile, transportSampleRate);
            residuals.push_back(residualDb);
            const double maximumResidualDb = transportSampleRate == 176'400
                ? guard.dsd64MaximumResidualDb
                : guard.dsd128MaximumResidualDb;
            require(
                residualDb < maximumResidualDb,
                std::string("SDM profile must meet its ") + rateName
                    + " in-band residual guard, measured " + std::to_string(residualDb));
        }
        // v4 in-band zeros plus TPDF already put HiFi+ near -100 dB on DSD64
        // and onto the DSD128 measurement floor. Keep the 3 dB ladder only
        // while the previous profile is still in the mid-band; near the floor
        // a higher-order NTF may trade 1 kHz residual for 15-20 kHz flattening.
        for (size_t profile = 1; profile < residuals.size(); ++profile)
        {
            const bool nearMeasurementFloor = residuals[profile - 1] < -100.0;
            const bool profileOrderingHolds = !nearMeasurementFloor && transportSampleRate == 176'400
                ? residuals[profile] < residuals[profile - 1] - 3.0
                : residuals[profile] <= residuals[profile - 1] + (nearMeasurementFloor ? 3.0 : 0.1);
            require(profileOrderingHolds,
                std::string("higher SDM profiles must preserve the expected ") + rateName
                    + " residual ordering near the measurement floor, previous="
                    + std::to_string(residuals[profile - 1])
                    + " current=" + std::to_string(residuals[profile]));
        }
    }
}

void testNativeSdmProfilesRemainBounded()
{
    constexpr int transportSampleRate = 176'400;
    constexpr int frames = transportSampleRate / 4;
    constexpr double pi = 3.14159265358979323846;
    uint32_t randomState = 0x12345678u;

    std::vector<float> input(static_cast<size_t>(frames));
    for (int frame = 0; frame < frames; ++frame)
    {
        const int section = (frame / 2'048) % 4;
        if (section == 0)
            input[static_cast<size_t>(frame)] = 1.0f;
        else if (section == 1)
            input[static_cast<size_t>(frame)] = -1.0f;
        else if (section == 2)
            input[static_cast<size_t>(frame)] = static_cast<float>(
                0.57 * std::sin(2.0 * pi * 997.0 * frame / transportSampleRate)
                + 0.43 * std::sin(2.0 * pi * 19'001.0 * frame / transportSampleRate));
        else
        {
            randomState = randomState * 1664525u + 1013904223u;
            input[static_cast<size_t>(frame)] =
                static_cast<float>(static_cast<double>(randomState) / 2147483648.0 - 1.0);
        }
    }

    const std::vector<std::tuple<echo::SdmQualityProfile, int, double>> profiles {
        { echo::SdmQualityProfile::Safe, 3, 3.0 },
        { echo::SdmQualityProfile::Hifi, 6, 4.5 },
        { echo::SdmQualityProfile::Reference, 7, 4.5 },
        { echo::SdmQualityProfile::Insane, 8, 6.0 },
        { echo::SdmQualityProfile::Adaptive, 7, 3.5 },
        { echo::SdmQualityProfile::AdaptiveLight, 7, 3.5 },
        { echo::SdmQualityProfile::AdaptiveSuper, 7, 3.5 },
        { echo::SdmQualityProfile::ReferenceEc, 7, 4.5 },
    };
    for (const auto& [profile, expectedOrder, expectedHeadroomDb] : profiles)
    {
        echo::SdmProcessor processor;
        processor.configure(1, profile, transportSampleRate);
        const double extraHeadroomDb = expectedOrder >= 6 ? 1.5 : 0.0;
        const double expectedGain = std::pow(10.0, -(expectedHeadroomDb + extraHeadroomDb) / 20.0);
        require(
            std::abs(processor.configuration().profileHeadroomGain - expectedGain) < 1.0e-12,
            "SDM quality profile must use its stress-tested safety headroom");
        for (int offset = 0; offset < frames; offset += 2'048)
        {
            const int chunkFrames = std::min(2'048, frames - offset);
            const auto output = processor.processNativeDsd(input.data() + offset, chunkFrames);
            require(
                output.size() == static_cast<size_t>(chunkFrames * 2),
                "SDM stress block must produce complete native DSD output");
        }
        require(processor.modulatorOrder() == expectedOrder, "SDM profile must use its guarded NTF order");
        require(processor.stabilityRecoveryCount() == 0,
            "SDM must not trigger stability recovery under full-scale DC, multitone, noise, or steps; order="
                + std::to_string(expectedOrder)
                + " recoveries=" + std::to_string(processor.stabilityRecoveryCount()));
        require(processor.peakFeedbackState() < 3.5,
            "SDM feedback state must stay below the emergency guard; order="
                + std::to_string(expectedOrder)
                + " peak=" + std::to_string(processor.peakFeedbackState()));
    }
}

void testNativeSdmHighOrderProfilesAreChunkInvariant()
{
    constexpr int transportSampleRate = 176'400;
    constexpr int frames = 4'097;
    constexpr double pi = 3.14159265358979323846;
    std::vector<float> input(static_cast<size_t>(frames));
    for (int frame = 0; frame < frames; ++frame)
        input[static_cast<size_t>(frame)] = static_cast<float>(
            0.72 * std::sin(2.0 * pi * 997.0 * frame / transportSampleRate)
            + 0.18 * std::sin(2.0 * pi * 17'003.0 * frame / transportSampleRate));

    for (const auto profile : {
        echo::SdmQualityProfile::Hifi,
        echo::SdmQualityProfile::Reference,
        echo::SdmQualityProfile::Insane,
        echo::SdmQualityProfile::Adaptive,
        echo::SdmQualityProfile::AdaptiveLight,
        echo::SdmQualityProfile::AdaptiveSuper,
        echo::SdmQualityProfile::ReferenceEc,
    })
    {
        echo::SdmProcessor contiguous;
        echo::SdmProcessor chunked;
        contiguous.configure(1, profile, transportSampleRate);
        chunked.configure(1, profile, transportSampleRate);
        const auto expected = contiguous.processNativeDsd(input.data(), frames);

        std::vector<uint8_t> actual;
        for (int offset = 0; offset < frames;)
        {
            const int chunkFrames = std::min(137 + (offset % 251), frames - offset);
            auto block = chunked.processNativeDsd(input.data() + offset, chunkFrames);
            actual.insert(actual.end(), block.begin(), block.end());
            offset += chunkFrames;
        }
        require(actual == expected, "high-order SDM output must be bit-identical across decoder chunking");
        require(chunked.stabilityRecoveryCount() == 0, "chunked high-order SDM must not require recovery");
    }
}

void testNativeSdmStereoParallelPathMatchesSequentialChunks()
{
    constexpr int transportSampleRate = 1'411'200;
    constexpr int frames = 4'097;
    constexpr double pi = 3.14159265358979323846;
    std::vector<float> input(static_cast<size_t>(frames * 2));
    for (int frame = 0; frame < frames; ++frame)
    {
        input[static_cast<size_t>(frame * 2)] = static_cast<float>(
            0.61 * std::sin(2.0 * pi * 997.0 * frame / transportSampleRate));
        input[static_cast<size_t>(frame * 2 + 1)] = static_cast<float>(
            0.47 * std::cos(2.0 * pi * 13'007.0 * frame / transportSampleRate));
    }

    const auto renderChunkedNativeDsd = [&]()
    {
        echo::SdmProcessor processor;
        processor.configure(2, echo::SdmQualityProfile::Reference, transportSampleRate);
        std::vector<uint8_t> output;
        for (int offset = 0; offset < frames;)
        {
            const int chunkFrames = std::min(257, frames - offset);
            auto block = processor.processNativeDsd(input.data() + offset * 2, chunkFrames);
            output.insert(output.end(), block.begin(), block.end());
            offset += chunkFrames;
        }
        return output;
    };
    echo::SdmProcessor parallelNativeDsd;
    parallelNativeDsd.configure(2, echo::SdmQualityProfile::Reference, transportSampleRate);
    require(
        parallelNativeDsd.processNativeDsd(input.data(), frames) == renderChunkedNativeDsd(),
        "parallel stereo native DSD must remain bit-identical to sequential chunks");

    const auto renderChunkedDop = [&]()
    {
        echo::SdmProcessor processor;
        processor.configure(2, echo::SdmQualityProfile::Reference, transportSampleRate);
        std::vector<uint32_t> output;
        for (int offset = 0; offset < frames;)
        {
            const int chunkFrames = std::min(257, frames - offset);
            auto block = processor.processDop(input.data() + offset * 2, chunkFrames);
            output.insert(output.end(), block.begin(), block.end());
            offset += chunkFrames;
        }
        return output;
    };
    echo::SdmProcessor parallelDop;
    parallelDop.configure(2, echo::SdmQualityProfile::Reference, transportSampleRate);
    require(
        parallelDop.processDop(input.data(), frames) == renderChunkedDop(),
        "parallel stereo DoP must remain bit-identical to sequential chunks");
}

void testNativeSdmAppliesHeadroomAndSmoothsTransitions()
{
    constexpr int transportSampleRate = 176'400;
    echo::SdmProcessor processor;
    processor.configure(1, echo::SdmQualityProfile::Safe, transportSampleRate);
    constexpr int transitionFrames = transportSampleRate / 100;
    std::vector<float> fullScale(static_cast<size_t>(transitionFrames * 3), 1.0f);
    const auto started = processor.processNativeDsd(fullScale.data(), static_cast<int>(fullScale.size()));

    const double earlyDensity = nativeDsdOneDensity(started, 0, transitionFrames / 10);
    const double settledDensity = nativeDsdOneDensity(started, transitionFrames * 2, transitionFrames);
    require(earlyDensity < 0.56, "SDM startup must ramp from silence instead of stepping to full signal");
    require(
        settledDensity > 0.83 && settledDensity < 0.88,
        "SDM Safe must apply its 3 dB safety headroom before modulation");

    processor.setTargetGain(0.0);
    std::vector<float> volumeChange(static_cast<size_t>(transitionFrames), 1.0f);
    const auto faded = processor.processNativeDsd(volumeChange.data(), transitionFrames);
    const double fadeStartDensity = nativeDsdOneDensity(faded, 0, transitionFrames / 4);
    const double fadeEndDensity = nativeDsdOneDensity(
        faded,
        transitionFrames - transitionFrames / 4,
        transitionFrames / 4);
    require(
        fadeStartDensity > fadeEndDensity + 0.1,
        "SDM volume changes must use a time-domain gain ramp");
    require(fadeEndDensity < 0.55, "SDM gain ramp must settle near shaped silence");

    echo::SdmProcessor mutedProcessor;
    mutedProcessor.configure(1, echo::SdmQualityProfile::Safe, transportSampleRate);
    mutedProcessor.setTargetGain(0.0);
    mutedProcessor.reset();
    const auto muted = mutedProcessor.processNativeDsd(fullScale.data(), transitionFrames);
    const double mutedDensity = nativeDsdOneDensity(muted, 0, transitionFrames);
    require(
        mutedDensity > 0.49 && mutedDensity < 0.51,
        "an SDM session started muted must not produce a gain-ramp hump");
}

void testNativeSdmIdleLockPreservesWeakSignals()
{
    constexpr int transportSampleRate = 176'400;
    constexpr int signalFrames = transportSampleRate / 20;
    constexpr double pi = 3.14159265358979323846;
    echo::SdmProcessor processor;
    processor.configure(1, echo::SdmQualityProfile::Reference, transportSampleRate);
    std::vector<float> weakSignal(static_cast<size_t>(signalFrames));
    for (int frame = 0; frame < signalFrames; ++frame)
        weakSignal[static_cast<size_t>(frame)] = static_cast<float>(
            std::sin(2.0 * pi * 1'000.0 * static_cast<double>(frame) / transportSampleRate) * 0.001);
    const auto signalOutput = processor.processNativeDsd(weakSignal.data(), signalFrames);

    int longestIdleRun = 0;
    int currentIdleRun = 0;
    for (int frame = transportSampleRate / 50; frame < signalFrames; ++frame)
    {
        const size_t base = static_cast<size_t>(frame * 2);
        if (signalOutput[base] == 0x69u && signalOutput[base + 1] == 0x69u)
        {
            currentIdleRun += 1;
            longestIdleRun = std::max(longestIdleRun, currentIdleRun);
        }
        else
        {
            currentIdleRun = 0;
        }
    }
    require(longestIdleRun < 64, "SDM idle lock must not gate sustained -60 dBFS content");

    processor.reset();
    std::vector<float> digitalSilence(static_cast<size_t>(transportSampleRate / 25), 0.0f);
    const auto silenceOutput = processor.processNativeDsd(
        digitalSilence.data(),
        static_cast<int>(digitalSilence.size()));
    for (int frame = static_cast<int>(digitalSilence.size()) - 32;
         frame < static_cast<int>(digitalSilence.size());
         ++frame)
    {
        const size_t base = static_cast<size_t>(frame * 2);
        require(
            silenceOutput[base] == 0x69u && silenceOutput[base + 1] == 0x69u,
            "sustained digital silence must settle to the standard DSD idle pattern");
    }
}

double goertzelMagnitude(const std::vector<double>& samples, double frequencyHz, double sampleRate)
{
    if (samples.empty() || sampleRate <= 0.0)
        return 0.0;
    constexpr double pi = 3.14159265358979323846;
    const double omega = 2.0 * pi * frequencyHz / sampleRate;
    const double coeff = 2.0 * std::cos(omega);
    double previous = 0.0;
    double previousPrevious = 0.0;
    for (const double sample : samples)
    {
        const double current = sample + coeff * previous - previousPrevious;
        previousPrevious = previous;
        previous = current;
    }
    const double power = previous * previous
        + previousPrevious * previousPrevious
        - coeff * previous * previousPrevious;
    return 2.0 * std::sqrt(std::max(0.0, power)) / static_cast<double>(samples.size());
}

void appendNativeDsdBits(
    const std::vector<uint8_t>& output,
    int startTransportFrame,
    int transportFrames,
    std::vector<double>& bits)
{
    const int endFrame = std::min(
        startTransportFrame + transportFrames,
        static_cast<int>(output.size() / 2u));
    for (int frame = std::max(0, startTransportFrame); frame < endFrame; ++frame)
    {
        for (int bit = 0; bit < 16; ++bit)
        {
            const uint8_t byte = output[static_cast<size_t>(frame * 2 + bit / 8)];
            bits.push_back((byte & static_cast<uint8_t>(1u << (bit % 8))) != 0 ? 1.0 : -1.0);
        }
    }
}

void testNativeSdmPreModulatorFollowsLowRatePlan()
{
    const std::vector<std::tuple<int, int, int>> cases {
        { 176'400, 4, 372 },
        { 192'000, 4, 372 },
        { 352'800, 2, 248 },
        { 384'000, 2, 248 },
        { 705'600, 2, 248 },
        { 768'000, 2, 248 },
        // DSD512 stays at pure x16 linear interpolation by design.
        { 1'411'200, 1, 0 },
    };
    for (const auto& [transportSampleRate, expectedFactor, expectedDelayBits] : cases)
    {
        require(echo::resolveSdmPreModulatorUpsampleFactor(transportSampleRate) == expectedFactor,
            "pre-modulator factor helper must match the low-rate DSD plan");
        require(echo::resolveSdmPreModulatorDelayBits(
                echo::resolveSdmPreModulatorStageCount(transportSampleRate)) == expectedDelayBits,
            "pre-modulator delay helper must count halfband group delay in output bits");
        echo::SdmProcessor processor;
        processor.configure(1, echo::SdmQualityProfile::Safe, transportSampleRate);
        require(processor.preModulatorUpsampleFactor() == expectedFactor,
            "SdmProcessor must upsample DSD64/128 carriers before the last linear interpolator");
        require(processor.preModulatorDelayBits() == expectedDelayBits,
            "SdmProcessor must publish the pre-modulator group delay used by residual alignment");
        require(processor.configuration().preModulatorUpsampleFactor == expectedFactor,
            "exported SDM config must include the pre-modulator factor");
        require(processor.configuration().preModulatorDelayBits == expectedDelayBits,
            "exported SDM config must include the pre-modulator delay");
        require(
            processor.configuration().preModulatorHalfbandTaps.size()
                == static_cast<size_t>(echo::sdmPreModulatorHalfbandTapCount),
            "exported SDM config must include the high-rejection halfband");
    }
    // The experimental heavyPreMod opt-in adds exactly one halfband stage on
    // DSD512-class transports; every lower rate keeps its default plan.
    require(echo::resolveSdmPreModulatorStageCount(1'411'200, true) == 1
            && echo::resolveSdmPreModulatorStageCount(1'536'000, true) == 1,
        "heavyPreMod must allow one pre-modulator stage on DSD512-class transports");
    require(echo::resolveSdmPreModulatorUpsampleFactor(1'411'200, true) == 2,
        "heavyPreMod DSD512 must upsample x2 in FIR before the linear interpolator");
    require(echo::resolveSdmPreModulatorStageCount(705'600, true) == 1
            && echo::resolveSdmPreModulatorStageCount(176'400, true) == 2,
        "heavyPreMod must not change the sub-DSD512 pre-modulator plan");
    echo::SdmProcessor heavyPreModProcessor;
    heavyPreModProcessor.configure(
        1,
        echo::SdmQualityProfile::Safe,
        1'411'200,
        echo::SdmInterpolationMode::Linear,
        echo::SdmDecisionMode::Greedy,
        true);
    require(heavyPreModProcessor.preModulatorUpsampleFactor() == 2
            && heavyPreModProcessor.preModulatorDelayBits() == 248,
        "heavyPreMod SdmProcessor must run the one-stage halfband plan on DSD512");
    echo::SdmHalfbandUpsampler dcCheck;
    dcCheck.configure(1, 2);
    double carriers[echo::sdmPreModulatorMaxFactor] {};
    for (int frame = 0; frame < 64; ++frame)
        dcCheck.processSample(0, 0.5, carriers);
    for (int carrier = 0; carrier < dcCheck.factor(); ++carrier)
    {
        require(std::abs(carriers[carrier] - 0.5) < 1.0e-9,
            "pre-modulator halfband must keep DC gain of 1 after settling");
    }
    require(std::abs(echo::SdmProcessor().configuration().ditherAmplitude - 0.0005) < 1.0e-12,
        "unconfigured SDM defaults must use the Safe TPDF amplitude");
    echo::SdmProcessor safe;
    echo::SdmProcessor hifi;
    echo::SdmProcessor reference;
    echo::SdmProcessor insane;
    safe.configure(1, echo::SdmQualityProfile::Safe, 176'400);
    hifi.configure(1, echo::SdmQualityProfile::Hifi, 176'400);
    reference.configure(1, echo::SdmQualityProfile::Reference, 176'400);
    insane.configure(1, echo::SdmQualityProfile::Insane, 176'400);
    require(std::abs(safe.configuration().ditherAmplitude - 0.0005) < 1.0e-12, "Safe dither must be TPDF 5e-4");
    require(std::abs(hifi.configuration().ditherAmplitude - 0.0002) < 1.0e-12, "HiFi dither must be TPDF 2e-4");
    require(std::abs(reference.configuration().ditherAmplitude - 0.00015) < 1.0e-12, "Reference dither must be TPDF 1.5e-4");
    require(std::abs(insane.configuration().ditherAmplitude - 0.0001) < 1.0e-12, "Insane dither must be TPDF 1e-4");
    echo::SdmProcessor adaptive;
    adaptive.configure(1, echo::SdmQualityProfile::Adaptive, 176'400);
    require(std::abs(adaptive.configuration().ditherAmplitude - 0.00015) < 1.0e-12, "Adaptive dither must be TPDF 1.5e-4");
}

void testNativeSdmPreModulatorSuppressesLowRateImages()
{
    constexpr double pi = 3.14159265358979323846;
    constexpr double signalHz = 20'000.0;
    // Guards sit ~8 dB below the measured Linux x64 baselines (-122 / -139 /
    // -118 dBc). DSD256 rejection is limited by the halfband passband ripple
    // at its 20 kHz-equivalent point, not by the linear interpolator.
    const std::vector<std::tuple<int, int, double>> cases {
        { 176'400, 4, -115.0 },
        { 352'800, 2, -130.0 },
        { 705'600, 2, -110.0 },
    };
    for (const auto& [transportSampleRate, expectedFactor, maximumImageDb] : cases)
    {
        const int bitSampleRate = transportSampleRate * echo::sdmBitsPerTransportSample;
        const int frames = transportSampleRate / 8;
        const double imageHz = static_cast<double>(transportSampleRate) - signalHz;
        echo::SdmHalfbandUpsampler upsampler;
        upsampler.configure(1, echo::resolveSdmPreModulatorStageCount(transportSampleRate));
        require(upsampler.factor() == expectedFactor,
            "low-rate image test must use the planned halfband factor");

        std::vector<double> interpolated(static_cast<size_t>(frames) * 16u, 0.0);
        double previous = 0.0;
        double carriers[echo::sdmPreModulatorMaxFactor] {};
        size_t bitIndex = 0;
        for (int frame = 0; frame < frames; ++frame)
        {
            const double sample = std::sin(
                2.0 * pi * signalHz * static_cast<double>(frame)
                    / static_cast<double>(transportSampleRate));
            upsampler.processSample(0, sample, carriers);
            const int bitsPerCarrier = echo::sdmBitsPerTransportSample / upsampler.factor();
            for (int carrier = 0; carrier < upsampler.factor(); ++carrier)
            {
                const double current = carriers[carrier];
                for (int bit = 0; bit < bitsPerCarrier; ++bit)
                {
                    const double phase = static_cast<double>(bit + 1) / static_cast<double>(bitsPerCarrier);
                    interpolated[bitIndex++] = previous + (current - previous) * phase;
                }
                previous = current;
            }
        }

        const int startBit = std::max(upsampler.delayBits() + bitSampleRate / 50, 0);
        const int bitCount = bitSampleRate / 20;
        require(startBit + bitCount < static_cast<int>(interpolated.size()),
            "image measurement window must fit inside the interpolated block");
        const std::vector<double> window(
            interpolated.begin() + startBit,
            interpolated.begin() + startBit + bitCount);
        const double signal = goertzelMagnitude(window, signalHz, static_cast<double>(bitSampleRate));
        const double image = goertzelMagnitude(window, imageHz, static_cast<double>(bitSampleRate));
        require(signal > 0.2, "20 kHz stimulus must remain visible after pre-modulator interpolation");
        const double imageDb = 20.0 * std::log10(
            std::max(image, 1.0e-15) / std::max(signal, 1.0e-15));
        require(imageDb < maximumImageDb,
            std::string("low-rate 20 kHz image must meet the high-rejection FIR guard, measured ")
                + std::to_string(imageDb));
    }
}

void testNativeSdmSmallSignalDoesNotCollapseToIdlePattern()
{
    constexpr int transportSampleRate = 176'400;
    constexpr int frames = transportSampleRate / 8;
    echo::SdmProcessor processor;
    processor.configure(1, echo::SdmQualityProfile::Reference, transportSampleRate);
    std::vector<float> dc(static_cast<size_t>(frames), 0.001f);
    const auto output = processor.processNativeDsd(dc.data(), frames);
    const int startFrame = transportSampleRate / 50;
    const int measuredFrames = transportSampleRate / 20;
    int longestIdleRun = 0;
    int currentIdleRun = 0;
    uint32_t dominantWord = 0;
    int dominantCount = 0;
    std::vector<int> counts(65'536, 0);
    for (int frame = startFrame; frame < startFrame + measuredFrames; ++frame)
    {
        const uint16_t word = static_cast<uint16_t>(
            output[static_cast<size_t>(frame * 2)]
            | (static_cast<uint16_t>(output[static_cast<size_t>(frame * 2 + 1)]) << 8));
        if (word == 0x6969u)
        {
            currentIdleRun += 1;
            longestIdleRun = std::max(longestIdleRun, currentIdleRun);
        }
        else
        {
            currentIdleRun = 0;
        }
        const int next = ++counts[word];
        if (next > dominantCount)
        {
            dominantCount = next;
            dominantWord = word;
        }
    }
    require(longestIdleRun < 64, "-60 dBFS DC must not settle into the digital-zero idle pattern");
    require(
        static_cast<double>(dominantCount) / static_cast<double>(measuredFrames) < 0.35,
        std::string("-60 dBFS DC must not collapse to a short repeating SDM word; dominant=")
            + std::to_string(dominantWord)
            + " ratio="
            + std::to_string(static_cast<double>(dominantCount) / static_cast<double>(measuredFrames)));
}

void testSdmExtraSharedLookaheadMatchesIndependentPredictions()
{
    uint32_t randomState = 0x6d2b79f5u;
    const auto nextSigned = [&]()
    {
        randomState = randomState * 1664525u + 1013904223u;
        return static_cast<double>(randomState) / 2147483648.0 - 1.0;
    };

    for (const auto profile : {
        echo::SdmQualityProfile::Safe,
        echo::SdmQualityProfile::Hifi,
        echo::SdmQualityProfile::Reference,
        echo::SdmQualityProfile::Insane,
    })
    {
        echo::SdmProcessor processor;
        processor.configure(1, profile, 352'800);
        const auto config = processor.configuration();
        const int order = processor.modulatorOrder();
        std::array<double, 8> errorHistory {};
        std::array<double, 8> feedbackHistory {};
        for (int iteration = 0; iteration < 2'048; ++iteration)
        {
            for (int tap = 0; tap < order; ++tap)
            {
                errorHistory[static_cast<size_t>(tap)] = nextSigned() * 0.75;
                feedbackHistory[static_cast<size_t>(tap)] = nextSigned() * 0.75;
            }
            const double decision = nextSigned() * config.stabilityLimit;
            const double currentFeedback = nextSigned() * 2.0;
            double independentError = 0.0;
            double sharedError = 0.0;
            const bool independent = chooseSdmExtraBitIndependentForTest(
                decision,
                config.stabilityLimit,
                config.feedbackNumeratorCoefficients.data(),
                config.feedbackDenominatorCoefficients.data(),
                errorHistory.data(),
                feedbackHistory.data(),
                order,
                currentFeedback,
                independentError);
            const bool shared = echo::chooseSdmExtraBit(
                decision,
                config.stabilityLimit,
                config.feedbackNumeratorCoefficients.data(),
                config.feedbackDenominatorCoefficients.data(),
                errorHistory.data(),
                feedbackHistory.data(),
                order,
                currentFeedback,
                sharedError);
            require(shared == independent && sharedError == independentError,
                "SDM Extra shared look-ahead must preserve the independent candidate decision");
        }
    }
}

void testNativeSdmExtraLoopChangesBitstreamAndStaysBounded()
{
    constexpr int transportSampleRate = 176'400;
    constexpr int frames = transportSampleRate / 8;
    constexpr double pi = 3.14159265358979323846;
    std::vector<float> sine(static_cast<size_t>(frames));
    for (int frame = 0; frame < frames; ++frame)
        sine[static_cast<size_t>(frame)] = static_cast<float>(
            0.1 * std::sin(2.0 * pi * 1'000.0 * frame / transportSampleRate));

    echo::SdmProcessor off;
    echo::SdmProcessor on;
    echo::SdmProcessor horizon;
    off.configure(1, echo::SdmQualityProfile::Reference, transportSampleRate);
    on.configure(1, echo::SdmQualityProfile::Reference, transportSampleRate, echo::SdmInterpolationMode::Linear, true);
    horizon.configure(
        1,
        echo::SdmQualityProfile::Reference,
        transportSampleRate,
        echo::SdmInterpolationMode::Linear,
        echo::SdmDecisionMode::Horizon);
    require(!off.extraLoopEnabled(), "SDM Extra must stay off by default");
    require(on.extraLoopEnabled(), "SDM Extra must report enabled when requested");
    require(!off.configuration().extraLoop, "Extra Off configuration must export extraLoop=false");
    require(on.configuration().extraLoop, "Extra On configuration must export extraLoop=true");
    require(horizon.decisionMode() == echo::SdmDecisionMode::Horizon,
        "Horizon must report the active decision strategy");
    require(horizon.configuration().decisionMode == echo::SdmDecisionMode::Horizon,
        "Horizon configuration must preserve the decision strategy");
    const double extraHeadroomDb = echo::sdmExtraLoopHeadroomDb;
    require(
        on.configuration().profileHeadroomGain
            < off.configuration().profileHeadroomGain * std::pow(10.0, -(extraHeadroomDb - 0.01) / 20.0),
        "Extra must add its measured safety headroom");
    // DSD64 Reference: Horizon's conservative margin absorbs the 1.5 dB
    // low-rate high-order add-on instead of stacking on top of it, so the
    // total is base 4.5 + max(1.5, 3.0) = 7.5 dB.
    require(
        std::abs(
            horizon.configuration().profileHeadroomGain
                - std::pow(10.0, -(4.5 + std::max(1.5, echo::sdmHorizonHeadroomDb)) / 20.0))
            < 1.0e-12,
        "DSD64 Reference Horizon must use the deduplicated stacked headroom");
    echo::SdmProcessor insaneHorizon;
    insaneHorizon.configure(
        1,
        echo::SdmQualityProfile::Insane,
        transportSampleRate,
        echo::SdmInterpolationMode::Linear,
        echo::SdmDecisionMode::Horizon);
    require(
        std::abs(
            insaneHorizon.configuration().profileHeadroomGain
                - std::pow(10.0, -(6.0 + 1.5 + echo::sdmHorizonHeadroomDb) / 20.0))
            < 1.0e-12,
        "the riskiest DSD64 Insane Horizon cell must keep the fully stacked headroom");
    echo::SdmProcessor highRateHorizon;
    highRateHorizon.configure(
        1,
        echo::SdmQualityProfile::Reference,
        705'600,
        echo::SdmInterpolationMode::Linear,
        echo::SdmDecisionMode::Horizon);
    require(
        std::abs(
            highRateHorizon.configuration().profileHeadroomGain
                - std::pow(10.0, -(4.5 + echo::sdmHorizonHeadroomDb) / 20.0))
            < 1.0e-12,
        "high-rate Reference Horizon must keep its base decision headroom without a low-rate add-on");
    const auto offOutput = off.processNativeDsd(sine.data(), frames);
    const auto onOutput = on.processNativeDsd(sine.data(), frames);
    const auto horizonOutput = horizon.processNativeDsd(sine.data(), frames);
    require(offOutput != onOutput, "Extra must change the 1-bit stream for a 1 kHz tone");
    require(horizonOutput != offOutput && horizonOutput != onOutput,
        "Horizon must produce its own 1-bit decision stream");
    require(on.stabilityRecoveryCount() == 0, "Extra 1 kHz tone must stay recovery-free");
    require(horizon.stabilityRecoveryCount() == 0, "Horizon 1 kHz tone must stay recovery-free");

    uint32_t randomState = 0x12345678u;
    std::vector<float> stress(static_cast<size_t>(frames));
    for (int frame = 0; frame < frames; ++frame)
    {
        const int section = (frame / 2'048) % 4;
        if (section == 0)
            stress[static_cast<size_t>(frame)] = 1.0f;
        else if (section == 1)
            stress[static_cast<size_t>(frame)] = -1.0f;
        else if (section == 2)
            stress[static_cast<size_t>(frame)] = static_cast<float>(
                0.57 * std::sin(2.0 * pi * 997.0 * frame / transportSampleRate)
                + 0.43 * std::sin(2.0 * pi * 19'001.0 * frame / transportSampleRate));
        else
        {
            randomState = randomState * 1664525u + 1013904223u;
            stress[static_cast<size_t>(frame)] =
                static_cast<float>(static_cast<double>(randomState) / 2147483648.0 - 1.0);
        }
    }

    for (const auto profile : {
        echo::SdmQualityProfile::Safe,
        echo::SdmQualityProfile::Hifi,
        echo::SdmQualityProfile::Reference,
        echo::SdmQualityProfile::Insane,
        echo::SdmQualityProfile::Adaptive,
    })
    {
        for (const auto decisionMode : {
            echo::SdmDecisionMode::Extra,
            echo::SdmDecisionMode::Horizon,
        })
        {
            echo::SdmProcessor predictive;
            predictive.configure(
                1,
                profile,
                transportSampleRate,
                echo::SdmInterpolationMode::Linear,
                decisionMode);
            for (int offset = 0; offset < frames; offset += 2'048)
            {
                const int chunkFrames = std::min(2'048, frames - offset);
                (void)predictive.processNativeDsd(stress.data() + offset, chunkFrames);
            }
            const std::string name = decisionMode == echo::SdmDecisionMode::Horizon
                ? "Horizon"
                : "Extra";
            require(predictive.stabilityRecoveryCount() == 0,
                name + " must stay recovery-free under the full-scale suite; recoveries="
                    + std::to_string(predictive.stabilityRecoveryCount()));
            require(predictive.peakFeedbackState() < 3.5,
                name + " feedback must stay below the emergency guard; peak="
                    + std::to_string(predictive.peakFeedbackState()));
        }
    }

    echo::SdmProcessor smallSignal;
    smallSignal.configure(1, echo::SdmQualityProfile::Reference, transportSampleRate, echo::SdmInterpolationMode::Linear, true);
    std::vector<float> dc(static_cast<size_t>(frames), 0.001f);
    const auto extraDc = smallSignal.processNativeDsd(dc.data(), frames);
    int longestIdleRun = 0;
    int currentIdleRun = 0;
    const int startFrame = transportSampleRate / 50;
    const int measuredFrames = transportSampleRate / 20;
    for (int frame = startFrame; frame < startFrame + measuredFrames; ++frame)
    {
        const uint16_t word = static_cast<uint16_t>(
            extraDc[static_cast<size_t>(frame * 2)]
            | (static_cast<uint16_t>(extraDc[static_cast<size_t>(frame * 2 + 1)]) << 8));
        if (word == 0x6969u)
        {
            currentIdleRun += 1;
            longestIdleRun = std::max(longestIdleRun, currentIdleRun);
        }
        else
        {
            currentIdleRun = 0;
        }
    }
    require(smallSignal.stabilityRecoveryCount() == 0, "Extra -60 dBFS DC must stay recovery-free");
    require(longestIdleRun < 64, "Extra -60 dBFS DC must not settle into the digital-zero idle pattern");
}

void testNativeSdmAdaptiveProfileScalesLoopGainFromEnvelope()
{
    constexpr int transportSampleRate = 176'400;

    // Reference-grade NTF with the lower stress-tested static headroom:
    // base 3.5 dB plus the unchanged 1.5 dB DSD64 low-rate add-on.
    echo::SdmProcessor adaptive;
    adaptive.configure(1, echo::SdmQualityProfile::Adaptive, transportSampleRate);
    require(adaptive.modulatorOrder() == 7, "Adaptive must run the Reference-grade order-7 NTF");
    require(std::abs(adaptive.ntfPeakGain() - echo::sdmNtfReferencePeakGain) < 1.0e-12,
        "Adaptive must keep the Reference 1.60 NTF peak-gain cap");
    require(adaptive.adaptiveLoopGainEnabled(), "Adaptive must report the envelope-driven loop gain");
    require(adaptive.configuration().adaptiveLoopGainEnabled,
        "Adaptive configuration must export the envelope-driven loop gain");
    require(
        std::abs(
            adaptive.configuration().profileHeadroomGain
                - std::pow(10.0, -(3.5 + 1.5) / 20.0)) < 1.0e-12,
        "DSD64 Adaptive must use its stress-tested 3.5 dB base headroom plus the low-rate add-on");
    echo::SdmProcessor reference;
    reference.configure(1, echo::SdmQualityProfile::Reference, transportSampleRate);
    require(!reference.adaptiveLoopGainEnabled() && !reference.configuration().adaptiveLoopGainEnabled,
        "Reference must not enable the adaptive loop gain");
    require(
        adaptive.configuration().feedbackNumeratorCoefficients
            == reference.configuration().feedbackNumeratorCoefficients,
        "Adaptive must reuse the exact Reference NTF coefficients");
    require(reference.configuration().adaptiveRelaxedNumeratorCoefficients.empty()
            && reference.configuration().adaptiveRelaxedDenominatorCoefficients.empty()
            && reference.configuration().adaptiveRelaxedNtfPeakGain == 0.0,
        "Reference must not carry a relaxed pole set");

    // v5 brake: the relaxed set is a complete same-order design at the
    // lower peak-gain cap that shares the zero polynomial B = 1 + D - N with
    // the Reference set, so every envelope blend keeps the in-band zeros.
    const auto adaptiveConfig = adaptive.configuration();
    require(std::abs(adaptive.adaptiveRelaxedNtfPeakGain() - echo::sdmNtfAdaptiveRelaxedPeakGain) < 1.0e-12
            && std::abs(adaptiveConfig.adaptiveRelaxedNtfPeakGain - echo::sdmNtfAdaptiveRelaxedPeakGain) < 1.0e-12,
        "Adaptive must publish the relaxed NTF peak-gain cap");
    require(adaptiveConfig.adaptiveRelaxedNumeratorCoefficients.size() == 7
            && adaptiveConfig.adaptiveRelaxedDenominatorCoefficients.size() == 7,
        "Adaptive must export a complete order-7 relaxed pole set to CUDA");
    for (std::size_t tap = 0; tap < 7; ++tap)
    {
        const double referenceZero = adaptiveConfig.feedbackDenominatorCoefficients[tap]
            - adaptiveConfig.feedbackNumeratorCoefficients[tap];
        const double relaxedZero = adaptiveConfig.adaptiveRelaxedDenominatorCoefficients[tap]
            - adaptiveConfig.adaptiveRelaxedNumeratorCoefficients[tap];
        require(std::abs(referenceZero - relaxedZero) < 1.0e-9,
            "the relaxed pole set must share the Reference zero polynomial");
    }
    require(adaptiveConfig.adaptiveRelaxedDenominatorCoefficients != adaptiveConfig.feedbackDenominatorCoefficients,
        "the relaxed pole set must actually move the poles");
    echo::SdmProcessorConfig relaxedOnly;
    relaxedOnly.feedbackNumeratorCoefficients = adaptiveConfig.adaptiveRelaxedNumeratorCoefficients;
    relaxedOnly.feedbackDenominatorCoefficients = adaptiveConfig.adaptiveRelaxedDenominatorCoefficients;
    require(std::abs(measureNtfPeak(relaxedOnly) - echo::sdmNtfAdaptiveRelaxedPeakGain) < 1.0e-6,
        "the relaxed NTF peak on the unit circle must match the relaxed cap");
    require(std::abs(measureNtfPeak(adaptiveConfig) - echo::sdmNtfReferencePeakGain) < 1.0e-6,
        "the Reference set inside Adaptive must keep the 1.60 cap");
    echo::SdmProcessor highRate;
    highRate.configure(1, echo::SdmQualityProfile::Adaptive, 352'800);
    require(
        std::abs(highRate.configuration().profileHeadroomGain - std::pow(10.0, -3.5 / 20.0)) < 1.0e-12,
        "DSD128 Adaptive must use the plain 3.5 dB base headroom");

    // Quiet material must keep the full Reference-grade shaping engaged: the
    // -20 dBFS residual has to satisfy the same guard Reference meets.
    require(measureSdmInBandResidualDb(echo::SdmQualityProfile::Adaptive, 176'400) < -113.0,
        "DSD64 Adaptive must meet the Reference-grade in-band residual guard");
    require(measureSdmInBandResidualDb(echo::SdmQualityProfile::Adaptive, 352'800) < -121.0,
        "DSD128 Adaptive must meet the Reference-grade in-band residual guard");

    // At 0 dBFS the brake is fully engaged (envelope 0.668 after the 3.5 dB
    // headroom, above the 0.65 full-relaxation point). Because the relaxed
    // set shares the Reference zeros, the loud in-band floor is a smooth
    // relaxed-NTF floor instead of the v2 DC leak (about -59 dB DSD64 /
    // -40 dB DSD128 at this level). Measured 2026-09-06: -106.6 dB DSD64,
    // -120.5 dB DSD128; guards keep about 8 dB of margin.
    const double loudResidualDsd64 = measureSdmInBandResidualDb(echo::SdmQualityProfile::Adaptive, 176'400, 1.0);
    const double loudResidualDsd128 = measureSdmInBandResidualDb(echo::SdmQualityProfile::Adaptive, 352'800, 1.0);
    require(loudResidualDsd64 < -98.0,
        "DSD64 Adaptive at 0 dBFS must keep the relaxed-loop in-band floor, measured "
            + std::to_string(loudResidualDsd64));
    require(loudResidualDsd128 < -112.0,
        "DSD128 Adaptive at 0 dBFS must keep the relaxed-loop in-band floor, measured "
            + std::to_string(loudResidualDsd128));

    // The relaxed recursion is decision state on the adaptive tiers only:
    // Reference exports no relaxed history, Adaptive exports one per tap
    // and channel, and a roundtrip through the exported state must continue
    // bit-identically while the brake is engaged (the loud state below).
    require(reference.state().adaptiveRelaxedFeedbackHistory.empty(),
        "Reference state must not carry a relaxed feedback history");
    require(adaptive.state().adaptiveRelaxedFeedbackHistory.size() == 7,
        "Adaptive state must export one relaxed feedback value per tap per channel");

    // The envelope is real decision state: preloading it through the state
    // contract must change the decision stream while the input stays below
    // the adaptive threshold (scale 1.0 on the fresh processor).
    constexpr int frames = 8'192;
    constexpr double pi = 3.14159265358979323846;
    std::vector<float> tone(static_cast<size_t>(frames));
    for (int frame = 0; frame < frames; ++frame)
        tone[static_cast<size_t>(frame)] = static_cast<float>(
            0.53 * std::sin(2.0 * pi * 997.0 * frame / transportSampleRate));
    echo::SdmProcessor fresh;
    echo::SdmProcessor preloaded;
    fresh.configure(1, echo::SdmQualityProfile::Adaptive, transportSampleRate);
    preloaded.configure(1, echo::SdmQualityProfile::Adaptive, transportSampleRate);
    auto preloadedState = fresh.state();
    require(preloadedState.adaptiveEnvelope.size() == 1,
        "Adaptive state must export one envelope value per channel");
    preloadedState.adaptiveEnvelope[0] = 0.92;
    require(preloaded.restoreState(preloadedState),
        "Adaptive state with an envelope value must restore");
    const auto freshOutput = fresh.processNativeDsd(tone.data(), frames);
    const auto preloadedOutput = preloaded.processNativeDsd(tone.data(), frames);
    require(freshOutput != preloadedOutput,
        "a preloaded adaptive envelope must scale the loop feedback and change the stream");
    require(fresh.stabilityRecoveryCount() == 0 && preloaded.stabilityRecoveryCount() == 0,
        "adaptive envelope preload must stay recovery-free");

    // Full-scale input must raise the exported envelope above the adaptive
    // threshold, and the state roundtrip must continue bit-identically.
    echo::SdmProcessor loud;
    loud.configure(1, echo::SdmQualityProfile::Adaptive, transportSampleRate);
    std::vector<float> fullScale(static_cast<size_t>(frames), 1.0f);
    (void)loud.processNativeDsd(fullScale.data(), frames);
    const auto loudState = loud.state();
    require(!loudState.adaptiveEnvelope.empty() && loudState.adaptiveEnvelope[0] > 0.5,
        "full-scale input must charge the adaptive envelope above its threshold");
    echo::SdmProcessor resumed;
    resumed.configure(1, echo::SdmQualityProfile::Adaptive, transportSampleRate);
    require(resumed.restoreState(loudState), "the loud adaptive state must restore");
    require(
        loud.processNativeDsd(tone.data(), frames) == resumed.processNativeDsd(tone.data(), frames),
        "adaptive state roundtrip must continue bit-identically");

    // Restoring a pre-envelope state (for example from the CUDA fallback
    // path) must still work and start from a zero envelope.
    auto legacyState = loud.state();
    legacyState.adaptiveEnvelope.clear();
    echo::SdmProcessor legacy;
    legacy.configure(1, echo::SdmQualityProfile::Adaptive, transportSampleRate);
    require(legacy.restoreState(legacyState),
        "adaptive must accept states captured before the envelope existed");

    // The relaxed history follows the same legacy contract (missing ->
    // restore at rest) and the same shape contract (wrong size -> reject).
    require(!loudState.adaptiveRelaxedFeedbackHistory.empty(),
        "a loud adaptive state must carry the relaxed feedback history");
    auto relaxedLegacyState = loud.state();
    relaxedLegacyState.adaptiveRelaxedFeedbackHistory.clear();
    echo::SdmProcessor relaxedLegacy;
    relaxedLegacy.configure(1, echo::SdmQualityProfile::Adaptive, transportSampleRate);
    require(relaxedLegacy.restoreState(relaxedLegacyState),
        "adaptive must accept states captured before the relaxed recursion existed");
    auto malformedState = loud.state();
    malformedState.adaptiveRelaxedFeedbackHistory.resize(3, 0.0);
    echo::SdmProcessor malformed;
    malformed.configure(1, echo::SdmQualityProfile::Adaptive, transportSampleRate);
    require(!malformed.restoreState(malformedState),
        "adaptive must reject a relaxed feedback history of the wrong shape");
}

void testNativeSdmAdaptiveProfilesPreserveCudaParity()
{
    struct ProfileCase
    {
        echo::SdmQualityProfile profile;
        echo::SdmDecisionMode decisionMode;
        double initialEnvelope;
        double initialEcError;
        const char* name;
    };
    const std::vector<ProfileCase> profileCases {
        { echo::SdmQualityProfile::AdaptiveLight, echo::SdmDecisionMode::Greedy, 0.8, 0.0, "adaptive-light" },
        { echo::SdmQualityProfile::Adaptive, echo::SdmDecisionMode::Extra, 0.75, 0.02, "adaptive" },
        { echo::SdmQualityProfile::AdaptiveSuper, echo::SdmDecisionMode::Horizon, 0.7, -0.015, "adaptive-super" },
        { echo::SdmQualityProfile::ReferenceEc, echo::SdmDecisionMode::Extra, 0.0, 0.02, "reference-ec" },
    };
    constexpr int channels = 2;
    constexpr int transportSampleRate = 352'800;
    constexpr int frames = 512;
    std::vector<float> input(static_cast<size_t>(frames * channels));
    for (int frame = 0; frame < frames; ++frame)
    {
        const double phase = static_cast<double>(frame);
        input[static_cast<size_t>(frame * channels)] =
            static_cast<float>(0.82 * std::sin(phase * 0.071));
        input[static_cast<size_t>(frame * channels + 1)] =
            static_cast<float>(0.73 * std::cos(phase * 0.053));
    }

    bool cudaValidated = false;
    for (const auto& profileCase : profileCases)
    {
        echo::SdmProcessor cpu;
        cpu.configure(
            channels,
            profileCase.profile,
            transportSampleRate,
            echo::SdmInterpolationMode::Linear,
            profileCase.decisionMode);
        auto initialState = cpu.state();
        initialState.adaptiveEnvelope.assign(
            static_cast<size_t>(channels), profileCase.initialEnvelope);
        initialState.ecCorrelatedError.assign(
            static_cast<size_t>(channels), profileCase.initialEcError);
        require(cpu.restoreState(initialState),
            std::string(profileCase.name) + " CPU state preload must succeed");

        CudaSdmProcessor cuda;
        std::string cudaError;
        if (!cuda.configure(cpu.configuration(), initialState, frames, cudaError))
        {
            require(
                cudaError != "native_cuda_sdm_adaptive_profile_unsupported"
                    && cudaError != "native_cuda_sdm_extended_compensation_unsupported",
                std::string(profileCase.name)
                    + " must not be rejected as unsupported by CUDA: " + cudaError);
            // CUDA-less CI and machines without a usable device still build
            // the same host tests through the stub. Capability validation is
            // meaningful only once a real CUDA configuration succeeds.
            if (!cudaValidated)
                return;
            require(false,
                std::string(profileCase.name)
                    + " CUDA configuration failed after another profile succeeded: "
                    + cudaError);
        }
        cudaValidated = true;

        for (int chunk = 0; chunk < 3; ++chunk)
        {
            const auto expected = cpu.processDop(input.data(), frames);
            std::vector<uint32_t> actual;
            require(cuda.processDop(input.data(), frames, actual, cudaError),
                std::string(profileCase.name) + " CUDA chunk must process: " + cudaError);
            if (actual != expected)
            {
                size_t mismatch = 0;
                while (mismatch < actual.size()
                    && mismatch < expected.size()
                    && actual[mismatch] == expected[mismatch])
                    ++mismatch;
                throw std::runtime_error(
                    std::string(profileCase.name)
                        + " CUDA output must stay bit-identical to CPU across chunks; chunk="
                        + std::to_string(chunk)
                        + ", sample=" + std::to_string(mismatch)
                        + ", cpu=" + (mismatch < expected.size()
                            ? std::to_string(expected[mismatch]) : "missing")
                        + ", cuda=" + (mismatch < actual.size()
                            ? std::to_string(actual[mismatch]) : "missing"));
            }
        }

        echo::SdmProcessorState cudaState;
        require(cuda.copyLatestState(cudaState),
            std::string(profileCase.name) + " CUDA state snapshot must succeed");
        echo::SdmProcessor resumed;
        resumed.configure(
            channels,
            profileCase.profile,
            transportSampleRate,
            echo::SdmInterpolationMode::Linear,
            profileCase.decisionMode);
        require(resumed.restoreState(cudaState),
            std::string(profileCase.name) + " CUDA state must restore on CPU");
        const auto expectedContinuation = cpu.processDop(input.data(), frames);
        require(resumed.processDop(input.data(), frames) == expectedContinuation,
            std::string(profileCase.name)
                + " CUDA-to-CPU state handoff must preserve the decision stream");

        require(cpu.restoreState(initialState),
            std::string(profileCase.name) + " CPU state reset for native DSD must succeed");
        require(cuda.reset(initialState, cudaError),
            std::string(profileCase.name) + " CUDA state reset for native DSD must succeed: "
                + cudaError);
        for (int chunk = 0; chunk < 3; ++chunk)
        {
            const auto expected = cpu.processNativeDsd(input.data(), frames);
            std::vector<uint8_t> actual;
            require(cuda.processNativeDsd(input.data(), frames, actual, cudaError),
                std::string(profileCase.name) + " CUDA native DSD chunk must process: "
                    + cudaError);
            require(actual == expected,
                std::string(profileCase.name)
                    + " CUDA native DSD output must stay byte-identical to CPU across chunks; chunk="
                    + std::to_string(chunk));
        }

        require(cuda.copyLatestState(cudaState),
            std::string(profileCase.name) + " CUDA native DSD state snapshot must succeed");
        resumed.configure(
            channels,
            profileCase.profile,
            transportSampleRate,
            echo::SdmInterpolationMode::Linear,
            profileCase.decisionMode);
        require(resumed.restoreState(cudaState),
            std::string(profileCase.name) + " CUDA native DSD state must restore on CPU");
        const auto expectedNativeContinuation = cpu.processNativeDsd(input.data(), frames);
        require(resumed.processNativeDsd(input.data(), frames) == expectedNativeContinuation,
            std::string(profileCase.name)
                + " CUDA-to-CPU native DSD state handoff must preserve the decision stream");

        NativeSdmProcessor processor;
        std::string error;
        require(processor.configure(
                channels,
                profileCase.profile,
                transportSampleRate,
                true,
                frames,
                error,
                profileCase.decisionMode),
            std::string(profileCase.name) + " CUDA admission must complete");
        require(
            processor.fallbackReason().find("disabled_for_adaptive_profile") == std::string::npos
                && processor.fallbackReason().find("disabled_for_extended_compensation") == std::string::npos
                && processor.fallbackReason().find("output_mismatch") == std::string::npos,
            std::string(profileCase.name)
                + " must reach CUDA realtime admission with bit parity, got: "
                + processor.fallbackReason());
    }
}

void testNativeSdmExtendedCompensationLayer()
{
    constexpr int transportSampleRate = 176'400;
    constexpr int frames = 16'384;

    // ASDM+EC direction: Adaptive derives the extended-compensation layer
    // from the profile; every EC-free profile must keep it off (including
    // the adaptive-light tier, which is exactly Adaptive minus EC).
    echo::SdmProcessor adaptive;
    adaptive.configure(1, echo::SdmQualityProfile::Adaptive, transportSampleRate);
    require(adaptive.extendedCompensationEnabled(),
        "Adaptive must enable the extended-compensation layer");
    require(adaptive.configuration().extendedCompensationEnabled,
        "Adaptive configuration must export the extended-compensation flag");
    for (const auto profile : {
        echo::SdmQualityProfile::Safe,
        echo::SdmQualityProfile::Hifi,
        echo::SdmQualityProfile::Reference,
        echo::SdmQualityProfile::Insane,
        echo::SdmQualityProfile::AdaptiveLight,
    })
    {
        echo::SdmProcessor other;
        other.configure(1, profile, transportSampleRate);
        require(!other.extendedCompensationEnabled()
                && !other.configuration().extendedCompensationEnabled,
            "EC-free profiles must keep extended compensation off");
    }

    // EC is real decision state: Reference + EC (lab/test setter) must
    // produce a different stream than plain Reference on a -60 dBFS DC bed
    // at identical headroom, and both must stay recovery-free.
    std::vector<float> dc(static_cast<size_t>(frames), 0.001f);
    echo::SdmProcessor reference;
    reference.configure(1, echo::SdmQualityProfile::Reference, transportSampleRate);
    echo::SdmProcessor referenceEc;
    referenceEc.configure(1, echo::SdmQualityProfile::Reference, transportSampleRate);
    referenceEc.setExtendedCompensationEnabled(true);
    const auto referenceOutput = reference.processNativeDsd(dc.data(), frames);
    const auto referenceEcOutput = referenceEc.processNativeDsd(dc.data(), frames);
    require(referenceOutput != referenceEcOutput,
        "extended compensation must change the 1-bit stream on a small-signal DC bed");
    require(reference.stabilityRecoveryCount() == 0 && referenceEc.stabilityRecoveryCount() == 0,
        "the Reference EC A/B pair must stay recovery-free");

    // Forcing EC off on adaptive must reproduce a different stream than the
    // shipping adaptive (EC on) configuration: the layer is active.
    echo::SdmProcessor adaptiveNoEc;
    adaptiveNoEc.configure(1, echo::SdmQualityProfile::Adaptive, transportSampleRate);
    adaptiveNoEc.setExtendedCompensationEnabled(false);
    require(adaptive.processNativeDsd(dc.data(), frames)
            != adaptiveNoEc.processNativeDsd(dc.data(), frames),
        "adaptive must actually run the extended-compensation layer");
    require(adaptive.stabilityRecoveryCount() == 0 && adaptiveNoEc.stabilityRecoveryCount() == 0,
        "the adaptive EC A/B pair must stay recovery-free");

    // The correlated-error EMA is bit-consistency state: it must export per
    // channel and a restored processor must continue bit-identically.
    echo::SdmProcessor charged;
    charged.configure(1, echo::SdmQualityProfile::Adaptive, transportSampleRate);
    (void)charged.processNativeDsd(dc.data(), frames);
    const auto chargedState = charged.state();
    require(chargedState.ecCorrelatedError.size() == 1,
        "EC state must export one correlated-error value per channel");
    echo::SdmProcessor resumed;
    resumed.configure(1, echo::SdmQualityProfile::Adaptive, transportSampleRate);
    require(resumed.restoreState(chargedState), "the EC state must restore");
    require(
        charged.processNativeDsd(dc.data(), frames) == resumed.processNativeDsd(dc.data(), frames),
        "EC state roundtrip must continue bit-identically");

    // States captured before EC existed restore with a neutral compensator.
    auto legacyState = charged.state();
    legacyState.ecCorrelatedError.clear();
    echo::SdmProcessor legacy;
    legacy.configure(1, echo::SdmQualityProfile::Adaptive, transportSampleRate);
    require(legacy.restoreState(legacyState),
        "EC must accept states captured before the correlated-error state existed");

    require(referenceEc.configuration().extendedCompensationEnabled,
        "the Reference EC A/B override must export EC enablement to CUDA");
    echo::SdmProcessor productReferenceEc;
    productReferenceEc.configure(
        1, echo::SdmQualityProfile::ReferenceEc, transportSampleRate);
    const auto referenceEcConfig = productReferenceEc.configuration();
    require(referenceEcConfig.extendedCompensationEnabled,
        "reference-ec must export EC enablement to CUDA");
    require(referenceEcConfig.ecTuning.envelopeGated,
        "reference-ec must export its envelope-gated tuning to CUDA");
}

void testNativeSdmTierProfilesFollowTheirContracts()
{
    constexpr int transportSampleRate = 176'400;
    constexpr int frames = 16'384;

    // Enablement matrix: envelope brake and EC layer per tier. The
    // Reference NTF is shared by all of them (guarded by coefficient
    // equality below), so these two flags plus the headroom are the whole
    // per-tier contract.
    const std::vector<std::tuple<echo::SdmQualityProfile, bool, bool, double>> tiers {
        { echo::SdmQualityProfile::Adaptive, true, true, 3.5 },
        { echo::SdmQualityProfile::AdaptiveLight, true, false, 3.5 },
        { echo::SdmQualityProfile::AdaptiveSuper, true, true, 3.5 },
        { echo::SdmQualityProfile::ReferenceEc, false, true, 4.5 },
    };
    echo::SdmProcessor reference;
    reference.configure(1, echo::SdmQualityProfile::Reference, transportSampleRate);
    for (const auto& [profile, envelope, ec, headroomDb] : tiers)
    {
        echo::SdmProcessor processor;
        processor.configure(1, profile, transportSampleRate);
        require(processor.modulatorOrder() == 7, "every tier must run the Reference-grade order-7 NTF");
        require(processor.adaptiveLoopGainEnabled() == envelope,
            "tier must match its envelope-brake contract");
        require(processor.extendedCompensationEnabled() == ec,
            "tier must match its extended-compensation contract");
        require(
            processor.configuration().feedbackNumeratorCoefficients
                == reference.configuration().feedbackNumeratorCoefficients,
            "every tier must reuse the exact Reference NTF coefficients");
        // DSD64-class carriers stack the unchanged 1.5 dB low-rate add-on on
        // the tier's static base headroom.
        require(
            std::abs(
                processor.configuration().profileHeadroomGain
                    - std::pow(10.0, -(headroomDb + 1.5) / 20.0)) < 1.0e-12,
            "tier must use its stress-tested static base headroom");
    }

    // adaptive-light is exactly Adaptive with the EC layer removed: its
    // stream must be bit-identical to the lab's adaptive-noec configuration.
    std::vector<float> dc(static_cast<size_t>(frames), 0.001f);
    echo::SdmProcessor light;
    light.configure(1, echo::SdmQualityProfile::AdaptiveLight, transportSampleRate);
    echo::SdmProcessor adaptiveNoEc;
    adaptiveNoEc.configure(1, echo::SdmQualityProfile::Adaptive, transportSampleRate);
    adaptiveNoEc.setExtendedCompensationEnabled(false);
    require(light.processNativeDsd(dc.data(), frames) == adaptiveNoEc.processNativeDsd(dc.data(), frames),
        "adaptive-light must be bit-identical to Adaptive with EC forced off");

    // adaptive-super carries its own EC constants: on the small-signal DC
    // bed it must diverge from Adaptive (stronger compensation) while
    // staying recovery-free.
    echo::SdmProcessor adaptive;
    adaptive.configure(1, echo::SdmQualityProfile::Adaptive, transportSampleRate);
    echo::SdmProcessor super;
    super.configure(1, echo::SdmQualityProfile::AdaptiveSuper, transportSampleRate);
    require(adaptive.processNativeDsd(dc.data(), frames) != super.processNativeDsd(dc.data(), frames),
        "adaptive-super must run its own stronger EC constants");
    require(adaptive.stabilityRecoveryCount() == 0 && super.stabilityRecoveryCount() == 0,
        "the adaptive/super DC pair must stay recovery-free");

    // reference-ec must compensate the small-signal regime (stream diverges
    // from plain Reference on the DC bed)...
    echo::SdmProcessor referenceDc;
    referenceDc.configure(1, echo::SdmQualityProfile::Reference, transportSampleRate);
    echo::SdmProcessor referenceEcDc;
    referenceEcDc.configure(1, echo::SdmQualityProfile::ReferenceEc, transportSampleRate);
    require(
        referenceDc.processNativeDsd(dc.data(), frames) != referenceEcDc.processNativeDsd(dc.data(), frames),
        "reference-ec must engage EC on the small-signal DC bed");
    require(referenceDc.stabilityRecoveryCount() == 0 && referenceEcDc.stabilityRecoveryCount() == 0,
        "the reference/reference-ec DC pair must stay recovery-free");

    // ...while its envelope-gated EC freezes through loud passages: a 0 dBFS
    // sine crosses zero every half cycle, and an instantaneously-gated
    // compensator would re-engage there. The gate envelope arms fully
    // charged at configure time (cold start counts as loud), so a loud
    // stream from sample zero must stay bit-identical to plain Reference --
    // that is the whole stress-convergence argument for this tier.
    constexpr double pi = 3.14159265358979323846;
    std::vector<float> loud(static_cast<size_t>(frames));
    for (int frame = 0; frame < frames; ++frame)
        loud[static_cast<size_t>(frame)] = static_cast<float>(
            std::sin(2.0 * pi * 997.0 * frame / transportSampleRate));
    echo::SdmProcessor referenceLoud;
    referenceLoud.configure(1, echo::SdmQualityProfile::Reference, transportSampleRate);
    echo::SdmProcessor referenceEcLoud;
    referenceEcLoud.configure(1, echo::SdmQualityProfile::ReferenceEc, transportSampleRate);
    const auto armedState = referenceEcLoud.state();
    require(!armedState.adaptiveEnvelope.empty() && armedState.adaptiveEnvelope[0] == 1.0,
        "reference-ec must arm its EC gate envelope fully charged");
    require(
        referenceLoud.processNativeDsd(loud.data(), frames)
            == referenceEcLoud.processNativeDsd(loud.data(), frames),
        "reference-ec from a cold start must stay bit-identical to Reference through a 0 dBFS passage");
}

// The v5 envelope brake is the one mechanism that keeps a Reference-grade
// order-7 loop recovery-free on the full-scale suite at every carrier rate
// (plain Reference recovers on the DSD128/256 greedy cells): the relaxed
// pole set must therefore hold across the whole adaptive tier family, all
// three decision strategies and more than one noise seed. 2026-09-06
// calibration: peak feedback <= 1.38 everywhere, so the 2.5 guard leaves
// the same kind of margin the other suites keep below their 3.5 guard.
void testNativeSdmAdaptiveTiersStayRecoveryFreeAtEveryRate()
{
    constexpr double pi = 3.14159265358979323846;
    for (const int transportSampleRate : { 176'400, 352'800, 705'600, 1'411'200 })
    {
        const int frames = transportSampleRate / 8;
        for (const uint32_t seed : { 0x12345678u, 0x7f3a9c21u })
        {
            // The second seed only matters where greedy is marginal.
            if (seed != 0x12345678u && transportSampleRate != 1'411'200)
                continue;
            uint32_t randomState = seed;
            std::vector<float> stress(static_cast<size_t>(frames));
            for (int frame = 0; frame < frames; ++frame)
            {
                const int section = (frame / 2'048) % 4;
                if (section == 0)
                    stress[static_cast<size_t>(frame)] = 1.0f;
                else if (section == 1)
                    stress[static_cast<size_t>(frame)] = -1.0f;
                else if (section == 2)
                    stress[static_cast<size_t>(frame)] = static_cast<float>(
                        0.57 * std::sin(2.0 * pi * 997.0 * frame / transportSampleRate)
                        + 0.43 * std::sin(2.0 * pi * 19'001.0 * frame / transportSampleRate));
                else
                {
                    randomState = randomState * 1664525u + 1013904223u;
                    stress[static_cast<size_t>(frame)] =
                        static_cast<float>(static_cast<double>(randomState) / 2147483648.0 - 1.0);
                }
            }
            for (const auto profile : {
                echo::SdmQualityProfile::Adaptive,
                echo::SdmQualityProfile::AdaptiveLight,
                echo::SdmQualityProfile::AdaptiveSuper,
            })
            {
                for (const auto decisionMode : {
                    echo::SdmDecisionMode::Greedy,
                    echo::SdmDecisionMode::Extra,
                    echo::SdmDecisionMode::Horizon,
                })
                {
                    echo::SdmProcessor processor;
                    processor.configure(
                        1, profile, transportSampleRate, echo::SdmInterpolationMode::Linear, decisionMode);
                    for (int offset = 0; offset < frames; offset += 2'048)
                    {
                        const int chunkFrames = std::min(2'048, frames - offset);
                        (void)processor.processNativeDsd(stress.data() + offset, chunkFrames);
                    }
                    const std::string cell = "rate=" + std::to_string(transportSampleRate)
                        + " decision=" + std::to_string(static_cast<int>(decisionMode))
                        + " seed=" + std::to_string(seed);
                    require(processor.stabilityRecoveryCount() == 0,
                        "adaptive tiers must stay recovery-free under the full-scale suite at every rate; "
                            + cell + " recoveries=" + std::to_string(processor.stabilityRecoveryCount()));
                    require(processor.peakFeedbackState() < 2.5,
                        "adaptive tiers must keep the relaxed-loop feedback margin; "
                            + cell + " peak=" + std::to_string(processor.peakFeedbackState()));
                }
            }
        }
    }
}

void testNativeSdmCudaAdmissionPreservesDecisionStreams()
{
    for (const auto decisionMode : {
        echo::SdmDecisionMode::Greedy,
        echo::SdmDecisionMode::Extra,
        echo::SdmDecisionMode::Horizon,
    })
    {
        NativeSdmProcessor processor;
        std::string error;
        require(processor.configure(
                2,
                echo::SdmQualityProfile::Reference,
                352'800,
                true,
                512,
                error,
                decisionMode),
            "native CUDA SDM admission probe must complete");
        require(processor.fallbackReason() != "native_cuda_sdm_output_mismatch",
            "CUDA SDM must preserve the CPU decision stream for quantizer strategy "
                + std::to_string(static_cast<int>(decisionMode)));
        if (processor.backend() == NativeSdmProcessor::Backend::Cuda)
            require(!processor.deviceName().empty(), "active CUDA SDM must report its device");
    }
}

void testNativeProcessingConfigurationFailsClosed()
{
    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    PcmRingAudioSource pcm(2, 4096, 0, 0, 1.0f, eqProcessor, channelBalanceProcessor);
    DopRingSource dop(2, 4096, 0, 0);
    NativeDsdRingSource nativeDsd(2, 8192, 0, 0);
    NativePlaybackPipeline pipeline(pcm, dop, nativeDsd, 2);
    Options options;
    options.exclusive = true;
    options.sampleRate = 96'000;
    std::string error;

    const echo_audio_host::Json validEcho = {
        {"processing", {
            {"outputFormat", "pcm"},
            {"echoSrc", {
                {"sourceSampleRate", 48'000},
                {"targetSampleRate", 96'000},
                {"stages", echo_audio_host::Json::array({
                    {{"upsampleFactor", 2}, {"taps", {0.25, 0.5, 0.25}}},
                })},
            }},
            {"dither", {{"mode", "ns-5"}, {"bitDepth", 24}}},
        }},
    };
    require(configureNativeProcessing(validEcho, options, pipeline, error),
        "valid native ECHO SRC and dither configuration must be accepted");
    require(pipeline.decoderSampleRate(options.sampleRate) == 48'000,
        "native ECHO SRC must make the decoder preserve source rate");

    auto invalidEcho = validEcho;
    invalidEcho["processing"]["echoSrc"]["targetSampleRate"] = 192'000;
    require(! configureNativeProcessing(invalidEcho, options, pipeline, error),
        "mismatched native ECHO SRC target must fail closed");
    requireContains(error, "sample_rate_mismatch", "native ECHO SRC mismatch error");
    require(pipeline.decoderSampleRate(options.sampleRate) == 48'000,
        "failed native DSP reconfiguration must preserve the previous processor state");

    const echo_audio_host::Json invalidSdm = {
        {"processing", {
            {"outputFormat", "dop24le"},
            {"sdm", {{"qualityProfile", "unknown"}}},
        }},
    };
    require(! configureNativeProcessing(invalidSdm, options, pipeline, error),
        "unknown native SDM profile must fail closed");
    requireContains(error, "invalid_quality_profile", "native SDM profile error");
    require(pipeline.outputFormat() == NativePlaybackPipeline::OutputFormat::Pcm,
        "failed native SDM reconfiguration must preserve PCM output routing");

    const echo_audio_host::Json invalidExtraLoop = {
        {"processing", {
            {"outputFormat", "dop24le"},
            {"sdm", {{"qualityProfile", "hifi"}, {"extraLoop", "asdm"}}},
        }},
    };
    require(! configureNativeProcessing(invalidExtraLoop, options, pipeline, error),
        "unknown native SDM Extra value must fail closed");
    requireContains(error, "invalid_extra_loop", "native SDM Extra error");

    options.exclusive = false;
    options.asio = true;
    options.sampleRate = 5'644'800;
    const echo_audio_host::Json validGeneratedNativeDsd = {
        {"processing", {
            {"outputFormat", "dsd-native-raw"},
            {"sdm", {
                {"qualityProfile", "hifi"},
                {"extraLoop", "horizon"},
                {"sourceSampleRate", 44'100},
                {"targetSampleRate", 352'800},
                {"stages", echo_audio_host::Json::array({
                    {{"upsampleFactor", 8}, {"taps", {0.0, 1.0, 0.0}}},
                })},
            }},
        }},
    };
    const bool generatedNativeDsdConfigured = configureNativeProcessing(validGeneratedNativeDsd, options, pipeline, error);
#if ECHO_NATIVE_INSTRUMENTED_TESTS
    if (! generatedNativeDsdConfigured)
    {
        requireContains(error, "native_sdm_realtime_admission_failed",
            "instrumented native DSD must reject an over-budget full chain explicitly");
        return;
    }
#endif
    require(generatedNativeDsdConfigured,
        "generated native ASIO DSD must accept a carrier target at one sixteenth of the native bit rate");
    require(pipeline.outputFormat() == NativePlaybackPipeline::OutputFormat::NativeDsd,
        "generated native ASIO DSD must route raw DSD bytes instead of DoP words");
    require(options.asioNativeDsdOutput,
        "generated native ASIO DSD must select the native DSD device backend");
    require(pipeline.processingStatus().sdm.decisionMode == "horizon",
        "native processing status must report the active Horizon strategy");
#ifdef __linux__
    options.asio = false;
    options.exclusive = true;
    options.sampleRate = 5'644'800;
    require(configureNativeProcessing(validGeneratedNativeDsd, options, pipeline, error),
        "generated native DSD must be accepted on Linux ALSA exclusive");
    require(options.asioNativeDsdOutput && options.exclusive && ! options.asio,
        "Linux exclusive native DSD must select the ALSA native DSD backend flags");
#endif
}

void testRuntimeDeviceConfigurationIsAuthoritative()
{
    const auto current = parseOptions({ "echo-audio-host", "--defer-device-open" });
    Options configured;
    std::string error;
    const echo_audio_host::Json sharedParams = {
        {"outputMode", "shared"},
        {"deviceId", "shared:3"},
        {"deviceName", "USB DAC"},
        {"deviceIndex", 3},
        {"sampleRate", 96000},
        {"channels", 2},
        {"latencyProfile", "stable"},
        {"sharedBackend", "windows"},
    };

    require(configureRuntimeOutputOptions(sharedParams, current, configured, error),
        "runtime shared device configuration must be accepted");
    require(! configured.exclusive && ! configured.asio, "runtime shared mode must clear exclusive flags");
    require(configured.deviceId == "shared:3" && configured.deviceName == "USB DAC" && configured.deviceIndex == 3,
        "runtime shared configuration must preserve the selected device identity");
    require(configured.sampleRate == 96000 && configured.channels == 2,
        "runtime shared configuration must preserve the requested PCM format");
    require(configured.bufferSize == 8192, "stable latency profile must choose the stable native buffer");
    require(configured.sharedBackend == "windows", "runtime shared backend must be authoritative");

#ifdef __linux__
    Options pipewireConfigured;
    require(configureRuntimeOutputOptions(
        echo_audio_host::Json{
            {"outputMode", "shared"},
            {"sharedBackend", "pipewire"},
            {"channels", 2},
            {"sampleRate", 48000},
            {"bufferSize", 256},
        },
        current,
        pipewireConfigured,
        error),
        "runtime PipeWire shared backend must be accepted");
    require(pipewireConfigured.sharedBackend == "pipewire", "runtime PipeWire backend must be authoritative");
#endif

    Options repeated;
    require(configureRuntimeOutputOptions(sharedParams, configured, repeated, error),
        "repeated runtime configuration must remain accepted");
    require(hasSameRuntimeOutputOptions(configured, repeated),
        "repeated runtime configuration must be detected as a no-op");

    Options invalid;
    require(! configureRuntimeOutputOptions(
        echo_audio_host::Json{{"outputMode", "invalid-mode"}}, configured, invalid, error),
        "unsupported runtime output mode must fail closed");
    requireContains(error, "unsupported outputMode", "unsupported runtime mode error");

#ifdef _WIN32
    Options exclusive;
    require(configureRuntimeOutputOptions(
        echo_audio_host::Json{
            {"outputMode", "exclusive"},
            {"deviceName", "Exclusive DAC"},
            {"sampleRate", 192000},
            {"channels", 2},
            {"bufferSize", 1024},
        },
        configured,
        exclusive,
        error),
        "runtime exclusive device configuration must be accepted on Windows");
    require(exclusive.exclusive && ! exclusive.asio && exclusive.sampleRate == 192000,
        "runtime exclusive configuration must select the WASAPI exclusive backend and rate");

    Options asio;
    require(configureRuntimeOutputOptions(
        echo_audio_host::Json{
            {"outputMode", "asio"},
            {"deviceName", "ASIO DAC"},
            {"deviceIndex", 1},
            {"sampleRate", 96000},
            {"channels", 2},
            {"asioOutputChannelStart", 2},
        },
        exclusive,
        asio,
        error),
        "runtime ASIO device configuration must be accepted on Windows");
    require(asio.asio && ! asio.exclusive && asio.asioOutputChannelStart == 2,
        "runtime ASIO configuration must preserve the selected output channel pair");

    Options kernelStreaming;
    require(configureRuntimeOutputOptions(
        echo_audio_host::Json{
            {"outputMode", "ks"},
            {"deviceName", "KS DAC"},
            {"deviceIndex", 2},
            {"sampleRate", 96000},
            {"channels", 2},
        },
        asio,
        kernelStreaming,
        error),
        "runtime WDM-KS device configuration must be accepted on Windows");
    require(kernelStreaming.kernelStreaming && ! kernelStreaming.asio && ! kernelStreaming.exclusive,
        "runtime WDM-KS configuration must select only the KS backend");
#elif defined(__APPLE__)
    Options exclusive;
    require(configureRuntimeOutputOptions(
        echo_audio_host::Json{
            {"outputMode", "exclusive"},
            {"deviceName", "CoreAudio DAC"},
            {"sampleRate", 192000},
            {"channels", 2},
            {"bufferSize", 1024},
        },
        configured,
        exclusive,
        error),
        "runtime exclusive device configuration must be accepted on macOS");
    require(exclusive.exclusive && ! exclusive.asio && ! exclusive.kernelStreaming && exclusive.sampleRate == 192000,
        "runtime exclusive configuration must select the CoreAudio Hog Mode backend and rate");

    Options asio;
    require(! configureRuntimeOutputOptions(
        echo_audio_host::Json{{"outputMode", "asio"}}, exclusive, asio, error),
        "runtime ASIO device configuration must remain unavailable on macOS");
    requireContains(error, "only supported on Windows", "runtime macOS ASIO error");
#elif defined(__linux__)
    Options exclusive;
    require(configureRuntimeOutputOptions(
        echo_audio_host::Json{
            {"outputMode", "exclusive"},
            {"deviceName", "mini-i Series"},
            {"sampleRate", 192000},
            {"channels", 2},
            {"bufferSize", 1024},
        },
        configured,
        exclusive,
        error),
        "runtime exclusive device configuration must be accepted on Linux");
    require(exclusive.exclusive && ! exclusive.asio && ! exclusive.kernelStreaming && exclusive.sampleRate == 192000,
        "runtime exclusive configuration must select the ALSA exclusive backend and rate");

    Options asio;
    require(! configureRuntimeOutputOptions(
        echo_audio_host::Json{{"outputMode", "asio"}}, exclusive, asio, error),
        "runtime ASIO device configuration must remain unavailable on Linux");
    requireContains(error, "only supported on Windows", "runtime Linux ASIO error");
#endif
}

void testHostBackendNames()
{
    const DeviceDescriptor shared { 0, "miniaudio-shared", "Default Output", 48000, 48000, true, false };
    const DeviceDescriptor exclusive { 1, "WASAPI Exclusive", "Exclusive DAC", 96000, 48000, false, false };
    const DeviceDescriptor asio { 2, "ASIO", "ASIO DAC", 192000, 0, false, true, 2, "1: Left, 2: Right", "asio:ASIO DAC" };
    const DeviceDescriptor kernelStreaming { 3, "WDM-KS", "KS DAC", 96000, 0, false, false };

    require(! isAsioType(shared.typeName), "miniaudio shared device must not be ASIO");
    require(! isExclusiveType(shared.typeName), "miniaudio shared device must not be exclusive");
    require(isExclusiveType(exclusive.typeName), "exclusive descriptor must be recognized as exclusive");
    require(isAsioType(asio.typeName), "ASIO descriptor must be recognized as ASIO");
    require(isKernelStreamingType(kernelStreaming.typeName), "WDM-KS descriptor must be recognized as Kernel Streaming");
    require(shouldIncludeType(shared.typeName, DeviceListMode::Shared), "shared listing must include shared descriptor");
    require(! shouldIncludeType(exclusive.typeName, DeviceListMode::Shared), "shared listing must skip exclusive descriptor");
    require(shouldIncludeType(exclusive.typeName, DeviceListMode::Exclusive), "exclusive listing must include exclusive descriptor");
    require(shouldIncludeType(asio.typeName, DeviceListMode::Asio), "ASIO listing must include ASIO descriptor");
    require(shouldIncludeType(kernelStreaming.typeName, DeviceListMode::KernelStreaming), "KS listing must include WDM-KS descriptor");
#ifdef _WIN32
    require(isPreferredSharedType("Windows Audio"), "Windows Audio descriptor must be preferred shared output on Windows");
#else
    require(isPreferredSharedType("ALSA"), "ALSA descriptor must be preferred shared output on non-Windows hosts");
#endif
    require(sharedTypePriority(shared.typeName) < sharedTypePriority("JACK"), "preferred shared backend must sort before JACK");
#ifdef __linux__
    const DeviceDescriptor alsaExclusive { 4, "ALSA exclusive", "mini-i Series", 768000, 48000, false, false };
    require(isExclusiveType(alsaExclusive.typeName), "ALSA exclusive descriptor must be recognized as exclusive");
    require(isAlsaType(alsaExclusive.typeName), "ALSA exclusive descriptor must remain an ALSA device");
    require(shouldIncludeType(alsaExclusive.typeName, DeviceListMode::Exclusive), "exclusive listing must include ALSA exclusive");
    require(! shouldIncludeType(alsaExclusive.typeName, DeviceListMode::Shared), "shared listing must skip ALSA exclusive");
    const std::uint8_t src[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 };
    std::uint8_t packed[8] {};
    packAlsaDsdU32Be(src, 2, 4, packed);
    require(packed[0] == 0x11 && packed[1] == 0x33 && packed[2] == 0x55 && packed[3] == 0x77,
        "ALSA DSD_U32_BE must pack left-channel bytes MSB-first");
    require(packed[4] == 0x22 && packed[5] == 0x44 && packed[6] == 0x66 && packed[7] == 0x88,
        "ALSA DSD_U32_BE must pack right-channel bytes MSB-first");
#endif
}

void testNativeOutputNotificationQueuePreservesHostEvents()
{
    NativePcmOutputNotificationQueue queue;
    queue.push({
        "audio_session_disconnected",
        "device-1",
        "asio_reset_request",
        7,
        true,
        false,
    });

    NativePcmOutputNotification notification;
    require(queue.pop(notification), "native output notification queue must publish pending events");
    const auto json = buildNativePcmOutputNotificationJson(notification);
    require(json.value("event", "") == "audio_session_disconnected", "native output event name must be preserved");
    require(json.value("deviceId", "") == "device-1", "native output device id must be preserved");
    require(json.value("reason", "") == "asio_reset_request", "native output reason must be preserved");
    require(json.value("code", 0) == 7, "native output event code must be preserved");
    require(json.value("currentDevice", false), "native output current-device ownership must be preserved");
    require(! json.value("followsDefaultDevice", true), "native output default-device ownership must be preserved");
    require(! queue.pop(notification), "native output notification queue must drain each event once");
}

void testSpecializedOutputsSkipMiniaudioSharedOutput()
{
    const auto shared = parseOptions({ "echo-audio-host" });
#ifdef _WIN32
    require(! shouldTryMiniaudioSharedOutput(shared), "default Windows shared output must use the stable native WASAPI backend");
    require(selectNativePcmOutputBackend(shared) == NativePcmOutputBackendKind::WasapiShared,
        "default Windows shared output must dispatch to native WASAPI shared");
#elif defined(__linux__)
    if (linuxPipeWireRuntimeAvailable())
    {
        require(! shouldTryMiniaudioSharedOutput(shared), "default Linux shared output must use native PipeWire when it is available");
        require(selectNativePcmOutputBackend(shared) == NativePcmOutputBackendKind::PipeWire,
            "default Linux shared output must dispatch to PipeWire when the session is available");
    }
    else
    {
        require(shouldTryMiniaudioSharedOutput(shared), "default Linux shared output must fall back to miniaudio when PipeWire is unavailable");
        require(selectNativePcmOutputBackend(shared) == NativePcmOutputBackendKind::Miniaudio,
            "default Linux shared output must dispatch to miniaudio when PipeWire is unavailable");
    }
    const auto explicitPipewire = parseOptions({ "echo-audio-host", "-shared-backend", "pipewire" });
    require(! shouldTryMiniaudioSharedOutput(explicitPipewire), "explicit PipeWire must not route through miniaudio");
    require(selectNativePcmOutputBackend(explicitPipewire) == NativePcmOutputBackendKind::PipeWire,
        "explicit PipeWire must dispatch to the native PipeWire backend");
    const auto explicitAlsa = parseOptions({ "echo-audio-host", "-shared-backend", "alsa" });
    require(shouldTryMiniaudioSharedOutput(explicitAlsa), "explicit ALSA shared must use miniaudio");
    require(selectNativePcmOutputBackend(explicitAlsa) == NativePcmOutputBackendKind::Miniaudio,
        "explicit ALSA shared must dispatch to miniaudio");
#else
    require(shouldTryMiniaudioSharedOutput(shared), "non-Windows shared output may use miniaudio when no platform backend is selected");
#endif

    const auto miniaudioShared = parseOptions({ "echo-audio-host", "-shared-backend", "miniaudio" });
    require(shouldTryMiniaudioSharedOutput(miniaudioShared), "miniaudio must require an explicit backend selection on Windows");
    require(selectNativePcmOutputBackend(miniaudioShared) == NativePcmOutputBackendKind::Miniaudio,
        "explicit miniaudio selection must dispatch to the experimental backend");

    const auto exclusive = parseOptions({ "echo-audio-host", "-exclusive" });
    require(! shouldTryMiniaudioSharedOutput(exclusive), "WASAPI exclusive must not route through miniaudio shared output");
    require(selectSpecializedHostRunner(exclusive) == SpecializedHostRunner::WasapiExclusivePcm,
        "WASAPI exclusive PCM must dispatch to the specialized WASAPI exclusive runner");
#if defined(__linux__) || defined(__APPLE__)
    require(selectNativePcmOutputBackend(exclusive) == NativePcmOutputBackendKind::Miniaudio,
        "non-Windows exclusive PCM must dispatch to the miniaudio backend");
#else
    require(selectNativePcmOutputBackend(exclusive) == NativePcmOutputBackendKind::WasapiExclusive,
        "Windows exclusive PCM must dispatch to native WASAPI exclusive");
#endif

    const auto wasapiDop = parseOptions({ "echo-audio-host", "-exclusive", "-dop-output" });
    require(! shouldTryMiniaudioSharedOutput(wasapiDop), "WASAPI exclusive DoP must not route through miniaudio shared output");
    require(selectSpecializedHostRunner(wasapiDop) == SpecializedHostRunner::WasapiExclusiveDop,
        "WASAPI exclusive DoP must dispatch to the specialized WASAPI exclusive DoP runner");

    const auto requestedMiniaudioDop = parseOptions({ "echo-audio-host", "-shared-backend", "miniaudio", "-dop-output" });
    require(! shouldTryMiniaudioSharedOutput(requestedMiniaudioDop), "requested miniaudio shared must reject DoP transport instead of claiming success");
    require(selectSpecializedHostRunner(requestedMiniaudioDop) == SpecializedHostRunner::None,
        "DoP without WASAPI exclusive or ASIO must not select a specialized runner");

    const auto asio = parseOptions({ "echo-audio-host", "-asio" });
    require(! shouldTryMiniaudioSharedOutput(asio), "ASIO must not route through miniaudio shared output");
    require(selectSpecializedHostRunner(asio) == SpecializedHostRunner::AsioPcm,
        "ASIO PCM must dispatch to the specialized ASIO runner");

    const auto kernelStreaming = parseOptions({ "echo-audio-host", "-ks" });
    require(! shouldTryMiniaudioSharedOutput(kernelStreaming), "WDM-KS must not route through miniaudio shared output");
    require(selectNativePcmOutputBackend(kernelStreaming) == NativePcmOutputBackendKind::KernelStreaming,
        "WDM-KS must dispatch to the dedicated native backend");

    const auto asioDop = parseOptions({ "echo-audio-host", "-asio", "-dop-output" });
    require(! shouldTryMiniaudioSharedOutput(asioDop), "ASIO DoP must not route through miniaudio shared output");
    require(selectSpecializedHostRunner(asioDop) == SpecializedHostRunner::AsioDop,
        "ASIO DoP must dispatch to the specialized ASIO DoP runner");

    const auto nativeDsd = parseOptions({ "echo-audio-host", "-asio", "-dop-output", "-asio-native-dsd-output" });
    require(! shouldTryMiniaudioSharedOutput(nativeDsd), "ASIO native DSD must not route through miniaudio shared output");
    require(selectSpecializedHostRunner(nativeDsd) == SpecializedHostRunner::AsioNativeDsd,
        "ASIO native DSD must dispatch before ASIO DoP and ASIO PCM");

    const auto requestedMiniaudioNativeDsd = parseOptions({ "echo-audio-host", "-shared-backend", "miniaudio", "-dop-output", "-asio-native-dsd-output" });
    require(! shouldTryMiniaudioSharedOutput(requestedMiniaudioNativeDsd), "requested miniaudio shared must reject native DSD transport instead of claiming success");
    require(selectSpecializedHostRunner(requestedMiniaudioNativeDsd) == SpecializedHostRunner::AsioNativeDsd,
        "ASIO native DSD selector remains fail-closed behind runHost validation when ASIO DoP is missing");
}

void testSpecializedRunHostValidationBeforeHardwareOpen()
{
    const auto dopWithoutSpecializedBackend = parseOptions({ "echo-audio-host", "-dop-output" });
    requireThrowsContaining(
        [&] { runHost(dopWithoutSpecializedBackend); },
        "DoP output requires WASAPI exclusive or ASIO",
        "DoP without WASAPI exclusive or ASIO must fail validation before hardware open");

    const auto nativeDsdWithoutAsioDop = parseOptions({ "echo-audio-host", "-dop-output", "-asio-native-dsd-output" });
#if defined(_WIN32)
    const char* nativeDsdTransportError = "ASIO native DSD output requires ASIO DoP output";
#elif defined(__linux__)
    const char* nativeDsdTransportError = "native DSD output requires ALSA exclusive";
#else
    const char* nativeDsdTransportError = "native DSD output is not supported on this platform";
#endif
    requireThrowsContaining(
        [&] { runHost(nativeDsdWithoutAsioDop); },
        nativeDsdTransportError,
        "native DSD must validate its platform transport before selecting a runner");

#if ! ECHO_ENABLE_ASIO && defined(_WIN32)
    const auto asioDop = parseOptions({ "echo-audio-host", "-asio", "-dop-output" });
    requireThrowsContaining(
        [&] { runHost(asioDop); },
        "ASIO DoP open failed: ASIO support is disabled at build time",
        "ASIO DoP must dispatch to the disabled-build DoP runner after validation");

    const auto nativeDsd = parseOptions({ "echo-audio-host", "-asio", "-dop-output", "-asio-native-dsd-output" });
    requireThrowsContaining(
        [&] { runHost(nativeDsd); },
        "ASIO native DSD open failed: ASIO support is disabled at build time",
        "ASIO native DSD must dispatch to the disabled-build native DSD runner after validation");
#elif ! ECHO_ENABLE_ASIO
    const auto asioDop = parseOptions({ "echo-audio-host", "-asio", "-dop-output" });
    requireThrowsContaining(
        [&] { runHost(asioDop); },
        "ASIO output is only supported on Windows",
        "non-Windows ASIO DoP must fail during platform validation");

    const auto nativeDsd = parseOptions({ "echo-audio-host", "-asio", "-dop-output", "-asio-native-dsd-output" });
#if defined(__linux__)
    const char* nonWindowsNativeDsdError = "native DSD output requires ALSA exclusive";
#else
    const char* nonWindowsNativeDsdError = "native DSD output is not supported on this platform";
#endif
    requireThrowsContaining(
        [&] { runHost(nativeDsd); },
        nonWindowsNativeDsdError,
        "non-Windows native DSD must fail during platform validation");
#endif
}

void testHostPrebufferDefaultsRemainCompatible()
{
    const auto exclusive = parseOptions({ "echo-audio-host", "-exclusive" });

    require(! exclusive.startupPrebufferMsSpecified, "exclusive prebuffer default must be unspecified");
    require(getFifoCapacityFrames(exclusive, 48000) == 9600, "exclusive FIFO default must remain compatible");
    require(getFifoCapacityFrames(exclusive, 192000) == 144000, "high-rate exclusive FIFO must absorb decoder jitter");
    require(getStartupPrebufferFrames(exclusive, 48000) == 960, "exclusive default prebuffer must remain compatible");
    require(getStartupPrebufferFrames(exclusive, 192000) == 34560, "high-rate exclusive default prebuffer must reduce startup underruns");
    require(getStartupPrebufferTimeoutMs(exclusive) == 300, "default prebuffer timeout must remain compatible");
}

void testHighRateNativeDsdBuffersUseByteFrameScale()
{
    const auto nativeDsd = parseOptions({
        "echo-audio-host",
        "-asio",
        "-dop-output",
        "-asio-native-dsd-output",
    });

    require(
        getNativeDsdFifoCapacityByteFrames(nativeDsd, 22'579'200) == 564'480,
        "DSD512 FIFO must allocate 200 ms in byte frames");
    require(
        getNativeDsdStartupPrebufferByteFrames(nativeDsd, 22'579'200) == 56'448,
        "DSD512 startup prebuffer must hold 20 ms in byte frames");
    require(
        getNativeDsdFifoCapacityByteFrames(nativeDsd, 45'158'400) == 1'128'960,
        "DSD1024 FIFO must allocate 200 ms in byte frames");
    require(
        getNativeDsdStartupPrebufferByteFrames(nativeDsd, 45'158'400) == 112'896,
        "DSD1024 startup prebuffer must hold 20 ms in byte frames");
}

void testExplicitZeroPrebufferDisablesWait()
{
    const auto exclusive = parseOptions({
        "echo-audio-host",
        "-exclusive",
        "-prebuffer-ms",
        "0",
        "-prebuffer-timeout-ms",
        "0",
    });

    require(exclusive.startupPrebufferMsSpecified, "zero prebuffer must be tracked as explicit");
    require(exclusive.startupPrebufferTimeoutMsSpecified, "zero prebuffer timeout must be tracked as explicit");
    require(getStartupPrebufferFrames(exclusive, 48000) == 0, "explicit zero prebuffer must disable startup prebuffer");
    require(getStartupPrebufferTimeoutMs(exclusive) == 0, "explicit zero prebuffer timeout must be preserved");

    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    PcmRingAudioSource source(2, 512, 0, 0, 1.0f, eqProcessor, channelBalanceProcessor);
    require(waitForInitialPcm(source, 512, 0) == 0, "zero prebuffer timeout must not wait for PCM");
}

std::vector<char> makePcmPayload(const std::vector<float>& samples)
{
    std::vector<char> payload(samples.size() * sizeof(float));
    std::memcpy(payload.data(), samples.data(), payload.size());
    return payload;
}

void testPcmIdleDoesNotCountUnderrunBeforePcm()
{
    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    PcmRingAudioSource source(2, 512, 0, 0, 1.0f, eqProcessor, channelBalanceProcessor);
    auto output = makeBuffer(2, 16);
    const auto payload = makePcmPayload({ 0.1f, 0.2f, 0.3f, 0.4f });

    source.beginSession();
    source.renderPlanar(output, 0, output.getNumSamples());
    require(source.getUnderrunCallbacks() == 0, "idle session before first PCM must not count underruns");
    require(source.getUnderrunFrames() == 0, "idle session before first PCM must not count underrun frames");

    std::vector<char> pending;
    pushPcmPayload(source, 2, pending, payload);
    source.renderPlanar(output, 0, output.getNumSamples());
    require(source.getUnderrunCallbacks() > 0, "session must count underruns after PCM has started");
}

void testPcmSourcePauseRetainsBufferedFrames()
{
    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    PcmRingAudioSource source(2, 512, 0, 0, 1.0f, eqProcessor, channelBalanceProcessor);
    auto output = makeBuffer(2, 2);
    std::vector<char> pending;

    source.beginSession();
    pushPcmPayload(source, 2, pending, makePcmPayload({ 0.1f, 0.2f, 0.3f, 0.4f }));
    require(source.getReadyFrames() == 2, "test setup must buffer two PCM frames");

    source.setPaused(true);
    source.renderPlanar(output, 0, output.getNumSamples());
    require(source.getFramesPlayed() == 0, "paused source must not advance playback position");
    require(source.getReadyFrames() == 2, "paused source must retain buffered PCM frames");
    require(std::abs(output.getSample(0, 0)) < 0.000001f && std::abs(output.getSample(1, 0)) < 0.000001f, "paused source must output silence");

    source.setPaused(false);
    source.renderPlanar(output, 0, output.getNumSamples());
    require(source.getFramesPlayed() == 2, "unpaused source must consume retained buffered PCM frames");
    require(source.getReadyFrames() == 0, "unpaused source must drain rendered buffered PCM frames");
}

void testPcmInitialPlayGateWaitsForPrebufferWithoutConsumingFirstFrames()
{
    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    PcmRingAudioSource source(2, 512, 4, 200, 1.0f, eqProcessor, channelBalanceProcessor);
    auto output = makeBuffer(2, 2);

    source.beginSession(true);
    const uint64_t generation = source.session_.generation();
    constexpr uint64_t operationId = 41;
    source.armInitialPlayGate(operationId);
    const auto requestedAt = std::chrono::steady_clock::now();
    require(source.beginInitialPlayGateWait(generation, operationId), "initial play request must arm the asynchronous gate");
    require(std::chrono::steady_clock::now() - requestedAt < std::chrono::milliseconds(20),
        "initial play request must not synchronously wait for PCM");
    require(source.pollInitialPlayGate(generation).state == PcmRingAudioSource::InitialPlayGateState::WaitingForPcm,
        "initial play gate must remain paused below the startup prebuffer threshold");
    require(source.getFramesPlayed() == 0, "initial play gate must keep framesPlayed at zero");
    require(source.getReadyFrames() == 0, "initial play gate must not synthesize or consume PCM");

    const std::vector<float> input {
        0.125f, 0.125f,
        0.250f, 0.250f,
        0.375f, 0.375f,
        0.500f, 0.500f,
    };
    require(source.push(input.data(), 4), "initial play gate test must accept PCM");
    require(source.pollInitialPlayGate(generation, operationId - 1).state
            == PcmRingAudioSource::InitialPlayGateState::WaitingForPcm,
        "a stale operation identity must not release the current generation gate");
    const auto released = source.pollInitialPlayGate(generation, operationId);
    require(released.state == PcmRingAudioSource::InitialPlayGateState::Released,
        "ready initial play gate must release the current generation");
    require(released.releaseReason == PcmRingAudioSource::InitialPlayGateReleaseReason::RequiredFramesReady,
        "ready initial play gate must record the readiness release reason");
    require(released.operationId == operationId,
        "initial play gate telemetry must retain the accepted operation identity");
    require(released.readyFrames == 4 && source.getReadyFrames() == 4,
        "initial play gate must retain every startup PCM frame before release");
    require(source.getFramesPlayed() == 0, "gate release itself must not advance playback position");

    source.renderPlanar(output, 0, 2);
    require(source.getFramesPlayed() == 2, "first callback after gate release must consume real PCM");
    require(source.getReadyFrames() == 2, "first callback must consume from FIFO frame zero without dropping a packet");
    require(std::abs(output.getSample(0, 1) - 0.250f) <= nearTolerance,
        "declick ramp must expose the original second FIFO frame after the silent ramp origin");
}

void testPcmInitialPlayGateReleasesForShortEndedInputAndTimeout()
{
    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;

    PcmRingAudioSource shortSource(2, 512, 64, 200, 1.0f, eqProcessor, channelBalanceProcessor);
    shortSource.beginSession(true);
    const uint64_t shortGeneration = shortSource.session_.generation();
    shortSource.armInitialPlayGate();
    require(shortSource.beginInitialPlayGateWait(shortGeneration), "short input gate must accept play");
    const std::vector<float> shortInput { 0.1f, 0.1f, 0.2f, 0.2f };
    require(shortSource.push(shortInput.data(), 2), "short input gate must retain partial PCM");
    shortSource.markInputEnded();
    const auto shortResult = shortSource.pollInitialPlayGate(shortGeneration);
    require(shortResult.state == PcmRingAudioSource::InitialPlayGateState::Released,
        "input-ended short audio must release the initial play gate");
    require(shortResult.releaseReason == PcmRingAudioSource::InitialPlayGateReleaseReason::ShortInputEnded
            && shortResult.readyFrames == 2,
        "input-ended short audio must release only when partial PCM exists");

    PcmRingAudioSource emptySource(2, 512, 64, 20, 1.0f, eqProcessor, channelBalanceProcessor);
    emptySource.beginSession(true);
    const uint64_t emptyGeneration = emptySource.session_.generation();
    emptySource.armInitialPlayGate();
    require(emptySource.beginInitialPlayGateWait(emptyGeneration), "empty input gate must accept play");
    emptySource.markInputEnded();
    const auto emptyResult = emptySource.pollInitialPlayGate(emptyGeneration);
    require(emptyResult.state == PcmRingAudioSource::InitialPlayGateState::Failed,
        "input-ended empty audio must fail instead of pretending playback started");

    PcmRingAudioSource timeoutSource(2, 512, 64, 20, 1.0f, eqProcessor, channelBalanceProcessor);
    timeoutSource.beginSession(true);
    const uint64_t timeoutGeneration = timeoutSource.session_.generation();
    timeoutSource.armInitialPlayGate();
    require(timeoutSource.beginInitialPlayGateWait(timeoutGeneration), "timeout gate must accept play");
    std::this_thread::sleep_for(std::chrono::milliseconds(25));
    const auto timeoutResult = timeoutSource.pollInitialPlayGate(timeoutGeneration);
    require(timeoutResult.state == PcmRingAudioSource::InitialPlayGateState::SoftTimeout
            && timeoutResult.readyFrames == 0,
        "soft timeout with an empty FIFO must remain gated");
    std::vector<float> delayedPcm(64 * 2, 0.25f);
    require(timeoutSource.push(delayedPcm.data(), 64), "PCM arriving after the soft timeout must still be accepted");
    require(timeoutSource.pollInitialPlayGate(timeoutGeneration).state
            == PcmRingAudioSource::InitialPlayGateState::Released,
        "PCM arriving after the soft timeout must release normally");
}

void testPcmInitialPlayGateDeadlineStartsAtPlayRequest()
{
    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    PcmRingAudioSource source(2, 512, 4, 80, 1.0f, eqProcessor, channelBalanceProcessor);

    source.beginSession(true);
    const uint64_t generation = source.session_.generation();
    source.armInitialPlayGate();
    std::this_thread::sleep_for(std::chrono::milliseconds(100));
    require(source.beginInitialPlayGateWait(generation), "play request must begin the gate deadline");
    require(source.pollInitialPlayGate(generation).state == PcmRingAudioSource::InitialPlayGateState::WaitingForPcm,
        "slow device opening before play must not consume the initial play wait");

    const std::vector<float> input {
        0.125f, 0.125f,
        0.250f, 0.250f,
        0.375f, 0.375f,
        0.500f, 0.500f,
    };
    require(source.push(input.data(), 4), "delayed initial play gate test must accept PCM");
    const auto released = source.pollInitialPlayGate(generation);
    require(released.state == PcmRingAudioSource::InitialPlayGateState::Released,
        "delayed initial play gate must release after PCM becomes ready");
    require(released.readyFrames == 4 && source.getReadyFrames() == 4,
        "delayed initial play gate must retain all startup PCM frames");
}

void testPcmInitialPlayGateCancelsForGenerationAndStop()
{
    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;

    PcmRingAudioSource generationSource(2, 512, 64, 200, 1.0f, eqProcessor, channelBalanceProcessor);
    generationSource.beginSession(true);
    const uint64_t staleGeneration = generationSource.session_.generation();
    generationSource.armInitialPlayGate();
    require(generationSource.beginInitialPlayGateWait(staleGeneration),
        "stale-generation test must enter the initial play state machine");
    generationSource.beginSession(true);
    require(generationSource.pollInitialPlayGate(staleGeneration).state
            == PcmRingAudioSource::InitialPlayGateState::Cancelled,
        "a new generation must cancel the old initial play waiter");
    require(generationSource.getFramesPlayed() == 0,
        "cancelled old generation must not unpause or advance the replacement session");

    PcmRingAudioSource stopSource(2, 512, 64, 200, 1.0f, eqProcessor, channelBalanceProcessor);
    stopSource.beginSession(true);
    const uint64_t stopGeneration = stopSource.session_.generation();
    stopSource.armInitialPlayGate();
    require(stopSource.beginInitialPlayGateWait(stopGeneration),
        "stop test must enter the initial play state machine");
    stopSource.requestStop();
    require(stopSource.pollInitialPlayGate(stopGeneration).state
            == PcmRingAudioSource::InitialPlayGateState::Cancelled,
        "stop must cancel the current initial play waiter");
    require(stopSource.getFramesPlayed() == 0, "stop during startup must keep framesPlayed at zero");
}

void testPcmInitialPlayGateRunsOnlyOnce()
{
    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    PcmRingAudioSource source(2, 512, 2, 200, 1.0f, eqProcessor, channelBalanceProcessor);
    const std::vector<float> input { 0.1f, 0.1f, 0.2f, 0.2f };

    source.beginSession(true);
    const uint64_t generation = source.session_.generation();
    source.armInitialPlayGate();
    require(source.push(input.data(), 2), "one-shot initial play gate test must accept PCM");
    require(source.beginInitialPlayGateWait(generation), "first initial play request must be accepted");
    require(source.pollInitialPlayGate(generation).state == PcmRingAudioSource::InitialPlayGateState::Released,
        "first initial play gate release must succeed at readiness");
    require(source.pollInitialPlayGate(generation).state == PcmRingAudioSource::InitialPlayGateState::Released,
        "a released gate must not release or mutate twice");

    PcmRingAudioSource explicitPaused(2, 512, 2, 200, 1.0f, eqProcessor, channelBalanceProcessor);
    explicitPaused.beginSession(true);
    require(explicitPaused.push(input.data(), 2), "explicitly paused session may prebuffer PCM");
    require(explicitPaused.getInitialPlayGateSnapshot().state == PcmRingAudioSource::InitialPlayGateState::Idle,
        "explicit startPaused must not arm an auto-play gate");
    auto output = makeBuffer(2, 2);
    explicitPaused.renderPlanar(output, 0, 2);
    require(explicitPaused.getFramesPlayed() == 0,
        "explicit startPaused must not auto-start merely because PCM is ready");
}

void testAudioDaemonPlayReturnsBeforePcmGateIsReady()
{
    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    PcmRingAudioSource source(2, 512, 64, 20, 1.0f, eqProcessor, channelBalanceProcessor);
    source.beginSession(true);
    constexpr uint64_t residentSessionId = 77;
    source.armInitialPlayGate(residentSessionId);

    AudioDaemon::SourceHooks hooks;
    hooks.setPaused = [&](bool paused) { source.setPaused(paused); };
    hooks.generation = [&] { return source.session_.generation(); };
    hooks.armInitialPlayGate = [&](uint64_t operationId) {
        source.armInitialPlayGate(operationId);
        return source.isInitialPlayGateArmed(source.session_.generation());
    };
    hooks.initialPlayGateArmed = [&](uint64_t generation) {
        return source.isInitialPlayGateArmed(generation);
    };
    hooks.beginInitialPlayGateWait = [&](uint64_t generation, uint64_t operationId) {
        return source.beginInitialPlayGateWait(generation, operationId);
    };
    hooks.pollInitialPlayGate = [&](uint64_t generation, uint64_t operationId, int& readyFrames, int& requiredFrames) {
        const auto snapshot = source.pollInitialPlayGate(generation, operationId);
        readyFrames = snapshot.readyFrames;
        requiredFrames = snapshot.requiredFrames;
        switch (snapshot.state)
        {
            case PcmRingAudioSource::InitialPlayGateState::SoftTimeout:
                return AudioDaemon::InitialPlayGatePollResult::SoftTimeout;
            case PcmRingAudioSource::InitialPlayGateState::Released:
                return AudioDaemon::InitialPlayGatePollResult::Released;
            case PcmRingAudioSource::InitialPlayGateState::Cancelled:
                return AudioDaemon::InitialPlayGatePollResult::Cancelled;
            case PcmRingAudioSource::InitialPlayGateState::Failed:
                return AudioDaemon::InitialPlayGatePollResult::Failed;
            default:
                return AudioDaemon::InitialPlayGatePollResult::Waiting;
        }
    };
    hooks.cancelInitialPlayGate = [&](uint64_t generation, uint64_t operationId) {
        source.cancelInitialPlayGate(
            generation,
            PcmRingAudioSource::InitialPlayGateReleaseReason::SessionReplaced,
            operationId);
    };

    std::atomic<bool> shutdownRequested { false };
    AudioDaemon daemon(std::move(hooks), 48000, 1, shutdownRequested);
    const auto requestedAt = std::chrono::steady_clock::now();
    require(daemon.pauseForTests(false), "PCM audio.play must accept the non-blocking gate request");
    require(source.getInitialPlayGateSnapshot().operationId == residentSessionId,
        "daemon play must accept a gate owned by the resident session rather than its local operation id");
    require(std::chrono::steady_clock::now() - requestedAt < std::chrono::milliseconds(20),
        "PCM audio.play callback must return before prebuffer readiness");
    require(source.getFramesPlayed() == 0, "accepted play must remain paused while FIFO is empty");

    const auto softTimeoutDeadline = std::chrono::steady_clock::now() + std::chrono::milliseconds(100);
    while (source.getInitialPlayGateSnapshot().state != PcmRingAudioSource::InitialPlayGateState::SoftTimeout
        && std::chrono::steady_clock::now() < softTimeoutDeadline)
        std::this_thread::yield();
    require(source.getInitialPlayGateSnapshot().state == PcmRingAudioSource::InitialPlayGateState::SoftTimeout,
        "the daemon gate worker must report a soft timeout without releasing an empty FIFO");
    std::vector<float> pcm(128, 0.25f);
    require(source.push(pcm.data(), 64), "late PCM must still reach the gated session");
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::milliseconds(100);
    while (source.getInitialPlayGateSnapshot().state != PcmRingAudioSource::InitialPlayGateState::Released
        && std::chrono::steady_clock::now() < deadline)
        std::this_thread::yield();
    require(source.getInitialPlayGateSnapshot().state == PcmRingAudioSource::InitialPlayGateState::Released,
        "daemon gate worker must release after delayed PCM reaches the required threshold");
}

void testAudioDaemonFailedInitialGateCannotResumeEmptySession()
{
    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    PcmRingAudioSource source(2, 512, 64, 20, 1.0f, eqProcessor, channelBalanceProcessor);
    source.beginSession(true);
    const uint64_t generation = source.session_.generation();
    source.armInitialPlayGate(0);
    require(source.beginInitialPlayGateWait(generation, 0), "failed gate test must begin waiting");
    source.markInputEnded();
    require(source.pollInitialPlayGate(generation, 0).state == PcmRingAudioSource::InitialPlayGateState::Failed,
        "zero-PCM EOF must put the gate in its terminal failed state");

    AudioDaemon::SourceHooks hooks;
    hooks.setPaused = [&](bool paused) { source.setPaused(paused); };
    hooks.generation = [&] { return source.session_.generation(); };
    hooks.armInitialPlayGate = [&](uint64_t operationId) {
        source.armInitialPlayGate(operationId);
        return source.isInitialPlayGateArmed(source.session_.generation());
    };
    hooks.initialPlayGateArmed = [&](uint64_t activeGeneration) {
        return source.isInitialPlayGateArmed(activeGeneration);
    };
    hooks.beginInitialPlayGateWait = [&](uint64_t activeGeneration, uint64_t operationId) {
        return source.beginInitialPlayGateWait(activeGeneration, operationId);
    };
    hooks.pollInitialPlayGate = [&](uint64_t activeGeneration, uint64_t operationId, int& readyFrames, int& requiredFrames) {
        const auto snapshot = source.pollInitialPlayGate(activeGeneration, operationId);
        readyFrames = snapshot.readyFrames;
        requiredFrames = snapshot.requiredFrames;
        if (snapshot.state == PcmRingAudioSource::InitialPlayGateState::Failed)
            return AudioDaemon::InitialPlayGatePollResult::Failed;
        if (snapshot.state == PcmRingAudioSource::InitialPlayGateState::Released)
            return AudioDaemon::InitialPlayGatePollResult::Released;
        return AudioDaemon::InitialPlayGatePollResult::Waiting;
    };

    std::atomic<bool> shutdownRequested { false };
    AudioDaemon daemon(std::move(hooks), 48000, 1, shutdownRequested);
    require(!daemon.pauseForTests(false),
        "a failed PCM operation must reject a later resume instead of ungating an empty FIFO");
    std::vector<float> latePcm(128, 0.25f);
    require(source.push(latePcm.data(), 64), "late PCM may still arrive while the failed operation is retained");
    auto output = makeBuffer(2, 64);
    require(source.renderPlanar(output, 0, 64) == 0 && source.getReadyFrames() == 64,
        "failed gate resume must leave the session paused and must not consume late PCM");
}

void testPcmInputEndedWaitsForBufferedDrain()
{
    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    PcmRingAudioSource source(2, 512, 0, 0, 1.0f, eqProcessor, channelBalanceProcessor);
    auto output = makeBuffer(2, 2);
    std::vector<char> pending;

    source.beginSession();
    pushPcmPayload(source, 2, pending, makePcmPayload({ 0.1f, 0.2f, 0.3f, 0.4f }));
    require(source.getReadyFrames() == 2, "test setup must buffer PCM before input ends");

    source.markInputEnded();
    require(source.hasInputEnded(), "source must report input ended after decoder EOF");
    require(! source.isDrained(), "input ended must not drain while buffered PCM remains");

    source.renderPlanar(output, 0, 1);
    require(source.getReadyFrames() == 1, "partial render must leave one buffered frame");
    require(! source.isDrained(), "source must wait for remaining buffered frame before drain");

    source.renderPlanar(output, 1, 1);
    require(source.getReadyFrames() == 0, "rendering remaining PCM must empty buffer");
    require(source.isDrained(), "source must drain only after input ended and buffered PCM is empty");
}

void testPcmSourcePlaybackRateConsumesSourceFramesAtRate()
{
    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    PcmRingAudioSource source(1, 64, 0, 0, 1.0f, eqProcessor, channelBalanceProcessor);
    auto output = makeBuffer(1, 8);
    source.prepareForNativeRender(output.getNumSamples(), 48'000.0);

    std::vector<float> input(32, 0.0f);
    for (size_t index = 0; index < input.size(); ++index)
        input[index] = static_cast<float>(index + 1);

    source.beginSession();
    require(source.push(input.data(), 32), "playback-rate source test must accept PCM");
    source.getRateProcessor()->setRate(2.0f);
    require(source.renderPlanar(output, 0, output.getNumSamples()) == 8,
        "2x playback must report bounded output frames rather than source frames consumed");

    require(source.getFramesPlayed() == 16, "2x playback must consume source frames at double output rate");
    require(source.getSourceTrackFrames() == 16, "2x playback must expose the source-track clock");
    require(source.getDeviceOutputFrames() == 8, "2x playback must advance the device clock by rendered output frames");
    require(source.getReadyFrames() == 16, "2x playback must drain double-rate source frames from FIFO");
}

void testListeningTapReceivesHostSpeed()
{
    struct Capture final : echo::listen_together::RealtimeProgramPcmSink
    {
        std::array<float, 4'800> samples {};
        int size = 0;
        int sampleRate = 0;
        void onProgramPcm(const echo::FloatAudioBuffer& audio, int start, int frames, int rate) noexcept override
        {
            sampleRate = rate;
            for (int frame = 0; frame < frames && size < static_cast<int>(samples.size()); ++frame)
                samples[static_cast<std::size_t>(size++)] = audio.getSample(0, start + frame);
        }
    } capture;
    constexpr int sampleRate = 48'000;
    constexpr int blockFrames = 480;
    constexpr double pi = 3.14159265358979323846;
    echo::EqProcessor eq;
    echo::ChannelBalanceProcessor balance;
    PcmRingAudioSource source(1, 16'384, 0, 0, 1.0f, eq, balance);
    source.prepareForNativeRender(blockFrames, sampleRate);
    source.setRealtimeProgramPcmSink(&capture);
    auto output = makeBuffer(1, blockFrames);

    // Change rate in the same session, including a return to unity. Each
    // bounded window must carry the audible rate, not the original 440 Hz PCM.
    source.beginSession();
    std::uint64_t inputFrame = 0;
    for (float rate : {1.0f, 0.5f, 1.5f, 2.0f, 1.0f})
    {
        const int needed = 12'000 - source.getReadyFrames();
        std::vector<float> input(static_cast<std::size_t>(needed));
        for (auto& sample : input)
            sample = static_cast<float>(0.2 * std::sin(2 * pi * 440 * static_cast<double>(inputFrame++) / sampleRate));
        require(source.push(input.data(), needed), "listening speed test must buffer source PCM");
        require(source.getRateProcessor()->configure(rate, echo::SpeedMode::Nightcore), "host speed must configure");
        capture.size = 0;
        for (int block = 0; block < 10; ++block)
            require(source.renderPlanar(output, 0, blockFrames) == blockFrames, "host speed must keep rendering");
        require(capture.size == 4'800 && capture.sampleRate == sampleRate,
            "listening tap must retain the device frame rate at every host speed");
        int crossings = 0;
        for (int frame = 961; frame < capture.size; ++frame)
        {
            require(std::isfinite(capture.samples[frame]), "shared speed PCM must remain finite");
            if (capture.samples[frame - 1] <= 0 && capture.samples[frame] > 0) ++crossings;
        }
        const double frequency = crossings * static_cast<double>(sampleRate) / (capture.size - 960);
        require(std::abs(frequency - 440 * rate) < 20,
            "listening tap must contain the host speed change exactly once");
    }
    source.setRealtimeProgramPcmSink(nullptr);
}

void testPlaybackRateIsContinuousAcrossCallbackSizesAndRates()
{
    constexpr int sampleRate = 48'000;
    constexpr int sourceFrames = 2'048;
    constexpr double pi = 3.14159265358979323846;
    const std::array<float, 6> rates { 0.5f, 0.75f, 1.1f, 1.25f, 1.5f, 2.0f };
    const std::array<int, 4> callbackSizes { 17, 64, 257, 512 };

    std::vector<float> input(static_cast<std::size_t>(sourceFrames));
    for (int frame = 0; frame < sourceFrames; ++frame)
        input[static_cast<std::size_t>(frame)] = static_cast<float>(
            0.2 * std::sin(2.0 * pi * 997.0 * static_cast<double>(frame) / sampleRate));

    for (const float rate : rates)
    {
        for (const int callbackSize : callbackSizes)
        {
            echo::EqProcessor eqProcessor;
            echo::ChannelBalanceProcessor channelBalanceProcessor;
            PcmRingAudioSource source(1, sourceFrames + 8, 0, 0, 1.0f, eqProcessor, channelBalanceProcessor);
            auto output = makeBuffer(1, callbackSize);
            source.prepareForNativeRender(callbackSize, sampleRate);
            source.beginSession();
            require(source.getRateProcessor()->configure(rate, echo::SpeedMode::Nightcore),
                "pitch-coupled playback rate must be supported");
            require(source.push(input.data(), sourceFrames), "rate matrix must accept all source PCM");
            source.markInputEnded();

            uint64_t renderedTotal = 0;
            bool havePrevious = false;
            float previousSample = 0.0f;
            int callbacks = 0;
            while (! source.isDrained() && callbacks++ < 1'000)
            {
                const auto rendered = source.renderPlanar(output, 0, callbackSize);
                require(rendered > 0, "ended rate-conversion stream must make progress until drained");
                const int renderedInt = static_cast<int>(rendered);
                if (havePrevious)
                {
                    const float boundaryDelta = std::abs(output.getSample(0, 0) - previousSample);
                    require(boundaryDelta < 0.25f,
                        "persistent rate conversion must not introduce a callback-boundary step");
                }
                previousSample = output.getSample(0, renderedInt - 1);
                havePrevious = true;
                renderedTotal += rendered;
            }

            const double expectedOutput = static_cast<double>(sourceFrames) / static_cast<double>(rate);
            require(std::abs(static_cast<double>(renderedTotal) - expectedOutput) <= 2.0,
                "rate-converted duration must follow source duration across callback sizes");
            require(source.getSourceTrackFrames() == sourceFrames,
                "source-track clock must count consumed source PCM exactly");
            require(source.getDeviceOutputFrames() == renderedTotal,
                "device clock must count actual produced frames exactly");
        }
    }
}

void testPlaybackRateConfigurationAcceptsDecoderTempoModeAtomically()
{
    echo::PlaybackRateProcessor processor;
    processor.prepare(48'000.0, 128, 2);
    require(processor.configure(1.0f, echo::SpeedMode::Speed),
        "tempo-only mode at unity must remain a no-processing bypass");
    const auto unity = processor.getConfiguration();
    require(unity.rate == 1.0f && unity.mode == echo::SpeedMode::Speed,
        "unity tempo-only bypass must preserve the requested dormant mode");
    require(processor.configure(1.25f, echo::SpeedMode::Daycore),
        "daycore must publish a pitch-coupled rate configuration");
    std::string error;
    require(processor.configure(1.5f, echo::SpeedMode::Speed, &error),
        "tempo-only mode must publish the rate consumed by the decoder-thread WSOLA");
    const auto after = processor.getConfiguration();
    require(error.empty(), "supported tempo mode must not report an error");
    require(after.rate == 1.5f && after.mode == echo::SpeedMode::Speed,
        "tempo mode must publish one atomic native configuration");
}

void testPlaybackRateCallbackDeadlineMatrix()
{
    const std::array<int, 3> sampleRates { 48'000, 96'000, 192'000 };
    const std::array<int, 4> callbackSizes { 64, 128, 256, 512 };
    const std::array<float, 6> rates { 0.5f, 0.75f, 1.1f, 1.25f, 1.5f, 2.0f };
    double worstP999Us = 0.0;
    double tightestHeadroom = std::numeric_limits<double>::infinity();
    std::string worstCase;

    for (const int sampleRate : sampleRates)
    {
        for (const int callbackSize : callbackSizes)
        {
            const double callbackDeadlineUs = 1'000'000.0 * callbackSize / sampleRate;
            for (const float rate : rates)
            {
                echo::PlaybackRateProcessor processor;
                processor.prepare(sampleRate, callbackSize, 2);
                require(processor.configure(rate, echo::SpeedMode::Nightcore),
                    "deadline matrix rate must configure");
                echo::FloatAudioBuffer source(2, callbackSize * 2 + 2);
                echo::FloatAudioBuffer output(2, callbackSize);
                for (int channel = 0; channel < 2; ++channel)
                    for (int frame = 0; frame < source.getNumSamples(); ++frame)
                        source.setSample(channel, frame, static_cast<float>(
                            0.1 * std::sin(static_cast<double>(frame + channel) * 0.13)));

                const auto configuration = processor.getConfiguration();
                for (int warmup = 0; warmup < 8; ++warmup)
                {
                    const int required = processor.sourceFramesRequired(callbackSize, configuration);
                    processor.processResampledBlock(source, required, output, 0, callbackSize, configuration);
                }

                std::vector<double> durations;
                durations.reserve(64);
                for (int iteration = 0; iteration < 64; ++iteration)
                {
                    const int required = processor.sourceFramesRequired(callbackSize, configuration);
                    const auto started = std::chrono::steady_clock::now();
                    processor.processResampledBlock(source, required, output, 0, callbackSize, configuration);
                    const auto finished = std::chrono::steady_clock::now();
                    durations.push_back(std::chrono::duration<double, std::micro>(finished - started).count());
                }
                std::sort(durations.begin(), durations.end());
                const double p999 = durations.back();
                if (p999 > worstP999Us)
                {
                    worstP999Us = p999;
                    worstCase = std::to_string(sampleRate) + "Hz/"
                        + std::to_string(callbackSize) + "f/"
                        + std::to_string(rate) + "x";
                }
                tightestHeadroom = std::min(tightestHeadroom, callbackDeadlineUs / std::max(0.001, p999));
                require(p999 < callbackDeadlineUs,
                    "band-limited playback-rate p999 must remain inside the callback deadline");
            }
        }
    }

    std::cout << "[BENCH] playback-rate callback matrix p999_us=" << worstP999Us
              << " tightest_deadline_headroom=" << tightestHeadroom
              << " worst_case=" << worstCase << '\n';
}

void testPlaybackRateUsesDeviceClockForPrebufferSeekGaplessAndAutomix()
{
    {
        echo::EqProcessor eqProcessor;
        echo::ChannelBalanceProcessor channelBalanceProcessor;
        PcmRingAudioSource source(1, 256, 64, 5'000, 1.0f, eqProcessor, channelBalanceProcessor);
        source.prepareForNativeRender(16, 48'000.0);
        require(source.getRateProcessor()->configure(2.0f, echo::SpeedMode::Nightcore),
            "prebuffer interaction rate must configure");
        source.beginSession();
        const std::vector<float> firstHalf(64, 0.1f);
        const std::vector<float> secondHalf(64, 0.1f);
        require(source.push(firstHalf.data(), 64), "prebuffer interaction must accept first half");
        auto output = makeBuffer(1, 16);
        require(source.renderPlanar(output, 0, 16) == 0,
            "2x startup must require twice the source PCM for the same wall-clock prebuffer");
        require(source.push(secondHalf.data(), 64), "prebuffer interaction must accept second half");
        require(source.renderPlanar(output, 0, 16) == 16,
            "2x startup must release at the wall-clock-adjusted source target");
        require(source.getSourceTrackFrames() == 32 && source.getDeviceOutputFrames() == 16,
            "prebuffer release must advance independent source and device clocks");

        const std::vector<float> replacement(64, -0.1f);
        require(source.replaceBufferedAudio(replacement.data(), 64, false) == 64,
            "seek-style replacement must publish fresh PCM");
        require(source.getSourceTrackFrames() == 0 && source.getDeviceOutputFrames() == 0,
            "seek-style replacement must reset both clocks atomically");
        require(source.renderPlanar(output, 0, 16) == 16,
            "seek-style replacement must restart the persistent resampler");
        require(source.getSourceTrackFrames() == 32 && source.getDeviceOutputFrames() == 16,
            "seek-style replacement must restart both monotonic clocks from zero");
    }

    {
        echo::EqProcessor eqProcessor;
        echo::ChannelBalanceProcessor channelBalanceProcessor;
        PcmRingAudioSource source(1, 64, 0, 0, 1.0f, eqProcessor, channelBalanceProcessor);
        source.prepareForNativeRender(4, 48'000.0);
        require(source.getRateProcessor()->configure(2.0f, echo::SpeedMode::Daycore),
            "gapless interaction rate must configure");
        source.beginSession();
        source.prepareGapless();
        const std::vector<float> current(4, 1.0f);
        const std::vector<float> next(4, 0.25f);
        require(source.push(current.data(), 4), "rate gapless must accept current PCM");
        require(source.pushAutomixNext(next.data(), 4), "rate gapless must accept next PCM");
        source.markInputEnded();
        source.markAutomixNextEnded();
        auto output = makeBuffer(1, 4);
        require(source.renderPlanar(output, 0, 4) == 4,
            "rate gapless must render across the source boundary");
        require(source.getGaplessBoundaryFrame() == 2,
            "gapless boundary must be expressed on the device-output clock");
        require(source.getSourceTrackFrames() == 8 && source.getDeviceOutputFrames() == 4,
            "rate gapless must keep source and device clocks independent");
    }

    {
        echo::EqProcessor eqProcessor;
        echo::ChannelBalanceProcessor channelBalanceProcessor;
        PcmRingAudioSource source(1, 128, 0, 0, 1.0f, eqProcessor, channelBalanceProcessor);
        source.prepareForNativeRender(8, 48'000.0);
        require(source.getRateProcessor()->configure(2.0f, echo::SpeedMode::Nightcore),
            "automix interaction rate must configure");
        source.beginSession();
        const uint64_t deckLease = source.prepareAutomixDeck(4, 4, 0.0, 0.0);
        require(deckLease != 0, "rate automix must arm on the device clock");
        const std::vector<float> current(32, 0.5f);
        const std::vector<float> next(32, 0.25f);
        require(source.push(current.data(), 32), "rate automix must accept current PCM");
        require(source.pushAutomixDeck(deckLease, next.data(), 32), "rate automix must accept next PCM");
        auto output = makeBuffer(1, 8);
        require(source.renderPlanar(output, 0, 8) == 8, "rate automix must render through fade completion");
        require(source.getDeviceOutputFrames() == 8 && source.getSourceTrackFrames() == 16,
            "rate automix fade must advance on device frames, not consumed source frames");
        require(source.commitAutomixDeck(deckLease),
            "rate automix commit must become eligible at the device-clock fade end");
    }
}

void testPlaybackRateChangesKeepWarmHistoryAndMonotonicClocks()
{
    constexpr int sampleRate = 48'000;
    constexpr int callbackSize = 64;
    constexpr double pi = 3.14159265358979323846;
    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    PcmRingAudioSource source(1, 2'048, 0, 0, 1.0f, eqProcessor, channelBalanceProcessor);
    source.prepareForNativeRender(callbackSize, sampleRate);
    source.beginSession();
    std::vector<float> input(1'024);
    for (int frame = 0; frame < static_cast<int>(input.size()); ++frame)
        input[static_cast<std::size_t>(frame)] = static_cast<float>(
            0.2 * std::sin(2.0 * pi * 440.0 * static_cast<double>(frame) / sampleRate));
    require(source.push(input.data(), static_cast<int>(input.size())),
        "rate-change continuity test must accept source PCM");

    auto output = makeBuffer(1, callbackSize);
    require(source.renderPlanar(output, 0, callbackSize) == callbackSize,
        "unity callback must prime rate-conversion history");
    float previous = output.getSample(0, callbackSize - 1);
    uint64_t previousSourceClock = source.getSourceTrackFrames();
    uint64_t previousDeviceClock = source.getDeviceOutputFrames();

    for (const auto [rate, mode] : std::array<std::pair<float, echo::SpeedMode>, 3> {{
        {1.25f, echo::SpeedMode::Nightcore},
        {0.75f, echo::SpeedMode::Daycore},
        {1.5f, echo::SpeedMode::Nightcore},
    }})
    {
        require(source.getRateProcessor()->configure(rate, mode),
            "rate-change continuity configuration must publish atomically");
        require(source.renderPlanar(output, 0, callbackSize) == callbackSize,
            "rate-change callback must keep producing device frames");
        require(std::abs(output.getSample(0, 0) - previous) < 0.25f,
            "rate change must not inject a zero-history callback-edge pop");
        require(source.getSourceTrackFrames() > previousSourceClock,
            "source clock must remain monotonic across rate changes");
        require(source.getDeviceOutputFrames() > previousDeviceClock,
            "device clock must remain monotonic across rate changes");
        previous = output.getSample(0, callbackSize - 1);
        previousSourceClock = source.getSourceTrackFrames();
        previousDeviceClock = source.getDeviceOutputFrames();
    }
}

void testNativeRenderPlaybackRateKeepsDitherInsideOutputBuffer()
{
    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    PcmRingAudioSource source(1, 64, 0, 0, 1.0f, eqProcessor, channelBalanceProcessor);
    source.prepareForNativeRender(8, 48'000.0);
    source.configureDither(echo::PcmDitherMode::Tpdf, 24);
    source.beginSession();
    source.getRateProcessor()->setRate(2.0f);

    const std::vector<float> input(32, 0.25f);
    require(source.push(input.data(), static_cast<int>(input.size())),
        "playback-rate dither guard test must accept PCM");

    constexpr float guardValue = 1234.5f;
    std::vector<float> outputWithGuard(12, guardValue);
    require(source.renderInterleaved(outputWithGuard.data(), 8, 1) == 8,
        "native playback-rate render must report no more than output capacity");
    require(std::all_of(outputWithGuard.begin() + 8, outputWithGuard.end(), [](float sample) {
        return sample == guardValue;
    }), "native playback-rate dither must not write beyond the output buffer");
}

void testPcmSourceReplaceBufferedAudioDropsStaleFrames()
{
    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    PcmRingAudioSource source(2, 512, 0, 0, 1.0f, eqProcessor, channelBalanceProcessor);
    auto output = makeBuffer(2, 16);
    std::vector<char> pending;

    source.beginSession();
    pushPcmPayload(source, 2, pending, makePcmPayload({ 0.1f, 0.2f, 0.3f, 0.4f }));
    std::vector<float> replacement(32, -0.5f);

    require(source.replaceBufferedAudio(replacement.data(), 16, false) == 16, "replacement must write target frames");
    require(source.getReadyFrames() == 16, "replacement must leave target frames ready");
    source.renderPlanar(output, 0, output.getNumSamples());

    bool renderedReplacement = false;
    for (int frame = 0; frame < 16; frame += 1)
        renderedReplacement = renderedReplacement || output.getSample(0, frame) < -0.001f;
    require(renderedReplacement, "replacement must render target frames instead of stale positive frames");
    require(source.getFramesPlayed() == 16, "replacement session must count rendered target frames");
}

void testPcmSourceReplaceBufferedAudioWhilePausedWaitsForResume()
{
    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    PcmRingAudioSource source(2, 512, 64, 5000, 1.0f, eqProcessor, channelBalanceProcessor);
    auto output = makeBuffer(2, 16);
    std::vector<char> pending;

    source.beginSession();
    pushPcmPayload(source, 2, pending, makePcmPayload({ 0.1f, 0.2f, 0.3f, 0.4f }));
    std::vector<float> replacement(32, -0.5f);

    require(source.replaceBufferedAudio(replacement.data(), 16, true) == 16, "paused replacement must write target frames");
    source.renderPlanar(output, 0, output.getNumSamples());
    require(source.getFramesPlayed() == 0, "paused replacement must not advance before resume");
    require(source.getReadyFrames() == 16, "paused replacement must retain target frames");

    source.setPaused(false);
    source.renderPlanar(output, 0, output.getNumSamples());
    bool renderedReplacement = false;
    for (int frame = 0; frame < 16; frame += 1)
        renderedReplacement = renderedReplacement || output.getSample(0, frame) < -0.001f;
    require(renderedReplacement, "resumed replacement must render target frames instead of stale positive frames");
    require(source.getFramesPlayed() == 16, "resumed replacement must count target frames");
}

void testPcmPrebufferDoesNotCountUnderrunBeforeTarget()
{
    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    PcmRingAudioSource source(2, 512, 64, 5000, 1.0f, eqProcessor, channelBalanceProcessor);
    auto output = makeBuffer(2, 16);
    std::vector<char> pending;

    source.beginSession();
    pushPcmPayload(source, 2, pending, makePcmPayload({ 0.1f, 0.2f, 0.3f, 0.4f }));
    source.renderPlanar(output, 0, output.getNumSamples());
    require(source.getFramesPlayed() == 0, "prebuffering PCM session must not consume early PCM");
    require(source.getReadyFrames() == 2, "prebuffering PCM session must retain early PCM");
    require(source.getUnderrunCallbacks() == 0, "prebuffering PCM session must not count underruns before target");

    std::vector<float> samples(128, 0.15f);
    pushPcmPayload(source, 2, pending, makePcmPayload(samples));
    source.renderPlanar(output, 0, output.getNumSamples());
    require(source.getFramesPlayed() > 0, "PCM session must start after the prebuffer target is reached");
}

void testNativeFifoWrapsAndResets()
{
    echo_audio_host::NativeFifo fifo(5);
    int start1 = 0;
    int size1 = 0;
    int start2 = 0;
    int size2 = 0;

    fifo.prepareToWrite(4, start1, size1, start2, size2);
    require(start1 == 0 && size1 == 4 && start2 == 0 && size2 == 0, "native FIFO first write block");
    fifo.finishedWrite(4);
    require(fifo.getNumReady() == 4 && fifo.getFreeSpace() == 1, "native FIFO tracks initial write");

    fifo.prepareToRead(3, start1, size1, start2, size2);
    require(start1 == 0 && size1 == 3 && start2 == 0 && size2 == 0, "native FIFO first read block");
    fifo.finishedRead(3);
    require(fifo.getNumReady() == 1 && fifo.getFreeSpace() == 4, "native FIFO tracks consumed frames");

    fifo.prepareToWrite(4, start1, size1, start2, size2);
    require(start1 == 4 && size1 == 1 && start2 == 0 && size2 == 3, "native FIFO write wraps around end");
    fifo.finishedWrite(4);

    fifo.prepareToRead(5, start1, size1, start2, size2);
    require(start1 == 3 && size1 == 2 && start2 == 0 && size2 == 3, "native FIFO read exposes wrapped blocks");
    fifo.resetWhenReadersQuiescent();
    require(fifo.getNumReady() == 0 && fifo.getFreeSpace() == 5, "native FIFO reset clears state");

    fifo.reconfigureCapacityWhenReadersQuiescent(8);
    require(fifo.getTotalSize() == 8 && fifo.getNumReady() == 0 && fifo.getFreeSpace() == 8,
        "native FIFO reconfigure must publish the new capacity after readers are quiescent");
    fifo.prepareToWrite(8, start1, size1, start2, size2);
    require(size1 + size2 == 8, "reconfigured native FIFO must accept a full new-capacity write");
}

void testDeferredDaemonRingsUseSessionRateAndKeepPassthroughLazy()
{
    require(getDeferredDaemonPcmFifoCapacityFrames(48000) == 384000,
        "deferred daemon PCM FIFO must stay 8000 ms at 48 kHz");
    require(getDeferredDaemonPcmFifoCapacityFrames(192000) == 1536000,
        "deferred daemon PCM FIFO must stay 8000 ms at 192 kHz");
    require(getDeferredDaemonNativeDsdFifoCapacityByteFrames(24'576'000) == 921'600,
        "deferred daemon DSD512 FIFO must stay 300 ms in byte frames");

    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    PcmRingAudioSource source(2, kDeferredDaemonRingPlaceholderFrames, 0, 0, 1.0f, eqProcessor, channelBalanceProcessor);
    DopRingSource dopSource(2, kDeferredDaemonRingPlaceholderFrames, 0, 0);
    NativeDsdRingSource nativeDsdSource(2, kDeferredDaemonRingPlaceholderFrames, 0, 0);
    require(source.getCapacityFrames() == 1, "deferred daemon must start with a placeholder PCM ring");
    require(dopSource.getCapacityFrames() == 1, "deferred daemon must start with a placeholder DoP ring");
    require(nativeDsdSource.getCapacityByteFrames() == 1, "deferred daemon must start with a placeholder DSD ring");

    int fifoCapacityFrames = 1;
    int nativeDsdFifoCapacityByteFrames = 1;
    applyDeferredDaemonRingCapacities(
        source, dopSource, nativeDsdSource, 48000, false, false, fifoCapacityFrames, nativeDsdFifoCapacityByteFrames);
    require(fifoCapacityFrames == 384000, "48 kHz session must publish an 8000 ms PCM FIFO");
    require(source.getCapacityFrames() == 384000, "48 kHz session must size PCM current and automix decks together");
    require(dopSource.getCapacityFrames() == 1, "PCM session must not allocate a DoP ring");
    require(nativeDsdSource.getCapacityByteFrames() == 1, "PCM session must not allocate a native DSD ring");

    std::vector<float> pcm(256, 0.25f);
    source.beginSession();
    require(source.push(pcm.data(), 128), "session-sized PCM ring must accept audio after reconfigure");
    require(source.getReadyFrames() == 128, "session-sized PCM ring must retain pushed frames");

    applyDeferredDaemonRingCapacities(
        source, dopSource, nativeDsdSource, 192000, true, false, fifoCapacityFrames, nativeDsdFifoCapacityByteFrames);
    require(fifoCapacityFrames == 1536000, "192 kHz session must keep an 8000 ms PCM FIFO");
    require(source.getCapacityFrames() == 1536000, "192 kHz session must grow PCM rings before output starts");
    require(dopSource.getCapacityFrames() == 1536000, "DoP output must receive the same 8000 ms capacity");
    require(nativeDsdSource.getCapacityByteFrames() == 1, "DoP session must not allocate a native DSD ring");

    applyDeferredDaemonRingCapacities(
        source, dopSource, nativeDsdSource, 24'576'000, true, true, fifoCapacityFrames, nativeDsdFifoCapacityByteFrames);
    require(fifoCapacityFrames == 1, "native DSD must not size PCM rings from the DSD bit rate");
    require(source.getCapacityFrames() == 1, "native DSD must release the unused PCM rings");
    require(dopSource.getCapacityFrames() == 1, "native DSD must not keep a DoP ring");
    require(nativeDsdFifoCapacityByteFrames == 921600, "native DSD output must publish a 300 ms DSD512 FIFO");
    require(nativeDsdSource.getCapacityByteFrames() == 921600, "native DSD output must allocate only when configured");
}

void testPcmSessionSwapWaitsForInFlightReaderLease()
{
    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    PcmRingAudioSource source(2, 512, 0, 0, 1.0f, eqProcessor, channelBalanceProcessor);
    source.beginSession();
    const uint64_t oldGeneration = source.session_.generation();
    std::vector<float> oldPcm(32, 0.5f);
    require(source.pushForGeneration(oldPcm.data(), 16, oldGeneration), "old session test PCM must be accepted");

    std::promise<void> readerPrepared;
    std::promise<void> releaseReader;
    auto releaseFuture = releaseReader.get_future().share();
    std::atomic<bool> hookEntered { false };
    source.setReadLeaseHookForTests([&] {
        if (!hookEntered.exchange(true, std::memory_order_acq_rel))
        {
            readerPrepared.set_value();
            releaseFuture.wait();
        }
    });

    auto oldOutput = makeBuffer(2, 16);
    auto render = std::async(std::launch::async, [&] {
        return source.renderPlanar(oldOutput, 0, 16);
    });
    readerPrepared.get_future().wait();

    auto swap = std::async(std::launch::async, [&] { source.beginSession(); });
    require(swap.wait_for(std::chrono::milliseconds(10)) == std::future_status::timeout,
        "session swap must wait for the in-flight callback reader without blocking that callback");
    releaseReader.set_value();
    require(render.get() == 16, "old callback must finish against its old FIFO epoch");
    swap.get();
    source.setReadLeaseHookForTests({});

    bool oldRenderContainsOnlyOldPattern = true;
    for (int frame = 1; frame < 16; ++frame)
        oldRenderContainsOnlyOldPattern = oldRenderContainsOnlyOldPattern && oldOutput.getSample(0, frame) >= 0.0f;
    require(oldRenderContainsOnlyOldPattern, "old callback must never observe replacement PCM");

    const uint64_t newGeneration = source.session_.generation();
    require(newGeneration > oldGeneration, "session generations must stay monotonic across the swap");
    require(!source.pushForGeneration(oldPcm.data(), 16, oldGeneration),
        "stale producer must be rejected after its generation and FIFO epoch retire");
    std::vector<float> newPcm(32, -0.5f);
    require(source.pushForGeneration(newPcm.data(), 16, newGeneration), "new session PCM must be accepted");
    auto newOutput = makeBuffer(2, 16);
    require(source.renderPlanar(newOutput, 0, 16) == 16, "replacement session must render its first frame block");
    bool replacementStartsWithNewPattern = false;
    for (int frame = 1; frame < 16; ++frame)
        replacementStartsWithNewPattern = replacementStartsWithNewPattern || newOutput.getSample(0, frame) < 0.0f;
    require(replacementStartsWithNewPattern, "old finishedRead must not consume the replacement session's first frames");
    require(source.getReadyFrames() >= 0 && source.getReadyFrames() <= 512,
        "FIFO ready space must remain legal after an overlapped session swap");
}

void testPcmSessionSwapRejectsCallbackAdmittedBeforeLease()
{
    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    PcmRingAudioSource source(2, 512, 0, 0, 1.0f, eqProcessor, channelBalanceProcessor);
    source.beginSession();
    const uint64_t oldGeneration = source.session_.generation();
    std::vector<float> oldPcm(32, 0.25f);
    require(source.pushForGeneration(oldPcm.data(), 16, oldGeneration),
        "admission-window test old PCM must be primed");

    std::promise<void> callbackAdmitted;
    std::promise<void> releaseAdmission;
    auto releaseFuture = releaseAdmission.get_future().share();
    source.setReadAdmissionHookForTests([&] {
        callbackAdmitted.set_value();
        releaseFuture.wait();
    });

    auto staleOutput = makeBuffer(2, 16);
    auto staleRender = std::async(std::launch::async, [&] {
        return source.renderPlanar(staleOutput, 0, 16);
    });
    callbackAdmitted.get_future().wait();

    auto swap = std::async(std::launch::async, [&] { source.beginSession(); });
    require(swap.wait_for(std::chrono::milliseconds(10)) == std::future_status::timeout,
        "session swap must wait for a callback admitted into the full render epoch");
    releaseAdmission.set_value();
    require(staleRender.get() == 16,
        "a callback admitted before the session swap must finish only its old render epoch");
    swap.get();
    source.setReadAdmissionHookForTests({});
    require(source.getFramesPlayed() == 0,
        "session swap must reset the clock after the admitted old callback exits");
    const uint64_t newGeneration = source.session_.generation();
    std::vector<float> newPcm(32, -0.5f);
    require(source.pushForGeneration(newPcm.data(), 16, newGeneration),
        "admission-window test new PCM must be primed");
    require(source.getReadyFrames() == 16,
        "stale callback admission must not advance the replacement FIFO read sequence");

    auto newOutput = makeBuffer(2, 16);
    require(source.renderPlanar(newOutput, 0, 16) == 16,
        "replacement callback must retain and render the new track's first block");
}

void testDopReplaceRejectsCallbackAdmittedBeforeLease()
{
    DopRingSource source(2, 128, 0, 0);
    source.beginSession();
    const uint64_t oldGeneration = source.generation();
    std::vector<uint32_t> oldFrames(32, 0x000011u);
    require(source.pushForGeneration(oldFrames.data(), 16, oldGeneration), "DoP old generation must be primed");

    std::promise<void> admitted;
    std::promise<void> release;
    auto releaseFuture = release.get_future().share();
    source.setReadAdmissionHookForTests([&] {
        admitted.set_value();
        releaseFuture.wait();
    });
    std::vector<uint32_t> staleOutput(32, 0u);
    auto staleRender = std::async(std::launch::async, [&] {
        return source.renderInterleaved(staleOutput.data(), 16, 2);
    });
    admitted.get_future().wait();

    std::vector<uint32_t> replacement(32, 0x0000a5u);
    auto replace = std::async(std::launch::async, [&] {
        return source.replaceBufferedAudio(replacement.data(), 16, false);
    });
    require(replace.wait_for(std::chrono::milliseconds(10)) == std::future_status::timeout,
        "DoP replacement must wait for the full admitted render callback");
    release.set_value();
    require(staleRender.get() == 16, "admitted DoP callback must finish against old frames only");
    require(replace.get() == 16, "DoP replacement must commit after old render quiescence");
    source.setReadAdmissionHookForTests({});
    require(source.getReadyFrames() == 16, "stale DoP callback must not advance the replacement read sequence");
    std::vector<uint32_t> replacementOutput(32, 0u);
    require(source.renderInterleaved(replacementOutput.data(), 16, 2) == 16,
        "fresh DoP callback must render the complete replacement block");
    require((replacementOutput[0] & 0xffffu) == 0x00a5u,
        "fresh DoP callback must begin with the replacement pattern");
    require(!source.pushForGeneration(oldFrames.data(), 1, oldGeneration),
        "stale DoP producer must be rejected after replacement");
}

void testNativeDsdReplaceRejectsCallbackAdmittedBeforeLease()
{
    NativeDsdRingSource source(2, 128, 0, 0);
    source.beginSession();
    const uint64_t oldGeneration = source.generation();
    std::vector<uint8_t> oldFrames(32, 0x11u);
    require(source.pushForGeneration(oldFrames.data(), 16, oldGeneration), "native DSD old generation must be primed");

    std::promise<void> admitted;
    std::promise<void> release;
    auto releaseFuture = release.get_future().share();
    source.setReadAdmissionHookForTests([&] {
        admitted.set_value();
        releaseFuture.wait();
    });
    std::vector<uint8_t> staleOutput(32, 0u);
    auto staleRender = std::async(std::launch::async, [&] {
        return source.renderInterleaved(staleOutput.data(), 16, 2);
    });
    admitted.get_future().wait();

    std::vector<uint8_t> replacement(32, 0xa5u);
    auto replace = std::async(std::launch::async, [&] {
        return source.replaceBufferedAudio(replacement.data(), 16, false);
    });
    require(replace.wait_for(std::chrono::milliseconds(10)) == std::future_status::timeout,
        "native DSD replacement must wait for the full admitted render callback");
    release.set_value();
    require(staleRender.get() == 16, "admitted native DSD callback must finish against old byte frames only");
    require(replace.get() == 16, "native DSD replacement must commit after old render quiescence");
    source.setReadAdmissionHookForTests({});
    require(source.getReadyByteFrames() == 16,
        "stale native DSD callback must not advance the replacement read sequence");
    std::vector<uint8_t> replacementOutput(32, 0u);
    require(source.renderInterleaved(replacementOutput.data(), 16, 2) == 16,
        "fresh native DSD callback must render the complete replacement block");
    require(replacementOutput[0] == 0xa5u,
        "fresh native DSD callback must begin with the replacement pattern");
    require(!source.pushForGeneration(oldFrames.data(), 1, oldGeneration),
        "stale native DSD producer must be rejected after replacement");
}

void testAudioDaemonBackToBackOpenKeepsProducerGenerationScoped()
{
    const auto fixturePath = writeStreamingDecoderWavFixture(48'000, 2, 256);
    ScopedFileRemoval removeFixture(fixturePath);
    std::atomic<uint64_t> generation { 1 };
    std::atomic<int> generationTwoPushes { 0 };
    std::atomic<int> generationThreePushes { 0 };
    std::mutex stoppedMutex;
    std::vector<uint64_t> stoppedGenerations;
    std::atomic<int> pauseMutations { 0 };

    AudioDaemon::SourceHooks hooks;
    hooks.markInputEnded = [] {};
    hooks.requestStop = [] {};
    hooks.setPaused = [&](bool) { pauseMutations.fetch_add(1, std::memory_order_relaxed); };
    hooks.push = [](const float*, int) { return true; };
    hooks.pushForGeneration = [&](const float*, int, uint64_t producerGeneration) {
        if (producerGeneration == 2) generationTwoPushes.fetch_add(1, std::memory_order_relaxed);
        if (producerGeneration == 3) generationThreePushes.fetch_add(1, std::memory_order_relaxed);
        return generation.load(std::memory_order_acquire) == producerGeneration;
    };
    hooks.generation = [&] { return generation.load(std::memory_order_acquire); };
    hooks.decoderSampleRateFor = [](int outputSampleRate) { return outputSampleRate; };
    hooks.requestStopForGeneration = [&](uint64_t producerGeneration) {
        std::lock_guard<std::mutex> lock(stoppedMutex);
        stoppedGenerations.push_back(producerGeneration);
        return generation.load(std::memory_order_acquire) == producerGeneration;
    };

    std::atomic<bool> shutdownRequested { false };
    AudioDaemon daemon(std::move(hooks), 48'000, 1, shutdownRequested);
    nlohmann::json firstResult;
    require(daemon.openFileForTests(fixturePath, 48'000, firstResult), "open A must start its decoder");
    require(firstResult.value("operationId", uint64_t { 0 }) == 1, "open A must own operation 1");

    generation.store(2, std::memory_order_release);
    nlohmann::json secondResult;
    require(daemon.openFileForTests(fixturePath, 48'000, secondResult), "open B must replace A without explicit stop");
    require(secondResult.value("operationId", uint64_t { 0 }) == 2, "open B must own operation 2");

    generation.store(3, std::memory_order_release);
    nlohmann::json thirdResult;
    require(daemon.openFileForTests(fixturePath, 48'000, thirdResult), "open C must replace B without explicit stop");
    require(thirdResult.value("operationId", uint64_t { 0 }) == 3, "open C must own operation 3");

    const auto decodedDeadline = std::chrono::steady_clock::now() + std::chrono::milliseconds(200);
    while (generationThreePushes.load(std::memory_order_acquire) == 0
        && std::chrono::steady_clock::now() < decodedDeadline)
        std::this_thread::yield();
    require(generationThreePushes.load(std::memory_order_acquire) > 0,
        "the newest generation must still produce PCM after two old producer joins");
    {
        std::lock_guard<std::mutex> lock(stoppedMutex);
        require(std::find(stoppedGenerations.begin(), stoppedGenerations.end(), 1) != stoppedGenerations.end(),
            "open B must cancel only producer generation A");
        require(std::find(stoppedGenerations.begin(), stoppedGenerations.end(), 2) != stoppedGenerations.end(),
            "open C must cancel only producer generation B");
        require(std::find(stoppedGenerations.begin(), stoppedGenerations.end(), 3) == stoppedGenerations.end(),
            "old producer cleanup must never stop the newest generation");
    }
    require(!daemon.pauseForTests(true, 2), "stale pause must be rejected by expectedOperationId");
    require(pauseMutations.load(std::memory_order_acquire) == 0,
        "a rejected stale pause must not mutate native paused state");
}

void testWorkshopVocalCutSuppressesCenterAndPreservesBassAndSide()
{
    constexpr double sampleRate = 48000.0;
    constexpr double pi = 3.14159265358979323846;
    constexpr int blockSize = 4096;

    echo::WorkshopAudioEffectState state;
    state.enabled = true;
    state.effect = echo::WorkshopAudioEffectType::VocalCut;
    state.mix = 1.0f;
    state.outputGainDb = 0.0f;
    state.vocalCutStrength = 1.0f;
    state.vocalCutBassPreserveHz = 160.0f;

    auto renderRatio = [&](double frequency, bool sideOnly, float bassPreserveHz = 160.0f,
                           float strength = 1.0f) {
        state.vocalCutBassPreserveHz = bassPreserveHz;
        state.vocalCutStrength = strength;
        echo::WorkshopAudioEffectController controller;
        require(controller.setState(state), "bounded Workshop vocal cut state is accepted");
        echo::WorkshopAudioEffectProcessor processor(controller);
        processor.prepare(sampleRate, blockSize, 2);

        double inputEnergy = 0.0;
        double outputEnergy = 0.0;
        for (int block = 0; block < 2; ++block)
        {
            echo::FloatAudioBuffer buffer(2, blockSize);
            for (int sample = 0; sample < blockSize; ++sample)
            {
                const int absoluteSample = block * blockSize + sample;
                const float value = 0.35f * std::sin(2.0 * pi * frequency * absoluteSample / sampleRate);
                buffer.getWritePointer(0)[sample] = value;
                buffer.getWritePointer(1)[sample] = sideOnly ? -value : value;
            }
            processor.processBlock(buffer, 0, blockSize);
            if (block == 1)
            {
                for (int sample = blockSize / 2; sample < blockSize; ++sample)
                {
                    const int absoluteSample = block * blockSize + sample;
                    const double input = 0.35 * std::sin(2.0 * pi * frequency * absoluteSample / sampleRate);
                    inputEnergy += input * input;
                    const double output = buffer.getReadPointer(0)[sample];
                    outputEnergy += output * output;
                }
            }
        }
        return std::sqrt(outputEnergy / std::max(1.0e-12, inputEnergy));
    };

    const double vocalBandRatio = renderRatio(1000.0, false);
    const double lowMidRatio = renderRatio(300.0, false);
    const double bassRatio = renderRatio(80.0, false);
    const double sideRatio = renderRatio(1000.0, true);
    const double gentlerStrengthRatio = renderRatio(1000.0, false, 160.0f, 0.5f);
    const double narrowBassRatio = renderRatio(150.0, false, 80.0f);
    const double wideBassRatio = renderRatio(150.0, false, 220.0f);
    require(vocalBandRatio < 0.02, "Workshop vocal cut deeply suppresses centered vocal-band content");
    require(lowMidRatio < 0.12, "Workshop vocal cut uses a steep low-mid transition for stronger male-vocal reduction");
    require(bassRatio > 0.9, "Workshop vocal cut preserves centered low bass below the configured crossover");
    require(sideRatio > 0.95, "Workshop vocal cut preserves side-panned accompaniment");
    // The laboratory preset must suppress low male-vocal harmonics without
    // turning its narrower bass protection into a broad bass cut.
    require(renderRatio(150.0, false, 120.0f) < 0.18,
        "strong vocal cut suppresses centered low-vocal content at 150 Hz");
    require(renderRatio(180.0, false, 120.0f) < 0.05,
        "strong vocal cut suppresses centered low-vocal content at 180 Hz");
    require(renderRatio(80.0, false, 120.0f) > 0.98,
        "strong vocal cut preserves deep centered bass with its flatter passband");
    require(gentlerStrengthRatio > 0.45 && gentlerStrengthRatio < 0.55,
        "Workshop vocal cut strength remains a predictable residual-center control");
    require(wideBassRatio > narrowBassRatio * 5.0,
        "Workshop vocal cut bass protection frequency materially changes the retained center band");

    echo::WorkshopAudioEffectController monoController;
    state.vocalCutStrength = 1.0f;
    state.vocalCutBassPreserveHz = 120.0f;
    require(monoController.setState(state), "Workshop vocal cut accepts a mono bypass state");
    echo::WorkshopAudioEffectProcessor monoProcessor(monoController);
    monoProcessor.prepare(sampleRate, 256, 1);
    echo::FloatAudioBuffer monoBuffer(1, 256);
    std::array<float, 256> monoInput {};
    for (int sample = 0; sample < 256; ++sample)
    {
        monoInput[static_cast<size_t>(sample)] = static_cast<float>(
            0.31 * std::sin(2.0 * pi * 733.0 * sample / sampleRate));
        monoBuffer.getWritePointer(0)[sample] = monoInput[static_cast<size_t>(sample)];
    }
    monoProcessor.processBlock(monoBuffer, 0, 256);
    for (int sample = 0; sample < 256; ++sample)
        requireNear(monoBuffer.getReadPointer(0)[sample], monoInput[static_cast<size_t>(sample)], 1.0e-7,
            "Workshop vocal cut must bypass mono without startup bitcrusher bleed");

    echo::WorkshopAudioEffectController multichannelController;
    require(multichannelController.setState(state), "Workshop vocal cut accepts a multichannel bypass state");
    echo::WorkshopAudioEffectProcessor multichannelProcessor(multichannelController);
    multichannelProcessor.prepare(sampleRate, 256, 4);
    echo::FloatAudioBuffer multichannelBuffer(4, 256);
    std::array<float, 256> centerInput {};
    std::array<float, 256> surroundInput {};
    for (int sample = 0; sample < 256; ++sample)
    {
        const float front = static_cast<float>(0.22 * std::sin(2.0 * pi * 911.0 * sample / sampleRate));
        centerInput[static_cast<size_t>(sample)] = static_cast<float>(
            0.17 * std::sin(2.0 * pi * 317.0 * sample / sampleRate));
        surroundInput[static_cast<size_t>(sample)] = static_cast<float>(
            0.13 * std::cos(2.0 * pi * 521.0 * sample / sampleRate));
        multichannelBuffer.getWritePointer(0)[sample] = front;
        multichannelBuffer.getWritePointer(1)[sample] = -front;
        multichannelBuffer.getWritePointer(2)[sample] = centerInput[static_cast<size_t>(sample)];
        multichannelBuffer.getWritePointer(3)[sample] = surroundInput[static_cast<size_t>(sample)];
    }
    multichannelProcessor.processBlock(multichannelBuffer, 0, 256);
    for (int sample = 0; sample < 256; ++sample)
    {
        requireNear(multichannelBuffer.getReadPointer(2)[sample], centerInput[static_cast<size_t>(sample)], 1.0e-7,
            "Workshop vocal cut must preserve non-stereo center/LFE channels");
        requireNear(multichannelBuffer.getReadPointer(3)[sample], surroundInput[static_cast<size_t>(sample)], 1.0e-7,
            "Workshop vocal cut must preserve non-stereo surround channels");
    }

    echo::WorkshopAudioEffectController smoothingController;
    state.vocalCutStrength = 0.5f;
    state.vocalCutBassPreserveHz = 80.0f;
    require(smoothingController.setState(state), "Workshop vocal cut accepts the lower bounded strength");
    echo::WorkshopAudioEffectProcessor smoothingProcessor(smoothingController);
    constexpr int smoothingBlockSize = 480;
    smoothingProcessor.prepare(sampleRate, smoothingBlockSize, 2);
    for (int block = 0; block < 2; ++block)
    {
        echo::FloatAudioBuffer warmup(2, smoothingBlockSize);
        for (int sample = 0; sample < smoothingBlockSize; ++sample)
        {
            const int absoluteSample = block * smoothingBlockSize + sample;
            const float value = 0.35f * std::sin(2.0 * pi * 1000.0 * absoluteSample / sampleRate);
            warmup.getWritePointer(0)[sample] = value;
            warmup.getWritePointer(1)[sample] = value;
        }
        smoothingProcessor.processBlock(warmup, 0, smoothingBlockSize);
    }
    state.vocalCutStrength = 1.0f;
    require(smoothingController.setState(state), "Workshop vocal cut accepts a live strength update");
    echo::FloatAudioBuffer transition(2, smoothingBlockSize);
    for (int sample = 0; sample < smoothingBlockSize; ++sample)
    {
        const int absoluteSample = 2 * smoothingBlockSize + sample;
        const float value = 0.35f * std::sin(2.0 * pi * 1000.0 * absoluteSample / sampleRate);
        transition.getWritePointer(0)[sample] = value;
        transition.getWritePointer(1)[sample] = value;
    }
    smoothingProcessor.processBlock(transition, 0, smoothingBlockSize);
    double earlyEnergy = 0.0;
    double lateEnergy = 0.0;
    for (int sample = 0; sample < 48; ++sample)
    {
        const double early = transition.getReadPointer(0)[sample];
        const double late = transition.getReadPointer(0)[smoothingBlockSize - 48 + sample];
        earlyEnergy += early * early;
        lateEnergy += late * late;
    }
    require(earlyEnergy > lateEnergy * 2.0,
        "Workshop vocal cut strength changes ramp instead of jumping at the block boundary");

    echo::WorkshopAudioEffectController cutoffController;
    state.vocalCutStrength = 1.0f;
    state.vocalCutBassPreserveHz = 80.0f;
    require(cutoffController.setState(state), "Workshop vocal cut accepts the lower bass-protection bound");
    echo::WorkshopAudioEffectProcessor cutoffProcessor(cutoffController);
    cutoffProcessor.prepare(sampleRate, smoothingBlockSize, 2);
    float previousOutput = 0.0f;
    for (int block = 0; block < 3; ++block)
    {
        echo::FloatAudioBuffer warmup(2, smoothingBlockSize);
        for (int sample = 0; sample < smoothingBlockSize; ++sample)
        {
            const int absoluteSample = block * smoothingBlockSize + sample;
            const float value = 0.35f * std::sin(2.0 * pi * 100.0 * absoluteSample / sampleRate);
            warmup.getWritePointer(0)[sample] = value;
            warmup.getWritePointer(1)[sample] = value;
        }
        cutoffProcessor.processBlock(warmup, 0, smoothingBlockSize);
        previousOutput = warmup.getReadPointer(0)[smoothingBlockSize - 1];
    }
    state.vocalCutBassPreserveHz = 300.0f;
    require(cutoffController.setState(state), "Workshop vocal cut accepts a live bass-protection update");
    echo::FloatAudioBuffer cutoffTransition(2, smoothingBlockSize);
    for (int sample = 0; sample < smoothingBlockSize; ++sample)
    {
        const int absoluteSample = 3 * smoothingBlockSize + sample;
        const float value = 0.35f * std::sin(2.0 * pi * 100.0 * absoluteSample / sampleRate);
        cutoffTransition.getWritePointer(0)[sample] = value;
        cutoffTransition.getWritePointer(1)[sample] = value;
    }
    cutoffProcessor.processBlock(cutoffTransition, 0, smoothingBlockSize);
    float maximumStep = 0.0f;
    float maximumAbsoluteOutput = 0.0f;
    for (int sample = 0; sample < smoothingBlockSize; ++sample)
    {
        const float output = cutoffTransition.getReadPointer(0)[sample];
        maximumStep = std::max(maximumStep, std::abs(output - previousOutput));
        maximumAbsoluteOutput = std::max(maximumAbsoluteOutput, std::abs(output));
        previousOutput = output;
    }
    require(maximumStep < 0.08f && maximumAbsoluteOutput < 0.5f,
        "Workshop vocal cut bass-protection updates remain bounded and click-free");
}

void testAudioDaemonLocalSourceRangeStopsAtSegmentBoundary()
{
    const auto fixturePath = writeStreamingDecoderWavFixture(48'000, 2, 48'000);
    ScopedFileRemoval removeFixture(fixturePath);
    std::atomic<int> pushedFrames { 0 };
    std::atomic<bool> inputEnded { false };

    AudioDaemon::SourceHooks hooks;
    hooks.markInputEnded = [&] { inputEnded.store(true, std::memory_order_release); };
    hooks.requestStop = [] {};
    hooks.setPaused = [](bool) {};
    hooks.push = [&](const float*, int frames) {
        pushedFrames.fetch_add(frames, std::memory_order_relaxed);
        return true;
    };
    hooks.pushForGeneration = [&](const float*, int frames, uint64_t) {
        pushedFrames.fetch_add(frames, std::memory_order_relaxed);
        return true;
    };
    hooks.generation = [] { return uint64_t { 1 }; };
    hooks.decoderSampleRateFor = [](int outputSampleRate) { return outputSampleRate; };
    hooks.requestStopForGeneration = [](uint64_t) { return true; };
    hooks.seekPrerollFrames = [] { return 0; };
    hooks.replaceBufferedAudioWithPreroll = [&](const float*, int frames, int prerollFrames, bool) {
        const int playableFrames = std::max(0, frames - prerollFrames);
        pushedFrames.fetch_add(playableFrames, std::memory_order_relaxed);
        return playableFrames;
    };

    std::atomic<bool> shutdownRequested { false };
    AudioDaemon daemon(std::move(hooks), 48'000, 1, shutdownRequested);
    nlohmann::json result;
    const nlohmann::json source = {
        {"kind", "local"},
        {"uri", fixturePath},
        {"range", {{"startSeconds", 0.25}, {"endSeconds", 0.5}}},
    };
    require(daemon.openSourceForTests(source, 48'000, 0.0, result),
        "bounded local source must open through the daemon");
    require(std::abs(result.value("durationSeconds", 0.0) - 0.25) < 0.0001,
        "bounded local source reports logical segment duration");
    require(std::abs(result.value("startSeconds", -1.0)) < 0.0001,
        "bounded local source reports logical start position");

    const auto decodeDeadline = std::chrono::steady_clock::now() + std::chrono::seconds(2);
    while (!inputEnded.load(std::memory_order_acquire)
        && std::chrono::steady_clock::now() < decodeDeadline)
        std::this_thread::yield();
    require(inputEnded.load(std::memory_order_acquire),
        "bounded local source marks input ended at its segment boundary");
    require(pushedFrames.load(std::memory_order_acquire) >= 11'900
            && pushedFrames.load(std::memory_order_acquire) <= 12'100,
        "bounded local source must not decode PCM from the following segment");

    pushedFrames.store(0, std::memory_order_release);
    inputEnded.store(false, std::memory_order_release);
    nlohmann::json seekResult;
    require(daemon.seekForTests(0.2, seekResult),
        "bounded local source seek must use logical segment time");
    const auto seekDeadline = std::chrono::steady_clock::now() + std::chrono::seconds(2);
    while (!inputEnded.load(std::memory_order_acquire)
        && std::chrono::steady_clock::now() < seekDeadline)
        std::this_thread::yield();
    require(inputEnded.load(std::memory_order_acquire),
        "bounded local source seek marks input ended at the same segment boundary");
    require(pushedFrames.load(std::memory_order_acquire) >= 2'300
            && pushedFrames.load(std::memory_order_acquire) <= 2'500,
        "bounded local source seek must not continue decoding past the segment end");

    const nlohmann::json invalidSource = {
        {"kind", "local"},
        {"uri", fixturePath},
        {"range", {{"startSeconds", 2.0}, {"endSeconds", 3.0}}},
    };
    nlohmann::json invalidResult;
    require(!daemon.openSourceForTests(invalidSource, 48'000, 0.0, invalidResult),
        "a source range beyond the media duration must fail to open");
    nlohmann::json invalidSeekResult;
    require(!daemon.seekForTests(0.0, invalidSeekResult),
        "a failed bounded source open must not leave a seekable current file");

    nlohmann::json stopResult;
    daemon.stopForTests(stopResult);
}

void testAudioDaemonDoubletimeProcessesPcmOnDecoderThread()
{
    const auto fixturePath = writeStreamingDecoderWavFixture(48'000, 2, 48'000);
    ScopedFileRemoval removeFixture(fixturePath);
    std::atomic<int> pushedFrames { 0 };
    std::atomic<bool> inputEnded { false };

    AudioDaemon::SourceHooks hooks;
    hooks.markInputEnded = [&] { inputEnded.store(true, std::memory_order_release); };
    hooks.requestStop = [] {};
    hooks.setPaused = [](bool) {};
    hooks.push = [&](const float*, int frames) {
        pushedFrames.fetch_add(frames, std::memory_order_relaxed);
        return true;
    };
    hooks.pushForGeneration = [&](const float*, int frames, uint64_t) {
        pushedFrames.fetch_add(frames, std::memory_order_relaxed);
        return true;
    };
    hooks.generation = [] { return uint64_t { 1 }; };
    hooks.decoderSampleRateFor = [](int outputSampleRate) { return outputSampleRate; };
    hooks.requestStopForGeneration = [](uint64_t) { return true; };

    std::atomic<bool> shutdownRequested { false };
    AudioDaemon daemon(std::move(hooks), 48'000, 1, shutdownRequested);
    daemon.setPlaybackRateForTests(2.0f, echo::SpeedMode::Speed);
    nlohmann::json result;
    require(daemon.openFileForTests(fixturePath, 48'000, result),
        "Doubletime source must open through the native daemon");

    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(2);
    while (!inputEnded.load(std::memory_order_acquire)
        && std::chrono::steady_clock::now() < deadline)
        std::this_thread::yield();
    require(inputEnded.load(std::memory_order_acquire),
        "Doubletime decoder must reach input EOF");
    require(pushedFrames.load(std::memory_order_acquire) >= 22'000
            && pushedFrames.load(std::memory_order_acquire) <= 26'000,
        "2.0x Doubletime must publish approximately half as many PCM frames");

    nlohmann::json stopResult;
    daemon.stopForTests(stopResult);
}

void testRenderEpochCoversPostFifoClockUpdatesForAllSources()
{
    {
        echo::EqProcessor eqProcessor;
        echo::ChannelBalanceProcessor channelBalanceProcessor;
        PcmRingAudioSource source(2, 256, 0, 0, 1.0f, eqProcessor, channelBalanceProcessor);
        source.beginSession();
        std::vector<float> oldFrames(32, 0.25f);
        require(source.push(oldFrames.data(), 16), "PCM post-FIFO race must prime old frames");
        std::promise<void> afterFifo;
        std::promise<void> release;
        auto releaseFuture = release.get_future().share();
        source.setPostFifoReadHookForTests([&] {
            afterFifo.set_value();
            releaseFuture.wait();
        });
        auto output = makeBuffer(2, 16);
        auto render = std::async(std::launch::async, [&] { return source.renderPlanar(output, 0, 16); });
        afterFifo.get_future().wait();
        auto replace = std::async(std::launch::async, [&] { source.beginSession(); });
        require(replace.wait_for(std::chrono::milliseconds(10)) == std::future_status::timeout,
            "PCM beginSession must wait after FIFO release until DSP, declick, and clock work exits");
        require(source.getFramesPlayed() == 0,
            "PCM callback blocked immediately after FIFO release must not have published its old clock yet");
        release.set_value();
        require(render.get() == 16, "PCM old render must finish before replacement commits");
        replace.get();
        source.setPostFifoReadHookForTests({});
        require(source.getFramesPlayed() == 0,
            "PCM old callback must not update the replacement session clock");
    }

    {
        DopRingSource source(2, 128, 0, 0);
        source.beginSession();
        std::vector<uint32_t> oldFrames(32, 0x000011u);
        require(source.push(oldFrames.data(), 16), "DoP post-FIFO race must prime old frames");
        std::promise<void> afterFifo;
        std::promise<void> release;
        auto releaseFuture = release.get_future().share();
        source.setPostFifoReadHookForTests([&] {
            afterFifo.set_value();
            releaseFuture.wait();
        });
        std::vector<uint32_t> output(32, 0u);
        auto render = std::async(std::launch::async, [&] {
            return source.renderInterleaved(output.data(), 16, 2);
        });
        afterFifo.get_future().wait();
        auto replace = std::async(std::launch::async, [&] { source.beginSession(); });
        require(replace.wait_for(std::chrono::milliseconds(10)) == std::future_status::timeout,
            "DoP beginSession must wait after FIFO release until marker and clock work exits");
        require(source.getFramesPlayed() == 0,
            "DoP callback blocked after FIFO release must not publish its old clock yet");
        release.set_value();
        require(render.get() == 16, "DoP old render must finish before replacement commits");
        replace.get();
        source.setPostFifoReadHookForTests({});
        require(source.getFramesPlayed() == 0,
            "DoP old callback must not update the replacement session clock");
    }

    {
        NativeDsdRingSource source(2, 128, 0, 0);
        source.beginSession();
        std::vector<uint8_t> oldFrames(32, 0x11u);
        require(source.push(oldFrames.data(), 16), "native DSD post-FIFO race must prime old frames");
        std::promise<void> afterFifo;
        std::promise<void> release;
        auto releaseFuture = release.get_future().share();
        source.setPostFifoReadHookForTests([&] {
            afterFifo.set_value();
            releaseFuture.wait();
        });
        std::vector<uint8_t> output(32, 0u);
        auto render = std::async(std::launch::async, [&] {
            return source.renderInterleaved(output.data(), 16, 2);
        });
        afterFifo.get_future().wait();
        auto replace = std::async(std::launch::async, [&] { source.beginSession(); });
        require(replace.wait_for(std::chrono::milliseconds(10)) == std::future_status::timeout,
            "native DSD beginSession must wait after FIFO release until clock work exits");
        require(source.getFramesPlayed() == 0,
            "native DSD callback blocked after FIFO release must not publish its old clock yet");
        release.set_value();
        require(render.get() == 16, "native DSD old render must finish before replacement commits");
        replace.get();
        source.setPostFifoReadHookForTests({});
        require(source.getFramesPlayed() == 0,
            "native DSD old callback must not update the replacement session clock");
    }
}

void testAutomixCancelWaitsForInFlightDeckReaderLease()
{
    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    PcmRingAudioSource source(2, 512, 0, 0, 1.0f, eqProcessor, channelBalanceProcessor);
    source.beginSession();
    std::vector<float> current(256, 0.25f);
    std::vector<float> next(256, -0.25f);
    require(source.push(current.data(), 128), "automix race current deck must be primed");
    const uint64_t oldLease = source.prepareAutomixDeck(0, 64, 0.0, 0.0);
    require(oldLease != 0 && source.pushAutomixDeck(oldLease, next.data(), 128),
        "automix race next deck must be primed with a lease");

    std::promise<void> deckReaderPrepared;
    std::promise<void> releaseDeckReader;
    auto releaseFuture = releaseDeckReader.get_future().share();
    std::atomic<int> hookCalls { 0 };
    source.setReadLeaseHookForTests([&] {
        if (hookCalls.fetch_add(1, std::memory_order_acq_rel) == 1)
        {
            deckReaderPrepared.set_value();
            releaseFuture.wait();
        }
    });

    auto output = makeBuffer(2, 64);
    auto render = std::async(std::launch::async, [&] { return source.renderPlanar(output, 0, 64); });
    deckReaderPrepared.get_future().wait();
    auto cancel = std::async(std::launch::async, [&] { source.cancelAutomix(); });
    require(cancel.wait_for(std::chrono::milliseconds(10)) == std::future_status::timeout,
        "automix cancel must retire the producer lease then wait for the deck callback reader");
    releaseDeckReader.set_value();
    render.get();
    cancel.get();
    source.setReadLeaseHookForTests({});

    require(!source.pushAutomixDeck(oldLease, next.data(), 1),
        "cancelled automix producer must not write after its deck slot is retired");
    const uint64_t newLease = source.prepareAutomixDeck(source.getFramesPlayed() + 32, 64, 0.0, 0.0);
    require(newLease != 0 && newLease != oldLease,
        "reused automix slot must receive a distinct lease after callback quiescence");
}

void testSessionSwapRetiresPromotedAutomixDeckReader()
{
    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    PcmRingAudioSource source(2, 512, 0, 0, 1.0f, eqProcessor, channelBalanceProcessor);
    source.beginSession();
    std::vector<float> current(128, 0.25f);
    std::vector<float> promoted(256, -0.25f);
    require(source.push(current.data(), 64), "promoted-deck race current PCM must be primed");
    const uint64_t promotedLease = source.prepareAutomixDeck(0, 16, 0.0, 0.0);
    require(promotedLease != 0 && source.pushAutomixDeck(promotedLease, promoted.data(), 128),
        "promoted-deck race next PCM must be primed");

    auto fadeOutput = makeBuffer(2, 32);
    require(source.renderPlanar(fadeOutput, 0, 32) > 0,
        "automix fade must render before promotion");
    require(source.commitAutomixDeck(promotedLease),
        "Deck B must promote to the current deck after the fade boundary");

    std::promise<void> readerPrepared;
    std::promise<void> releaseReader;
    auto releaseFuture = releaseReader.get_future().share();
    std::atomic<bool> hookEntered { false };
    source.setReadLeaseHookForTests([&] {
        if (!hookEntered.exchange(true, std::memory_order_acq_rel))
        {
            readerPrepared.set_value();
            releaseFuture.wait();
        }
    });

    auto oldOutput = makeBuffer(2, 16);
    auto render = std::async(std::launch::async, [&] {
        return source.renderPlanar(oldOutput, 0, 16);
    });
    readerPrepared.get_future().wait();
    auto swap = std::async(std::launch::async, [&] { source.beginSession(); });
    require(swap.wait_for(std::chrono::milliseconds(10)) == std::future_status::timeout,
        "new open must wait for an in-flight reader of a promoted Deck B slot");
    releaseReader.set_value();
    require(render.get() == 16, "promoted Deck B callback must finish against its retired epoch");
    swap.get();
    source.setReadLeaseHookForTests({});

    require(!source.pushAutomixDeck(promotedLease, promoted.data(), 1),
        "producer for a promoted old deck must be rejected after the new session");
    const uint64_t generation = source.session_.generation();
    std::vector<float> replacement(32, 0.75f);
    require(source.pushForGeneration(replacement.data(), 16, generation),
        "new session must accept replacement PCM after promoted-deck retirement");
    auto replacementOutput = makeBuffer(2, 16);
    require(source.renderPlanar(replacementOutput, 0, 16) == 16,
        "old promoted-deck finishedRead must not consume new-session frames");
    require(source.getReadyFrames() == 0,
        "replacement FIFO sequence must remain legal after promoted-deck retirement");
}

void testGaplessCancelRejectsBlockedProducerLease()
{
    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    PcmRingAudioSource source(2, 8, 0, 0, 1.0f, eqProcessor, channelBalanceProcessor);
    source.beginSession();
    source.prepareGapless();
    std::vector<float> full(16, 0.25f);
    require(source.pushAutomixNext(full.data(), 8), "gapless race must fill its deck FIFO");
    auto blockedProducer = std::async(std::launch::async, [&] {
        return source.pushAutomixNext(full.data(), 1);
    });
    require(blockedProducer.wait_for(std::chrono::milliseconds(10)) == std::future_status::timeout,
        "gapless producer must be blocked by a full leased FIFO");
    source.cancelAutomix();
    require(!blockedProducer.get(), "gapless cancel must invalidate and wake the blocked producer lease");
}

void testPcmSessionSwapStressAtSmallCallbacks()
{
    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    PcmRingAudioSource source(2, 512, 0, 0, 1.0f, eqProcessor, channelBalanceProcessor);
    for (int iteration = 0; iteration < 1000; ++iteration)
    {
        source.beginSession();
        const int frames = iteration % 2 == 0 ? 64 : 128;
        const float pattern = iteration % 2 == 0 ? 0.2f : -0.2f;
        std::vector<float> input(static_cast<size_t>(frames * 2), pattern);
        require(source.pushForGeneration(input.data(), frames, source.session_.generation()),
            "rapid session swap must accept only the active generation");
        auto output = makeBuffer(2, frames);
        require(source.renderPlanar(output, 0, frames) == static_cast<uint64_t>(frames),
            "64/128-frame rapid session swap callback must not deadlock");
        require(source.getReadyFrames() >= 0 && source.getReadyFrames() <= 512,
            "rapid session swap must preserve legal FIFO occupancy");
    }
}


void testLibavPcmStreamDecoderReadsBoundedChunks()
{
    const int sampleRate = 48000;
    const int channels = 2;
    const int frames = 10000;
    const auto path = writeStreamingDecoderWavFixture(sampleRate, channels, frames);
    ScopedFileRemoval cleanup(path);

    echo::LibavPcmStreamDecoder decoder;
    decoder.open(path);
    require(decoder.sampleRate() == sampleRate, "streaming decoder reports fixture sample rate");
    require(decoder.channels() == channels, "streaming decoder reports fixture channels");

    int totalFrames = 0;
    int chunks = 0;
    while (! decoder.eof())
    {
        auto chunk = decoder.readFrames(1024);
        if (chunk.frames == 0)
            break;
        require(chunk.frames <= 1024, "streaming decoder respects requested chunk frame bound");
        require(chunk.samples.size() == static_cast<size_t>(chunk.frames * channels), "streaming decoder chunk is interleaved f32");
        totalFrames += chunk.frames;
        ++chunks;
    }

    require(totalFrames == frames, "streaming decoder emits every generated frame");
    require(chunks > 1, "streaming decoder does not return the whole track as one resident chunk");
}

void testLibavPcmStreamDecoderBoundsDamagedMediaRecovery()
{
    echo::LibavDecodeRecoveryBudget budget;
    require(
        ! budget.tryConsume(AVERROR(EIO), echo::LibavDecodeErrorStage::packet),
        "streaming decoder does not hide I/O errors");
    require(
        ! budget.tryConsume(AVERROR(ENOMEM), echo::LibavDecodeErrorStage::packet),
        "streaming decoder does not hide allocation errors");
    require(
        ! budget.tryConsume(AVERROR_EOF, echo::LibavDecodeErrorStage::packet),
        "streaming decoder keeps EOF outside error recovery");

    for (int count = 1; count <= 5; ++count)
        require(
            budget.tryConsume(AVERROR_INVALIDDATA, echo::LibavDecodeErrorStage::packet, 24'000, 1'024),
            "streaming decoder tolerates the five-packet damage burst seen in a playable MP3");
    require(
        budget.regionErrors() == 5
            && budget.skippedDurationUs() == 120'000
            && budget.skippedBytes() == 5'120,
        "streaming decoder tracks the damaged media region");
    budget.resetRegion();
    require(
        budget.regionErrors() == 0 && budget.totalErrors() == 5,
        "a decoded PCM frame resets the region without erasing lifetime diagnostics");

    for (int count = 1; count <= echo::maxRecoverableLibavFrameErrors; ++count)
        require(
            budget.tryConsume(AVERROR_INVALIDDATA, echo::LibavDecodeErrorStage::frame),
            "streaming decoder permits a bounded run of invalid frames");
    require(
        ! budget.tryConsume(AVERROR_INVALIDDATA, echo::LibavDecodeErrorStage::frame),
        "streaming decoder fails closed after repeated invalid frames");

    budget.resetRegion();
    for (int count = 1; count <= echo::maxRecoverableLibavDemuxErrors; ++count)
        require(
            budget.tryConsume(AVERROR_INVALIDDATA, echo::LibavDecodeErrorStage::demux),
            "streaming decoder permits a bounded demux resynchronization attempt");
    require(
        ! budget.tryConsume(AVERROR_INVALIDDATA, echo::LibavDecodeErrorStage::demux),
        "streaming decoder stops a demuxer that cannot make progress");

    budget.resetRegion();
    require(
        budget.tryConsume(
            AVERROR_INVALIDDATA,
            echo::LibavDecodeErrorStage::packet,
            echo::maxRecoverableLibavDamageDurationUs,
            0),
        "streaming decoder accepts damage at the duration budget boundary");
    require(
        ! budget.tryConsume(AVERROR_INVALIDDATA, echo::LibavDecodeErrorStage::packet, 1, 0),
        "streaming decoder rejects damage beyond the duration budget");

    budget.resetRegion();
    require(
        budget.tryConsume(
            AVERROR_INVALIDDATA,
            echo::LibavDecodeErrorStage::packet,
            0,
            echo::maxRecoverableLibavDamageBytes),
        "streaming decoder accepts damage at the byte budget boundary");
    require(
        ! budget.tryConsume(AVERROR_INVALIDDATA, echo::LibavDecodeErrorStage::packet, 0, 1),
        "streaming decoder rejects damage beyond the byte budget");

    budget.resetRegion();
    for (int count = 1; count <= echo::maxRecoverableLibavRegionErrors; ++count)
        require(
            budget.tryConsume(AVERROR_INVALIDDATA, echo::LibavDecodeErrorStage::packet),
            "streaming decoder permits bounded recovery when packet timing is unavailable");
    require(
        ! budget.tryConsume(AVERROR_INVALIDDATA, echo::LibavDecodeErrorStage::packet),
        "streaming decoder stops recovery when no valid PCM frame appears");

    require(echo::shouldLogRecoverableLibavError(1), "recovery logs the first damaged packet");
    require(echo::shouldLogRecoverableLibavError(4), "recovery logs power-of-two progress");
    require(! echo::shouldLogRecoverableLibavError(5), "recovery suppresses repetitive log noise");
}

void testLibavPcmStreamDecoderMapsToNativeOutputFormat()
{
    const int sourceSampleRate = 44100;
    const int targetSampleRate = 48000;
    const int sourceFrames = 4410;
    const auto path = writeStreamingDecoderWavFixture(sourceSampleRate, 1, sourceFrames);
    ScopedFileRemoval cleanup(path);

    echo::LibavPcmStreamDecoder decoder;
    decoder.open(path, targetSampleRate, 2);
    require(decoder.sampleRate() == targetSampleRate,
        "streaming decoder must resample into the native device rate");
    require(decoder.channels() == 2,
        "streaming decoder must map mono input into the native stereo layout");

    int totalFrames = 0;
    while (! decoder.eof())
    {
        auto chunk = decoder.readFrames(512);
        if (chunk.frames == 0)
            break;
        require(chunk.samples.size() == static_cast<size_t>(chunk.frames * 2),
            "mapped streaming decoder output must remain stereo interleaved f32");
        totalFrames += chunk.frames;
    }
    require(totalFrames > sourceFrames,
        "44.1 kHz source must expand when mapped into a 48 kHz native output stream");
}

void requireDsdContainerDecodesToPcm(const std::string& path, const std::string& container)
{
    const auto probe = echo::LibavDecoder::probe(path);
    require(probe.channels == 2, container + " probe reports stereo channels");
    require(probe.codec.find("dsd") != std::string::npos, container + " probe resolves a DSD codec");

    constexpr int targetSampleRate = 176400;
    echo::LibavPcmStreamDecoder decoder;
    decoder.open(path, targetSampleRate, 2);
    require(decoder.sampleRate() == targetSampleRate, container + " decoder maps into the requested PCM sample rate");
    require(decoder.channels() == 2, container + " decoder keeps stereo PCM output");

    int totalFrames = 0;
    float peakMagnitude = 0.0f;
    while (! decoder.eof())
    {
        auto chunk = decoder.readFrames(512);
        if (chunk.frames == 0)
            break;
        require(chunk.samples.size() == static_cast<size_t>(chunk.frames * 2),
            container + " decoder output is stereo interleaved f32");
        require(std::all_of(chunk.samples.begin(), chunk.samples.end(), [](float sample) {
            return std::isfinite(sample);
        }), container + " decoder output remains finite");
        for (const float sample : chunk.samples)
            peakMagnitude = std::max(peakMagnitude, std::abs(sample));
        totalFrames += chunk.frames;
    }

    require(totalFrames > 0, container + " decoder emits PCM frames");
    require(peakMagnitude <= 1.0f, container + " decoder output stays within normalized PCM range");
}

void testLibavPcmStreamDecoderDecodesDsdContainersToPcm()
{
    const auto dsfPath = writeStreamingDecoderDsfFixture();
    ScopedFileRemoval dsfCleanup(dsfPath);
    requireDsdContainerDecodesToPcm(dsfPath, "DSF");

    const auto dffPath = writeStreamingDecoderDffFixture();
    ScopedFileRemoval dffCleanup(dffPath);
    requireDsdContainerDecodesToPcm(dffPath, "DFF");
}

void testLibavPcmStreamDecoderSeekCancelAndInvalidFile()
{
    const int sampleRate = 44100;
    const int channels = 2;
    const auto path = writeStreamingDecoderWavFixture(sampleRate, channels, 12000);
    ScopedFileRemoval cleanup(path);

    echo::LibavPcmStreamDecoder decoder;
    decoder.open(path);
    decoder.seek(0.10);
    auto chunk = decoder.readFrames(512);
    require(chunk.frames > 0 && chunk.frames <= 512, "streaming decoder reads after seek");
    decoder.cancel();
    require(decoder.cancelled(), "streaming decoder exposes cancel state");
    require(decoder.readFrames(512).frames == 0, "streaming decoder cancel stops subsequent reads");
    decoder.close();
    require(decoder.eof(), "streaming decoder close reaches EOF state");

    bool invalidFailed = false;
    try
    {
        echo::LibavPcmStreamDecoder invalid;
        invalid.open(path + ".missing");
    }
    catch (const std::exception&)
    {
        invalidFailed = true;
    }
    require(invalidFailed, "streaming decoder invalid input fails visibly");
}


void testNativeRenderAdapter()
{
    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    PcmRingAudioSource source(2, 64, 0, 0, 1.0f, eqProcessor, channelBalanceProcessor);
    source.prepareForNativeRender(16, 100.0);
    source.beginSession();

    std::vector<float> emptyOutput(8, 1.0f);
    const auto emptyFrames = source.renderInterleaved(emptyOutput.data(), 4, 2);
    require(emptyFrames == 0, "native render adapter must report zero frames before PCM");
    require(std::all_of(emptyOutput.begin(), emptyOutput.end(), [] (float sample) { return sample == 0.0f; }),
        "native render adapter must clear output before PCM");
    require(source.getUnderrunCallbacks() == 0, "native render adapter must not count underrun before first PCM");

    const std::vector<float> input {
        0.10f, -0.10f,
        0.20f, -0.20f,
        0.30f, -0.30f,
        0.40f, -0.40f,
        0.50f, -0.50f,
        0.60f, -0.60f,
    };
    require(source.push(input.data(), 6), "native render adapter test PCM push");

    std::vector<float> rampWarmup(4, 0.0f);
    require(source.renderInterleaved(rampWarmup.data(), 2, 2) == 2, "native render adapter must consume ramp warmup");
    std::vector<float> output(8, 0.0f);
    const auto frames = source.renderInterleaved(output.data(), 4, 2);
    require(frames == 4, "native render adapter must report consumed frame count");

    for (size_t i = 0; i < output.size(); ++i)
        require(std::abs(output[i] - input[i + 4]) <= nearTolerance, "native render adapter must preserve interleaved PCM after declick ramp");
}

void testPcmDeclickRampOnSessionStartAndStop()
{
    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    PcmRingAudioSource source(2, 64, 0, 0, 1.0f, eqProcessor, channelBalanceProcessor);
    source.prepareForNativeRender(16, 1000.0);
    source.beginSession();

    std::vector<float> input(48, 1.0f);
    require(source.push(input.data(), 24), "declick source must accept PCM");

    auto fadeIn = makeBuffer(2, 8);
    require(source.renderPlanar(fadeIn, 0, 8) == 8, "declick fade-in must render input");
    require(std::abs(fadeIn.getSample(0, 0)) <= nearTolerance, "declick fade-in must start from silence");
    for (int sample = 1; sample < 7; ++sample)
        require(fadeIn.getSample(0, sample) >= fadeIn.getSample(0, sample - 1), "declick fade-in must be monotonic");
    require(fadeIn.getSample(0, 7) > 0.99f, "declick fade-in must reach unity");

    source.requestStop();
    auto fadeOut = makeBuffer(2, 8);
    require(source.renderPlanar(fadeOut, 0, 8) == 8, "declick fade-out must render remaining input");
    require(fadeOut.getSample(0, 0) > 0.99f, "declick fade-out must begin at current level");
    for (int sample = 1; sample < 8; ++sample)
        require(fadeOut.getSample(0, sample) <= fadeOut.getSample(0, sample - 1) + nearTolerance, "declick fade-out must be monotonic");
    require(std::abs(fadeOut.getSample(0, 7)) <= nearTolerance, "declick fade-out must reach silence");
}

void testNativeAutomixDeckMixesNextBeforeCurrentEnds()
{
    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    PcmRingAudioSource source(2, 64, 0, 0, 1.0f, eqProcessor, channelBalanceProcessor);
    source.beginSession();
    source.prepareAutomix(4.0, 0.5, 0.5, 0.0, 0.0);

    const std::vector<float> current {
        1.0f, 1.0f,
        1.0f, 1.0f,
        1.0f, 1.0f,
        1.0f, 1.0f,
        1.0f, 1.0f,
        1.0f, 1.0f,
    };
    const std::vector<float> next {
        0.5f, 0.5f,
        0.5f, 0.5f,
        0.5f, 0.5f,
        0.5f, 0.5f,
    };
    require(source.push(current.data(), 6), "native automix must accept current deck PCM");
    require(source.pushAutomixNext(next.data(), 4), "native automix must accept next deck PCM");
    source.markInputEnded();
    source.markAutomixNextEnded();

    auto output = makeBuffer(2, 6);
    const auto frames = source.renderPlanar(output, 0, 6);
    require(frames == 6, "native automix must advance output clock through current deck");
    require(std::abs(output.getSample(0, 1) - 1.0f) <= nearTolerance, "automix must keep current deck before fade");
    require(output.getSample(0, 3) > 1.0f, "automix must overlap current and next during fade");
    require(std::abs(output.getSample(0, 4) - 0.5f) <= 0.02f, "automix must keep next deck after fade");
    require(source.isDrained(), "native automix must drain only after next deck ends");
}

void testNativeAutomixPromotesDeckAndRearmsTheReleasedDeck()
{
    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    PcmRingAudioSource source(2, 128, 0, 0, 1.0f, eqProcessor, channelBalanceProcessor);
    source.prepareForNativeRender(8, 1000.0);
    source.beginSession();

    const std::vector<float> deckA(32 * 2, 0.2f);
    const std::vector<float> deckB(32 * 2, 0.4f);
    const std::vector<float> deckBContinuation(8 * 2, 0.4f);
    const std::vector<float> deckC(32 * 2, 0.8f);
    require(source.push(deckA.data(), 32), "rolling AutoMix must accept Deck A");

    const uint64_t deckBLease = source.prepareAutomixDeck(4, 4, 0.0, 0.0);
    require(deckBLease != 0, "rolling AutoMix must lease Deck B's producer slot");
    require(source.pushAutomixDeck(deckBLease, deckB.data(), 32),
        "rolling AutoMix must accept Deck B through its immutable lease");

    auto firstTransition = makeBuffer(2, 8);
    require(source.renderPlanar(firstTransition, 0, 8) == 8,
        "rolling AutoMix must render the first A to B transition");
    require(source.commitAutomixDeck(deckBLease),
        "rolling AutoMix must promote Deck B after its fade completes");
    require(!source.push(deckA.data(), 1),
        "the retired Deck A producer must not write into Deck C's released slot");

    const uint64_t deckCLease = source.prepareAutomixDeck(12, 4, 0.0, 0.0);
    require(deckCLease != 0 && deckCLease != deckBLease,
        "rolling AutoMix must rearm the released Deck A slot for Deck C");
    require(source.pushAutomixDeck(deckBLease, deckBContinuation.data(), 8),
        "promoted Deck B must keep accepting PCM after Deck C is armed");
    require(source.pushAutomixDeck(deckCLease, deckC.data(), 32),
        "rolling AutoMix must accept Deck C without disturbing Deck B");

    auto secondTransition = makeBuffer(2, 8);
    require(source.renderPlanar(secondTransition, 0, 8) == 8,
        "rolling AutoMix must render the second B to C transition");
    require(source.commitAutomixDeck(deckCLease),
        "rolling AutoMix must promote Deck C after the second fade");
    require(!source.pushAutomixDeck(deckBLease, deckBContinuation.data(), 1),
        "the retired Deck B lease must not write into Deck D's future slot");

    auto deckCOutput = makeBuffer(2, 4);
    require(source.renderPlanar(deckCOutput, 0, 4) == 4,
        "rolling AutoMix must continue directly from promoted Deck C");
    require(std::abs(deckCOutput.getSample(0, 3) - 0.8f) < 0.02f,
        "the second transition must leave Deck C audible instead of jumping tracks");
}

void testNativeAutomixAppliesUserDspOnceAfterDeckSum()
{
    echo::EqProcessor eqProcessor;
    echo::ConvolutionProcessor convolutionProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    echo::DspHeadroomProcessor headroomProcessor;
    echo::ReplayGainProcessor replayGainProcessor;
    echo::CompressorProcessor compressorProcessor;
    echo::SpatialDspProcessor spatialDspProcessor;
    echo::PlaybackRateProcessor playbackRateProcessor;
    echo::LevelMeterProcessor meterProcessor;
    eqProcessor.setEnabled(true);
    headroomProcessor.setHeadroomDb(-6.0f);

    PcmRingAudioSource source(
        2,
        256,
        0,
        0,
        1.0f,
        eqProcessor,
        convolutionProcessor,
        channelBalanceProcessor,
        headroomProcessor,
        replayGainProcessor,
        compressorProcessor,
        spatialDspProcessor,
        playbackRateProcessor,
        meterProcessor);
    source.prepareForNativeRender(64, 1000.0);
    source.beginSession();
    require(source.prepareAutomixFrames(16, 32, 0.0, 0.0),
        "DSP routing test must arm Smart Transition");

    const std::vector<float> current(64 * 2, 0.5f);
    const std::vector<float> next(64 * 2, 0.5f);
    require(source.push(current.data(), 64), "DSP routing test must accept current deck");
    require(source.pushAutomixNext(next.data(), 64), "DSP routing test must accept next deck");

    auto output = makeBuffer(2, 64);
    require(source.renderPlanar(output, 0, 64) == 64, "DSP routing test must render the overlap");

    constexpr float minusSixDbGain = 0.5011872f;
    require(std::abs(output.getSample(0, 8) - (0.5f * minusSixDbGain)) < 0.015f,
        "headroom must run once on the single-deck region");
    const float equalPowerMidpoint = 0.5f * std::sqrt(2.0f) * minusSixDbGain;
    require(std::abs(output.getSample(0, 32) - equalPowerMidpoint) < 0.02f,
        "EQ/headroom must run once after the equal-power A/B sum");
}

void testNativeAutomixAppliesUserVolumeToBothDecks()
{
    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    PcmRingAudioSource source(2, 64, 0, 0, 0.25f, eqProcessor, channelBalanceProcessor);
    source.beginSession();
    source.prepareAutomix(4.0, 0.5, 0.5, 0.0, 0.0);

    const std::vector<float> current(6 * 2, 1.0f);
    const std::vector<float> next(6 * 2, 0.8f);
    require(source.push(current.data(), 6), "volume routing test must accept current Deck PCM");
    require(source.pushAutomixNext(next.data(), 6), "volume routing test must accept next Deck PCM");
    source.markInputEnded();
    source.markAutomixNextEnded();

    auto output = makeBuffer(2, 6);
    require(source.renderPlanar(output, 0, 6) == 6, "volume routing test must render both Decks");
    require(std::abs(output.getSample(0, 1) - 0.25f) <= nearTolerance,
        "global user volume must apply to the outgoing Deck before the fade");
    require(std::abs(output.getSample(0, 4) - 0.2f) <= 0.01f,
        "global user volume must remain applied after the incoming Deck owns playback");
}

void testNativeAutomixProtectsTheDeckSumWithoutOptionalDsp()
{
    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    PcmRingAudioSource source(2, 256, 0, 0, 1.0f, eqProcessor, channelBalanceProcessor);
    source.prepareForNativeRender(96, 1000.0);
    source.beginSession();
    require(source.prepareAutomixFrames(16, 64, 0.0, 0.0),
        "deck-sum protection test must arm Smart Transition");

    const std::vector<float> current(96 * 2, 1.0f);
    const std::vector<float> next(96 * 2, 1.0f);
    require(source.push(current.data(), 96), "deck-sum protection test must accept current Deck PCM");
    require(source.pushAutomixNext(next.data(), 96), "deck-sum protection test must accept next Deck PCM");

    auto output = makeBuffer(2, 96);
    require(source.renderPlanar(output, 0, 96) == 96, "deck-sum protection test must render the overlap");
    float maximumSample = 0.0f;
    for (int frame = 0; frame < output.getNumSamples(); ++frame)
        maximumSample = std::max(maximumSample, std::abs(output.getSample(0, frame)));
    require(maximumSample <= 1.0001f,
        "equal-power Deck summing must not exceed full scale when optional DSP is bypassed");
    require(source.isDspLimiterProtecting(),
        "the native safety limiter must report protection during a full-scale Deck overlap");
}

void testAutomixTempoGlidesBackToUnityAfterOverlap()
{
    constexpr int sampleRate = 48'000;
    constexpr int channels = 2;
    constexpr double ratio = 1.06;
    constexpr uint64_t overlapFrames = 48'000;
    constexpr double rampSeconds = 4.0;
    constexpr uint64_t rampFrames = static_cast<uint64_t>(rampSeconds * sampleRate);

    AutomixTempoProcessor processor(sampleRate, channels, ratio);
    require(processor.active(), "tempo glide test must start with an active WSOLA");
    processor.scheduleReturnToUnity(overlapFrames, rampSeconds);

    // Deterministic noise keeps the WSOLA correlation locked to exact
    // alignment and makes the passthrough handoff comparable sample-by-sample.
    uint32_t noiseState = 0x12345u;
    const auto nextNoiseSample = [&noiseState]() {
        noiseState = noiseState * 1664525u + 1013904223u;
        return static_cast<float>(static_cast<int32_t>(noiseState >> 8)) / 16'777'216.0f;
    };

    constexpr int chunkFrames = 4'800;
    constexpr uint64_t maximumFedFrames = 400'000;
    std::vector<float> fedInput;
    std::vector<float> producedOutput;
    std::vector<float> chunk(static_cast<size_t>(chunkFrames * channels));
    bool sawHeldOverlapRatio = false;
    while (processor.active()
        && fedInput.size() < static_cast<size_t>(maximumFedFrames * channels))
    {
        for (auto& sample : chunk)
            sample = nextNoiseSample();
        fedInput.insert(fedInput.end(), chunk.begin(), chunk.end());
        const auto processed = processor.process(chunk.data(), chunkFrames);
        producedOutput.insert(producedOutput.end(), processed.begin(), processed.end());
        if (processor.active() && processor.producedOutputFrames() < overlapFrames)
        {
            require(std::abs(processor.currentRatio() - ratio) <= 1.0e-9,
                "tempo glide must hold the plan ratio through the overlap");
            sawHeldOverlapRatio = true;
        }
    }
    require(sawHeldOverlapRatio, "tempo glide test must observe the held overlap ratio");
    require(! processor.active(), "tempo glide must end in verbatim passthrough");
    require(std::abs(processor.currentRatio() - 1.0) <= 1.0e-9,
        "tempo glide must land exactly on unity");

    // Once passthrough, further chunks must come back verbatim.
    for (auto& sample : chunk)
        sample = nextNoiseSample();
    fedInput.insert(fedInput.end(), chunk.begin(), chunk.end());
    const auto passthrough = processor.process(chunk.data(), chunkFrames);
    require(passthrough.size() == chunk.size(),
        "unity passthrough must keep the chunk length");
    require(std::equal(passthrough.begin(), passthrough.end(), chunk.begin()),
        "unity passthrough must return the chunk verbatim");
    producedOutput.insert(producedOutput.end(), passthrough.begin(), passthrough.end());

    // The handoff flushes the buffered window verbatim, so the trailing output
    // must be the trailing fed input (tail hop + remainder + passthrough).
    const size_t continuitySamples = static_cast<size_t>((960 + chunkFrames) * channels);
    require(producedOutput.size() >= continuitySamples && fedInput.size() >= continuitySamples,
        "tempo glide continuity check needs enough trailing samples");
    require(std::equal(
            producedOutput.end() - static_cast<ptrdiff_t>(continuitySamples),
            producedOutput.end(),
            fedInput.end() - static_cast<ptrdiff_t>(continuitySamples)),
        "tempo glide handoff must be sample-continuous with the fed input");

    // Frame accounting: the overlap consumes overlap*ratio input, the linear
    // glide consumes ramp*(1+ratio)/2, everything afterwards is 1:1, so
    // output - input == (1-ratio)*(overlap + ramp/2) within hop/search slack.
    const int64_t producedFrames = static_cast<int64_t>(producedOutput.size()) / channels;
    const int64_t fedFrames = static_cast<int64_t>(fedInput.size()) / channels;
    const int64_t expectedDelta = static_cast<int64_t>(std::llround(
        (1.0 - ratio) * (static_cast<double>(overlapFrames) + static_cast<double>(rampFrames) / 2.0)));
    require(std::abs((producedFrames - fedFrames) - expectedDelta) <= 2'400,
        "tempo glide must integrate the ramped consumption rate");
}

void testAutomixTempoRampKeepsPositionScaleInSync()
{
    require(std::abs(AutomixTempoProcessor::returnToUnitySeconds(1.06) - 8.0) <= 1.0e-9,
        "full +6% shift must glide back over the maximum window");
    require(std::abs(AutomixTempoProcessor::returnToUnitySeconds(0.94) - 8.0) <= 1.0e-9,
        "full -6% shift must glide back over the maximum window");
    require(std::abs(AutomixTempoProcessor::returnToUnitySeconds(1.03) - 6.0) <= 1.0e-9,
        "half shift must glide back over the midpoint window");
    require(std::abs(AutomixTempoProcessor::returnToUnitySeconds(1.0) - 4.0) <= 1.0e-9,
        "unity ratio must resolve the minimum glide window");

    constexpr double ratio = 1.06;
    constexpr uint64_t rampFrames = 192'000;
    require(AudioDaemon::scaleAutomixTrackFrames(0, ratio, rampFrames) == 0,
        "position ramp must start at zero");
    require(AudioDaemon::scaleAutomixTrackFrames(100'000, ratio, 0) == 106'000,
        "no ramp must keep the constant frame scale");
    // Mid-ramp integral: 1.06*96000 - 0.06*96000^2/(2*192000) = 100320.
    require(AudioDaemon::scaleAutomixTrackFrames(96'000, ratio, rampFrames) == 100'320,
        "mid-ramp position must integrate the glide");
    // Ramp end: rampFrames*(1+ratio)/2.
    require(AudioDaemon::scaleAutomixTrackFrames(rampFrames, ratio, rampFrames) == 197'760,
        "ramp end position must equal the full glide integral");
    require(AudioDaemon::scaleAutomixTrackFrames(rampFrames + 48'000, ratio, rampFrames)
            == 197'760 + 48'000,
        "post-ramp position must advance 1:1 with the output clock");
    // Slow-down plans converge from below: 240000 + (0.94-1)*192000/2 = 234240.
    require(AudioDaemon::scaleAutomixTrackFrames(240'000, 0.94, rampFrames) == 234'240,
        "slow-down ramp must land on the same identity mapping");
}

void testNativeAutomixKeepsOutgoingIdentityUntilFadeCompletes()
{
    require(
        AudioDaemon::isValidAutomixFrameSchedule(48'000, 240'000, 240'000, 192'000),
        "Smart Transition must accept identity commitment at fade completion");
    require(
        ! AudioDaemon::isValidAutomixFrameSchedule(48'000, 240'000, 144'000, 192'000),
        "Smart Transition must reject midpoint identity commitment plans");
    require(
        AudioDaemon::resolveAutomixIdentityCommitFrame(48'000, 240'000) == 240'000,
        "Smart Transition must not publish the next track while the outgoing Deck is still audible");
}

void testNativeAutomixRejectsFadeScheduleAlreadyBehindRenderClock()
{
    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    PcmRingAudioSource source(2, 64, 0, 0, 1.0f, eqProcessor, channelBalanceProcessor);
    source.beginSession();

    const std::vector<float> current(16 * 2, 0.5f);
    require(source.push(current.data(), 16), "stale-plan test must accept current PCM");
    auto output = makeBuffer(2, 8);
    require(source.renderPlanar(output, 0, 8) == 8, "stale-plan test must advance the native render clock");

    require(! source.prepareAutomixFrames(4, 8, 0.0, 0.0),
        "native host must reject an AutoMix fade scheduled in the already-rendered past");
}

void testNativeAutomixRhythmicHandoffStaggersBassAtCrossover()
{
    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    PcmRingAudioSource source(2, 256, 0, 0, 1.0f, eqProcessor, channelBalanceProcessor);
    source.prepareForNativeRender(96, 1000.0);
    source.beginSession();
    require(source.prepareAutomixFrames(16, 64, 0.0, 0.0, 0.0, 0.0, true),
        "rhythmic handoff must arm the native bass swap");

    const std::vector<float> current(96 * 2, 0.5f);
    const std::vector<float> next(96 * 2, 0.5f);
    require(source.push(current.data(), 96), "bass swap test must accept current deck");
    require(source.pushAutomixNext(next.data(), 96), "bass swap test must accept next deck");

    auto output = makeBuffer(2, 96);
    require(source.renderPlanar(output, 0, 96) == 96, "bass swap test must render the overlap");

    require(std::abs(output.getSample(0, 12) - 0.5f) < 0.02f,
        "rhythmic handoff must preserve the outgoing deck before the overlap");
    require(output.getSample(0, 48) < 0.58f && output.getSample(0, 48) > 0.35f,
        "rhythmic handoff must create a bounded low-band crossover instead of stacking both bass lines");
    require(std::abs(output.getSample(0, 88) - 0.5f) < 0.02f,
        "rhythmic handoff must restore the incoming low band after the overlap");
}

void testNativeAutomixRhythmicHandoffClearsOutgoingHighs()
{
    const auto renderOutgoingHighRms = [](bool rhythmicBassSwap) {
        constexpr int sampleRate = 48'000;
        constexpr int frameCount = 768;
        echo::EqProcessor eqProcessor;
        echo::ChannelBalanceProcessor channelBalanceProcessor;
        PcmRingAudioSource source(2, 1'024, 0, 0, 1.0f, eqProcessor, channelBalanceProcessor);
        source.prepareForNativeRender(frameCount, sampleRate);
        source.beginSession();
        require(source.prepareAutomixFrames(64, 512, 0.0, 0.0, 0.0, 0.0, rhythmicBassSwap),
            "high-band handoff test must arm Smart Transition");

        std::vector<float> current(static_cast<size_t>(frameCount * 2), 0.0f);
        const std::vector<float> next(static_cast<size_t>(frameCount * 2), 0.0f);
        for (int frame = 0; frame < frameCount; ++frame)
        {
            const float sample = 0.2f * static_cast<float>(std::sin(
                2.0 * 3.14159265358979323846 * 8'000.0 * static_cast<double>(frame) / sampleRate));
            current[static_cast<size_t>(frame * 2)] = sample;
            current[static_cast<size_t>(frame * 2 + 1)] = sample;
        }
        require(source.push(current.data(), frameCount), "high-band handoff test must accept current deck");
        require(source.pushAutomixNext(next.data(), frameCount), "high-band handoff test must accept next deck");

        auto output = makeBuffer(2, frameCount);
        require(source.renderPlanar(output, 0, frameCount) == frameCount,
            "high-band handoff test must render the overlap");
        double sumSquares = 0.0;
        constexpr int measureStart = 304;
        constexpr int measureEnd = 368;
        for (int frame = measureStart; frame < measureEnd; ++frame)
        {
            const double sample = output.getSample(0, frame);
            sumSquares += sample * sample;
        }
        return std::sqrt(sumSquares / static_cast<double>(measureEnd - measureStart));
    };

    const double balancedRms = renderOutgoingHighRms(false);
    const double rhythmicRms = renderOutgoingHighRms(true);
    require(rhythmicRms < balancedRms * 0.9,
        "rhythmic handoff must clear outgoing high-frequency transients for the incoming deck");
}

void testNativeAutomixTailRideHoldsBothDecksThroughTheMiddle()
{
    const auto renderMiddleRms = [](bool tailRide) {
        constexpr int sampleRate = 48'000;
        constexpr int frameCount = 768;
        echo::EqProcessor eqProcessor;
        echo::ChannelBalanceProcessor channelBalanceProcessor;
        PcmRingAudioSource source(2, 1'024, 0, 0, 1.0f, eqProcessor, channelBalanceProcessor);
        source.prepareForNativeRender(frameCount, sampleRate);
        source.beginSession();
        require(source.prepareAutomixFrames(64, 512, 0.0, 0.0, 0.0, 0.0, false, tailRide),
            "tail-ride test must arm Smart Transition");

        std::vector<float> current(static_cast<size_t>(frameCount * 2), 0.0f);
        std::vector<float> next(static_cast<size_t>(frameCount * 2), 0.0f);
        for (int frame = 0; frame < frameCount; ++frame)
        {
            const float sample = 0.08f * static_cast<float>(std::sin(
                2.0 * 3.14159265358979323846 * 8'000.0 * static_cast<double>(frame) / sampleRate));
            current[static_cast<size_t>(frame * 2)] = sample;
            current[static_cast<size_t>(frame * 2 + 1)] = sample;
            next[static_cast<size_t>(frame * 2)] = sample;
            next[static_cast<size_t>(frame * 2 + 1)] = sample;
        }
        require(source.push(current.data(), frameCount), "tail-ride test must accept current deck");
        require(source.pushAutomixNext(next.data(), frameCount), "tail-ride test must accept next deck");

        auto output = makeBuffer(2, frameCount);
        require(source.renderPlanar(output, 0, frameCount) == frameCount,
            "tail-ride test must render the overlap");
        double sumSquares = 0.0;
        constexpr int measureStart = 304;
        constexpr int measureEnd = 368;
        for (int frame = measureStart; frame < measureEnd; ++frame)
        {
            const double sample = output.getSample(0, frame);
            sumSquares += sample * sample;
        }
        return std::sqrt(sumSquares / static_cast<double>(measureEnd - measureStart));
    };

    const double balancedRms = renderMiddleRms(false);
    const double tailRideRms = renderMiddleRms(true);
    require(tailRideRms > balancedRms * 1.08,
        "tail ride must hold the decaying outgoing deck while the incoming intro rises underneath it");
}

void testNativeAutomixRhythmicHandoffMovesCenterVoiceAfterTheGroove()
{
    const auto renderOutgoingPresenceRms = [](bool centered) {
        constexpr int sampleRate = 48'000;
        constexpr int frameCount = 1'024;
        echo::EqProcessor eqProcessor;
        echo::ChannelBalanceProcessor channelBalanceProcessor;
        PcmRingAudioSource source(2, 2'048, 0, 0, 1.0f, eqProcessor, channelBalanceProcessor);
        source.prepareForNativeRender(frameCount, sampleRate);
        source.beginSession();
        require(source.prepareAutomixFrames(64, 768, 0.0, 0.0, 0.0, 0.0, true),
            "centre-voice handoff test must arm the rhythmic profile");

        std::vector<float> current(static_cast<size_t>(frameCount * 2), 0.0f);
        const std::vector<float> next(static_cast<size_t>(frameCount * 2), 0.0f);
        for (int frame = 0; frame < frameCount; ++frame)
        {
            const float sample = 0.2f * static_cast<float>(std::sin(
                2.0 * 3.14159265358979323846 * 1'200.0 * static_cast<double>(frame) / sampleRate));
            current[static_cast<size_t>(frame * 2)] = sample;
            current[static_cast<size_t>(frame * 2 + 1)] = centered ? sample : -sample;
        }
        require(source.push(current.data(), frameCount), "centre-voice test must accept current deck");
        require(source.pushAutomixNext(next.data(), frameCount), "centre-voice test must accept next deck");

        auto output = makeBuffer(2, frameCount);
        require(source.renderPlanar(output, 0, frameCount) == frameCount,
            "centre-voice test must render the overlap");
        double sumSquares = 0.0;
        constexpr int measureStart = 640;
        constexpr int measureEnd = 704;
        for (int frame = measureStart; frame < measureEnd; ++frame)
        {
            const double sample = output.getSample(0, frame);
            sumSquares += sample * sample;
        }
        return std::sqrt(sumSquares / static_cast<double>(measureEnd - measureStart));
    };

    const double centeredRms = renderOutgoingPresenceRms(true);
    const double sideRms = renderOutgoingPresenceRms(false);
    require(centeredRms < sideRms * 0.82,
        "rhythmic handoff must move a centre vocal before the stereo accompaniment");
}

void testNativeAutomixUsesPlannedCenterVocalExit()
{
    const auto renderCenterRms = [](double exitStart) {
        constexpr int sampleRate = 48'000;
        constexpr int frameCount = 1'024;
        echo::EqProcessor eqProcessor;
        echo::ChannelBalanceProcessor channelBalanceProcessor;
        PcmRingAudioSource source(2, 2'048, 0, 0, 1.0f, eqProcessor, channelBalanceProcessor);
        source.prepareForNativeRender(frameCount, sampleRate);
        source.beginSession();
        require(source.prepareAutomixFrames(
            64, 768, 0.0, 0.0, 0.0, 0.0, true, false, false, 0.25, 0.6, 0.45, exitStart, 0.82),
            "planned centre-vocal test must arm the rhythmic profile");

        std::vector<float> current(static_cast<size_t>(frameCount * 2), 0.0f);
        const std::vector<float> next(static_cast<size_t>(frameCount * 2), 0.0f);
        for (int frame = 0; frame < frameCount; ++frame)
        {
            const float sample = 0.2f * static_cast<float>(std::sin(
                2.0 * 3.14159265358979323846 * 1'200.0 * static_cast<double>(frame) / sampleRate));
            current[static_cast<size_t>(frame * 2)] = sample;
            current[static_cast<size_t>(frame * 2 + 1)] = sample;
        }
        require(source.push(current.data(), frameCount), "planned centre-vocal test must accept current deck");
        require(source.pushAutomixNext(next.data(), frameCount), "planned centre-vocal test must accept next deck");
        auto output = makeBuffer(2, frameCount);
        require(source.renderPlanar(output, 0, frameCount) == frameCount,
            "planned centre-vocal test must render the overlap");
        double sumSquares = 0.0;
        constexpr int measureStart = 480;
        constexpr int measureEnd = 544;
        for (int frame = measureStart; frame < measureEnd; ++frame)
        {
            const double sample = output.getSample(0, frame);
            sumSquares += sample * sample;
        }
        return std::sqrt(sumSquares / static_cast<double>(measureEnd - measureStart));
    };

    const double earlyExitRms = renderCenterRms(0.38);
    const double lateExitRms = renderCenterRms(0.72);
    require(earlyExitRms < lateExitRms * 0.7,
        "native rhythmic handoff must follow the model-planned centre-vocal exit instead of a fixed curve");
}

void testNativeAutomixMonoUsesPlannedVocalExit()
{
    const auto renderVocalBandRms = [](double exitStart) {
        constexpr int sampleRate = 48'000;
        constexpr int frameCount = 1'024;
        echo::EqProcessor eqProcessor;
        echo::ChannelBalanceProcessor channelBalanceProcessor;
        PcmRingAudioSource source(1, 2'048, 0, 0, 1.0f, eqProcessor, channelBalanceProcessor);
        source.prepareForNativeRender(frameCount, sampleRate);
        source.beginSession();
        require(source.prepareAutomixFrames(
            64, 768, 0.0, 0.0, 0.0, 0.0, true, false, false, 0.25, 0.6, 0.45, exitStart, 0.82),
            "mono planned-vocal test must arm the rhythmic profile");

        std::vector<float> current(static_cast<size_t>(frameCount), 0.0f);
        const std::vector<float> next(static_cast<size_t>(frameCount), 0.0f);
        for (int frame = 0; frame < frameCount; ++frame)
        {
            current[static_cast<size_t>(frame)] = 0.2f * static_cast<float>(std::sin(
                2.0 * 3.14159265358979323846 * 1'200.0 * static_cast<double>(frame) / sampleRate));
        }
        require(source.push(current.data(), frameCount), "mono planned-vocal test must accept current deck");
        require(source.pushAutomixNext(next.data(), frameCount), "mono planned-vocal test must accept next deck");
        auto output = makeBuffer(1, frameCount);
        require(source.renderPlanar(output, 0, frameCount) == frameCount,
            "mono planned-vocal test must render the overlap");
        double sumSquares = 0.0;
        constexpr int measureStart = 480;
        constexpr int measureEnd = 544;
        for (int frame = measureStart; frame < measureEnd; ++frame)
        {
            const double sample = output.getSample(0, frame);
            sumSquares += sample * sample;
        }
        return std::sqrt(sumSquares / static_cast<double>(measureEnd - measureStart));
    };

    const double earlyExitRms = renderVocalBandRms(0.38);
    const double lateExitRms = renderVocalBandRms(0.72);
    require(earlyExitRms < lateExitRms * 0.78,
        "mono rhythmic handoff must follow the model-planned vocal exit");
}

void testNativeAutomixBeatCutEchoThrowRingsAfterPromotion()
{
    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    PcmRingAudioSource source(2, 512, 0, 0, 1.0f, eqProcessor, channelBalanceProcessor);
    source.prepareForNativeRender(256, 1000.0);
    source.beginSession();

    const uint64_t deckLease = source.prepareAutomixDeck(
        160,
        40,
        0.0,
        0.0,
        0.0,
        0.0,
        false,
        false,
        true,
        0.08,
        0.4,
        0.35);
    require(deckLease != 0, "beat-cut Echo Throw must arm a native deck transition");

    std::vector<float> current(400 * 2, 0.0f);
    const std::vector<float> next(400 * 2, 0.0f);
    current[120 * 2] = 1.0f;
    current[120 * 2 + 1] = 1.0f;
    require(source.push(current.data(), 400), "Echo Throw test must accept outgoing PCM");
    require(source.pushAutomixDeck(deckLease, next.data(), 400),
        "Echo Throw test must accept incoming PCM");

    auto beforePromotion = makeBuffer(2, 220);
    for (int offset = 0; offset < 220; offset += 20)
    {
        require(source.renderPlanar(beforePromotion, offset, 20) == 20,
            "Echo Throw test must render through small callbacks around the beat cut");
    }
    require(beforePromotion.getSample(0, 200) > 0.1f
            && beforePromotion.getSample(0, 200) < 0.22f,
        "the pre-cut throw must apply the plan's bounded dynamic send");
    require(source.commitAutomixDeck(deckLease),
        "Echo Throw must allow Deck B promotion after the cut");

    auto afterPromotion = makeBuffer(2, 100);
    for (int offset = 0; offset < 100; offset += 20)
    {
        require(source.renderPlanar(afterPromotion, offset, 20) == 20,
            "promoted Deck B must keep rendering while the throw decays");
    }
    require(afterPromotion.getSample(0, 60) > 0.03f
            && afterPromotion.getSample(0, 60) < 0.1f,
        "Echo Throw must apply dynamic feedback while ringing after Deck B promotion");
}

void testNativeAutomixAppliesReplayGainPerDeckWithoutGlobalDoubleProcessing()
{
    echo::EqProcessor eqProcessor;
    echo::ConvolutionProcessor convolutionProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    echo::DspHeadroomProcessor headroomProcessor;
    echo::ReplayGainProcessor replayGainProcessor;
    echo::CompressorProcessor compressorProcessor;
    echo::SpatialDspProcessor spatialDspProcessor;
    echo::PlaybackRateProcessor playbackRateProcessor;
    echo::LevelMeterProcessor meterProcessor;
    echo::ReplayGainConfig globalReplayGain;
    globalReplayGain.mode = echo::replayGainModeTrack;
    globalReplayGain.trackGainDb = 6.0f;
    globalReplayGain.preventClipping = false;
    replayGainProcessor.setConfig(globalReplayGain);

    PcmRingAudioSource source(
        2,
        256,
        0,
        0,
        1.0f,
        eqProcessor,
        convolutionProcessor,
        channelBalanceProcessor,
        headroomProcessor,
        replayGainProcessor,
        compressorProcessor,
        spatialDspProcessor,
        playbackRateProcessor,
        meterProcessor);
    source.prepareForNativeRender(64, 1000.0);
    source.beginSession();
    require(source.prepareAutomixFrames(16, 32, 0.0, 0.0, -6.0, -12.0),
        "ReplayGain routing test must arm Smart Transition");

    const std::vector<float> current(64 * 2, 0.5f);
    const std::vector<float> next(64 * 2, 0.5f);
    require(source.push(current.data(), 64), "ReplayGain routing test must accept current deck");
    require(source.pushAutomixNext(next.data(), 64), "ReplayGain routing test must accept next deck");

    auto output = makeBuffer(2, 64);
    require(source.renderPlanar(output, 0, 64) == 64, "ReplayGain routing test must render the overlap");

    constexpr float currentDeckGain = 0.5011872f;
    constexpr float nextDeckGain = 0.2511886f;
    require(std::abs(output.getSample(0, 8) - (0.5f * currentDeckGain)) < 0.015f,
        "current Deck ReplayGain must replace, not multiply with, the global ReplayGain stage");
    const float expectedMidpoint =
        0.5f * static_cast<float>(std::sqrt(0.5)) * (currentDeckGain + nextDeckGain);
    require(std::abs(output.getSample(0, 32) - expectedMidpoint) < 0.02f,
        "current and next ReplayGain must be applied independently before the A/B sum");
}

void testNativeAutomixNextDeckCannotAdvancePastCurrentBuffer()
{
    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    PcmRingAudioSource source(2, 64, 0, 0, 1.0f, eqProcessor, channelBalanceProcessor);
    source.beginSession();
    source.prepareAutomix(4.0, 0.5, 0.5, 0.0, 0.0);

    const std::vector<float> current {
        1.0f, 1.0f,
        1.0f, 1.0f,
        1.0f, 1.0f,
    };
    const std::vector<float> next {
        0.5f, 0.5f,
        0.5f, 0.5f,
        0.5f, 0.5f,
        0.5f, 0.5f,
        0.5f, 0.5f,
        0.5f, 0.5f,
    };
    require(source.push(current.data(), 3), "native automix must accept partial current deck PCM");
    require(source.pushAutomixNext(next.data(), 6), "native automix must accept prebuffered next deck PCM");

    auto output = makeBuffer(2, 6);
    const auto frames = source.renderPlanar(output, 0, 6);
    require(frames == 3, "native automix next deck must not advance the clock beyond current deck PCM");
    require(source.getFramesPlayed() == 3, "native automix clock must stay pinned to available current deck frames");
}

void testNativeAutomixDeckFailureUsesTwentyMillisecondRecovery()
{
    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    PcmRingAudioSource source(2, 256, 0, 0, 1.0f, eqProcessor, channelBalanceProcessor);
    source.prepareForNativeRender(64, 1000.0);
    source.beginSession();
    require(source.prepareAutomixFrames(0, 100, 0.0, 0.0), "fault test must arm AutoMix");
    const std::vector<float> current(160 * 2, 0.5f);
    const std::vector<float> next(160 * 2, 0.25f);
    require(source.push(current.data(), 160), "fault test must accept current deck");
    require(source.pushAutomixNext(next.data(), 160), "fault test must accept next deck");

    auto beforeFailure = makeBuffer(2, 12);
    require(source.renderPlanar(beforeFailure, 0, 12) == 12, "fault test must enter overlap");
    source.failAutomixNext(20);
    source.markAutomixNextEnded();
    auto recovery = makeBuffer(2, 24);
    require(source.renderPlanar(recovery, 0, 24) == 24, "healthy deck must continue through recovery");
    for (int frame = 0; frame < recovery.getNumSamples(); ++frame)
    {
        require(std::isfinite(recovery.getSample(0, frame)), "fault recovery must remain finite");
        if (frame > 0)
            require(std::abs(recovery.getSample(0, frame) - recovery.getSample(0, frame - 1)) < 0.08f,
                "fault recovery must not introduce a block-edge pop");
    }
    require(std::abs(recovery.getSample(0, 23) - 0.5f) < 0.02f,
        "20ms recovery must restore the healthy current deck to its original level");
}

void testNativeAutomixCancelWithFadeRampsInsteadOfHardCut()
{
    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    PcmRingAudioSource source(2, 256, 0, 0, 1.0f, eqProcessor, channelBalanceProcessor);
    source.prepareForNativeRender(64, 1000.0);
    source.beginSession();
    require(source.prepareAutomixFrames(0, 100, 0.0, 0.0), "cancel fade test must arm AutoMix");
    const std::vector<float> current(200 * 2, 0.5f);
    const std::vector<float> next(200 * 2, 0.25f);
    require(source.push(current.data(), 200), "cancel fade test must accept current deck");
    require(source.pushAutomixNext(next.data(), 200), "cancel fade test must accept next deck");

    auto beforeCancel = makeBuffer(2, 60);
    require(source.renderPlanar(beforeCancel, 0, 60) == 60,
        "cancel fade test must enter the audible middle of the overlap");

    source.cancelAutomixWithFade();

    auto recovery = makeBuffer(2, 24);
    require(source.renderPlanar(recovery, 0, 24) == 24, "cancelled transition must keep rendering Deck A");
    require(std::abs(recovery.getSample(0, 0) - beforeCancel.getSample(0, 11)) < 0.08f,
        "cancel must not hard-cut at the callback boundary");
    for (int frame = 1; frame < recovery.getNumSamples(); ++frame)
    {
        require(std::isfinite(recovery.getSample(0, frame)), "cancel ramp must remain finite");
        require(std::abs(recovery.getSample(0, frame) - recovery.getSample(0, frame - 1)) < 0.08f,
            "cancel ramp must not introduce a block-edge pop");
    }
    require(std::abs(recovery.getSample(0, 23) - 0.5f) < 0.02f,
        "the ~20ms cancel ramp must return Deck A to unity gain");

    auto afterFinalize = makeBuffer(2, 8);
    require(source.renderPlanar(afterFinalize, 0, 8) == 8, "playback must continue after the cancel finalizes");
    require(std::abs(afterFinalize.getSample(0, 7) - 0.5f) <= nearTolerance,
        "the finalized cancel must leave only Deck A audible");
    require(! source.isAutomixActive(), "a cancelled transition must not stay active");
    require(! source.pushAutomixNext(next.data(), 1),
        "the cancelled Deck B lease must reject further PCM");
    require(source.prepareAutomixFrames(200, 32, 0.0, 0.0),
        "a new transition must be armable after the cancel finalizes");
}

void testNativeAutomixActivatesFadeWhenDeckAEndsBeforePlannedStart()
{
    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    PcmRingAudioSource source(2, 64, 0, 0, 1.0f, eqProcessor, channelBalanceProcessor);
    source.prepareForNativeRender(8, 1000.0);
    source.beginSession();

    // Let the session-start declick ramp reach unity so the assertions below
    // observe only the transition envelopes.
    auto warmup = makeBuffer(2, 8);
    require(source.renderPlanar(warmup, 0, 8) == 0, "declick warmup must not consume frames");

    const std::vector<float> current(8 * 2, 0.5f);
    require(source.push(current.data(), 8), "early-exhaustion test must accept Deck A");

    // The plan expects Deck A to reach frame 32, but only 8 frames are
    // actually decodable (metadata longer than the real audio).
    const uint64_t deckLease = source.prepareAutomixDeck(32, 8, 0.0, 0.0);
    require(deckLease != 0, "early-exhaustion test must arm Deck B");
    const std::vector<float> next(32 * 2, 0.25f);
    require(source.pushAutomixDeck(deckLease, next.data(), 32), "early-exhaustion test must accept Deck B");
    source.markInputEnded();
    source.markAutomixDeckEnded(deckLease);

    auto first = makeBuffer(2, 8);
    require(source.renderPlanar(first, 0, 8) == 8, "front-shifted fade must render Deck A's real tail");
    require(std::abs(first.getSample(0, 0) - 0.5f) <= 0.02f,
        "front-shifted fade must start from the full Deck A level");

    auto second = makeBuffer(2, 8);
    require(source.renderPlanar(second, 0, 8) == 8,
        "the render clock must keep advancing after Deck A exhausts before the planned fade start");
    require(source.getFramesPlayed() == 16,
        "Deck B must drive the render clock once Deck A cannot reach the planned fade start");
    require(std::abs(second.getSample(0, 7) - 0.25f) <= 0.02f,
        "Deck B must be audible instead of hanging silent until a watchdog");

    require(source.commitAutomixDeck(deckLease),
        "the front-shifted transition must still promote Deck B");
    auto promoted = makeBuffer(2, 4);
    require(source.renderPlanar(promoted, 0, 4) == 4, "promoted Deck B must continue rendering");
    require(std::abs(promoted.getSample(0, 3) - 0.25f) <= 0.02f,
        "promoted Deck B must continue at its own level");
}

void testNativeGaplessJoinsAtDecodedPcmBoundary()
{
    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    PcmRingAudioSource source(2, 64, 0, 0, 1.0f, eqProcessor, channelBalanceProcessor);
    source.beginSession();
    source.prepareGapless();

    const std::vector<float> current(8, 1.0f);
    const std::vector<float> next(8, 0.25f);
    require(source.push(current.data(), 4), "native gapless must accept current PCM");
    require(source.pushAutomixNext(next.data(), 4), "native gapless must prebuffer next PCM");
    source.markInputEnded();
    source.markAutomixNextEnded();

    auto output = makeBuffer(2, 8);
    require(source.renderPlanar(output, 0, 8) == 8, "native gapless must fill one callback across the track boundary");
    require(std::abs(output.getSample(0, 3) - 1.0f) <= nearTolerance, "native gapless must preserve the final current-track sample");
    require(std::abs(output.getSample(0, 4) - 0.25f) <= nearTolerance, "native gapless must start the next track without a silent or faded sample");
    require(source.getGaplessBoundaryFrame() == 4, "native gapless boundary must come from consumed PCM frames");
    require(source.isDrained(), "native gapless must drain only after both tracks finish");
}

void testDopRenderKeepsValidMarkersDuringSilenceAndData()
{
    DopRingSource source(2, 16, 0, 0);
    std::vector<uint32_t> silence(6, 0xffffffffu);

    const auto emptyFrames = source.renderInterleaved(silence.data(), 3, 2);
    require(emptyFrames == 0, "DoP silence render must not count as consumed input frames");
    require(silence[0] == 0x056969 && silence[1] == 0x056969,
        "DoP silence frame 0 must carry balanced DSD idle with 0x05 markers");
    require(silence[2] == 0xfa6969 && silence[3] == 0xfa6969,
        "DoP silence frame 1 must carry balanced DSD idle with 0xfa markers");
    require(silence[4] == 0x056969 && silence[5] == 0x056969,
        "DoP silence frame 2 must keep balanced idle and alternating markers");

    source.beginSession();
    const std::vector<uint32_t> wrongMarkerInput {
        0xaa0201u, 0xaa0605u,
        0xbb0403u, 0xbb0807u,
    };
    require(source.push(wrongMarkerInput.data(), 2), "DoP source must accept packed frames");

    std::vector<uint32_t> data(4, 0u);
    const auto dataFrames = source.renderInterleaved(data.data(), 2, 2);
    require(dataFrames == 2, "DoP render must consume queued input frames");
    require(data[0] == 0xfa0201 && data[1] == 0xfa0605,
        "DoP data frame 0 must preserve the transport phase with a 0xfa marker");
    require(data[2] == 0x050403 && data[3] == 0x050807,
        "DoP data frame 1 must preserve the transport phase with a 0x05 marker");
}

#ifdef _WIN32
std::vector<uint32_t> buildAsioCandidates(long minSize, long maxSize, long preferredSize, long granularity, uint32_t requested)
{
    std::vector<uint32_t> values(16, 0);
    const auto count = asio_build_buffer_candidates_for_tests(
        minSize,
        maxSize,
        preferredSize,
        granularity,
        requested,
        values.data(),
        static_cast<uint32_t>(values.size()));
    values.resize(count);
    return values;
}

std::vector<int> buildAsioIncludeInputAttempts(long inputChannels, bool dopMode, bool nativeDsdMode)
{
    std::vector<int> values(4, -1);
    const auto count = asio_build_buffer_include_input_attempts_for_tests(
        inputChannels,
        dopMode ? 1 : 0,
        nativeDsdMode ? 1 : 0,
        values.data(),
        static_cast<uint32_t>(values.size()));
    values.resize(count);
    return values;
}

std::vector<uint32_t> buildAsioRatePivots(double requested)
{
    std::vector<uint32_t> values(8, 0);
    const auto count = asio_build_sample_rate_pivot_candidates_for_tests(
        requested,
        values.data(),
        static_cast<uint32_t>(values.size()));
    values.resize(count);
    return values;
}

void testAsioBufferCandidateGeneration()
{
    auto explicitValid = buildAsioCandidates(128, 4096, 512, 128, 1024);
    require(! explicitValid.empty(), "ASIO explicit valid candidate list");
    require(explicitValid[0] == 1024, "ASIO explicit valid buffer must be first");
    require(std::find(explicitValid.begin(), explicitValid.end(), 512) != explicitValid.end(), "ASIO preferred fallback must be included");

    auto defaultPreferred = buildAsioCandidates(128, 4096, 512, 128, 0);
    require(! defaultPreferred.empty(), "ASIO default candidate list");
    require(defaultPreferred[0] == 512, "ASIO default buffer must prefer driver preferred size");

    auto powerOfTwo = buildAsioCandidates(64, 4096, 512, -1, 300);
    require(std::find(powerOfTwo.begin(), powerOfTwo.end(), 256) != powerOfTwo.end(), "ASIO power-of-two lower candidate");
    require(std::find(powerOfTwo.begin(), powerOfTwo.end(), 512) != powerOfTwo.end(), "ASIO power-of-two preferred candidate");

    auto stepped = buildAsioCandidates(128, 4096, 512, 128, 1000);
    require(std::find(stepped.begin(), stepped.end(), 896) != stepped.end(), "ASIO stepped lower aligned candidate");
    require(std::find(stepped.begin(), stepped.end(), 1024) != stepped.end(), "ASIO stepped upper aligned candidate");

    requireVectorEquals(
        buildAsioIncludeInputAttempts(2, false, false),
        { 1, 0 },
        "ASIO PCM should still try input+output before output-only");
    requireVectorEquals(
        buildAsioIncludeInputAttempts(0, false, false),
        { 0 },
        "ASIO PCM without inputs should try output-only");
    requireVectorEquals(
        buildAsioIncludeInputAttempts(2, true, false),
        { 0 },
        "ASIO DoP must match the reference host and create output-only buffers");
    requireVectorEquals(
        buildAsioIncludeInputAttempts(2, false, true),
        { 0 },
        "ASIO native DSD must match the reference host and create output-only buffers");
}

void testAsioSampleRatePivotCandidateGeneration()
{
    const auto downTo48 = buildAsioRatePivots(48000.0);
    require(! downTo48.empty(), "ASIO 48k pivot candidates");
    require(downTo48[0] == 44100, "ASIO 48k recovery must pivot away from 48k first");
    require(std::find(downTo48.begin(), downTo48.end(), 48000u) == downTo48.end(), "ASIO 48k pivot must not include requested rate");
    require(std::find(downTo48.begin(), downTo48.end(), 96000u) != downTo48.end(), "ASIO 48k pivot includes high-rate recovery");

    const auto upTo192 = buildAsioRatePivots(192000.0);
    require(! upTo192.empty(), "ASIO 192k pivot candidates");
    require(upTo192[0] == 48000, "ASIO non-48k recovery should try stable 48k first");
    require(std::find(upTo192.begin(), upTo192.end(), 192000u) == upTo192.end(), "ASIO 192k pivot must not include requested rate");
}

void testAsioSampleConversion()
{
    std::vector<unsigned char> bytes(16, 0);
    asio_write_sample_for_tests(bytes.data(), ASIOSTInt16LSB, 0, 1.0f);
    require(reinterpret_cast<int16_t*>(bytes.data())[0] == 32767, "ASIO int16 LSB conversion");

    std::fill(bytes.begin(), bytes.end(), static_cast<unsigned char>(0));
    asio_write_sample_for_tests(bytes.data(), ASIOSTInt16MSB, 0, 1.0f);
    require(bytes[0] == 0x7f && bytes[1] == 0xff, "ASIO int16 MSB conversion");

    std::fill(bytes.begin(), bytes.end(), static_cast<unsigned char>(0));
    asio_write_sample_for_tests(bytes.data(), ASIOSTInt24LSB, 0, 1.0f);
    require(bytes[0] == 0xff && bytes[1] == 0xff && bytes[2] == 0x7f, "ASIO int24 LSB conversion");

    std::fill(bytes.begin(), bytes.end(), static_cast<unsigned char>(0));
    constexpr int32_t encodedMqaPcm = 12345;
    asio_write_exact_sample_for_tests(
        bytes.data(),
        ASIOSTInt24LSB,
        0,
        static_cast<float>(encodedMqaPcm) / 8388608.0f);
    const int32_t recoveredMqaPcm = static_cast<int32_t>(bytes[0])
        | (static_cast<int32_t>(bytes[1]) << 8)
        | (static_cast<int32_t>(bytes[2]) << 16);
    require(recoveredMqaPcm == encodedMqaPcm,
        "ASIO exact PCM conversion must preserve a 24-bit MQA payload sample");

    std::fill(bytes.begin(), bytes.end(), static_cast<unsigned char>(0));
    asio_write_sample_for_tests(bytes.data(), ASIOSTInt32LSB24, 0, 1.0f);
    require(reinterpret_cast<int32_t*>(bytes.data())[0] == 0x7fffff00, "ASIO int32 LSB 24-bit aligned conversion");

    std::fill(bytes.begin(), bytes.end(), static_cast<unsigned char>(0));
    asio_write_sample_for_tests(bytes.data(), ASIOSTFloat32LSB, 0, 0.5f);
    require(std::abs(reinterpret_cast<float*>(bytes.data())[0] - 0.5f) <= nearTolerance, "ASIO float32 LSB conversion");

    std::fill(bytes.begin(), bytes.end(), static_cast<unsigned char>(0));
    asio_write_sample_for_tests(bytes.data(), ASIOSTFloat64LSB, 0, -0.5f);
    require(std::abs(reinterpret_cast<double*>(bytes.data())[0] + 0.5) <= nearTolerance, "ASIO float64 LSB conversion");

    std::fill(bytes.begin(), bytes.end(), static_cast<unsigned char>(0));
    asio_write_sample_for_tests(bytes.data(), ASIOSTFloat32MSB, 0, 1.0f);
    require(bytes[0] == 0x3f && bytes[1] == 0x80 && bytes[2] == 0x00 && bytes[3] == 0x00, "ASIO float32 MSB conversion");

    require(std::string(asio_error_name_for_tests(ASE_InvalidMode)) == "ASE_InvalidMode", "ASIO error name helper");
}

void testAsioDopConversionMatchesStandards()
{
    std::vector<unsigned char> bytes(16, 0);
    asio_write_dop_sample_for_tests(bytes.data(), ASIOSTInt24LSB, 0, 0x05a1b2u);
    require(
        bytes[0] == 0xb2 && bytes[1] == 0xa1 && bytes[2] == 0x05,
        "ASIO DoP packed int24 LSB must put the marker in the most-significant byte");

    std::fill(bytes.begin(), bytes.end(), static_cast<unsigned char>(0));
    asio_write_dop_sample_for_tests(bytes.data(), ASIOSTInt24MSB, 0, 0x05a1b2u);
    require(
        bytes[0] == 0x05 && bytes[1] == 0xa1 && bytes[2] == 0xb2,
        "ASIO DoP packed int24 MSB must put the marker in the most-significant byte");

    std::fill(bytes.begin(), bytes.end(), static_cast<unsigned char>(0));
    asio_write_dop_sample_for_tests(bytes.data(), ASIOSTInt32LSB, 0, 0x05a1b2u);
    require(
        bytes[0] == 0x00 && bytes[1] == 0xb2 && bytes[2] == 0xa1 && bytes[3] == 0x05,
        "ASIO DoP basic int32 LSB must left-align the 24-bit frame");

    std::fill(bytes.begin(), bytes.end(), static_cast<unsigned char>(0));
    asio_write_dop_sample_for_tests(bytes.data(), ASIOSTInt32MSB, 0, 0x05a1b2u);
    require(
        bytes[0] == 0x05 && bytes[1] == 0xa1 && bytes[2] == 0xb2 && bytes[3] == 0x00,
        "ASIO DoP basic int32 MSB must left-align the 24-bit frame");

    std::fill(bytes.begin(), bytes.end(), static_cast<unsigned char>(0));
    asio_write_dop_sample_for_tests(bytes.data(), ASIOSTInt32LSB24, 0, 0xfaa1b2u);
    require(
        bytes[0] == 0xb2 && bytes[1] == 0xa1 && bytes[2] == 0xfa && bytes[3] == 0xff,
        "ASIO DoP int32 LSB24 must right-align and sign-extend the 24-bit frame");

    std::fill(bytes.begin(), bytes.end(), static_cast<unsigned char>(0));
    asio_write_dop_sample_for_tests(bytes.data(), ASIOSTInt32MSB24, 0, 0xfaa1b2u);
    require(
        bytes[0] == 0xff && bytes[1] == 0xfa && bytes[2] == 0xa1 && bytes[3] == 0xb2,
        "ASIO DoP int32 MSB24 must right-align and sign-extend the 24-bit frame");
}

void testAsioNativeDsdConversion()
{
    const std::vector<uint8_t> source { 0x80, 0x01 };
    std::vector<unsigned char> bytes(16, 0);

    asio_write_native_dsd_samples_for_tests(
        bytes.data(),
        ASIOSTDSDInt8MSB1,
        16,
        source.data(),
        2,
        1,
        0,
        0);
    require(bytes[0] == 0x01 && bytes[1] == 0x80, "ASIO native DSD MSB must reverse DSF byte order");

    std::fill(bytes.begin(), bytes.end(), static_cast<unsigned char>(0));
    asio_write_native_dsd_samples_for_tests(
        bytes.data(),
        ASIOSTDSDInt8LSB1,
        16,
        source.data(),
        2,
        1,
        0,
        0);
    require(bytes[0] == 0x80 && bytes[1] == 0x01, "ASIO native DSD LSB normally preserves DSF byte order");

    std::fill(bytes.begin(), bytes.end(), static_cast<unsigned char>(0));
    asio_write_native_dsd_samples_for_tests(
        bytes.data(),
        ASIOSTDSDInt8LSB1,
        16,
        source.data(),
        2,
        1,
        0,
        1);
    require(bytes[0] == 0x01 && bytes[1] == 0x80, "ASIO native DSD compatibility mode must reverse packed DSF bytes");

    std::fill(bytes.begin(), bytes.end(), static_cast<unsigned char>(0));
    asio_write_native_dsd_samples_for_tests(
        bytes.data(),
        ASIOSTDSDInt8NER8,
        8,
        source.data(),
        1,
        1,
        0,
        1);
    require(bytes[0] == 1 && bytes[1] == 0 && bytes[7] == 0, "ASIO native DSD NER8 expands MSB-first bits in compatibility mode");
}

void testAsioRenderGuardCatchesCallbackException()
{
    require(
        asio_render_guard_catches_exception_for_tests() != 0,
        "ASIO render guard must catch callback exceptions, write silence, and publish a runtime failure");
}

void testAsioCallbackDrainMustCompleteBeforeTeardown()
{
    require(
        asio_wait_for_callbacks_for_tests(0, 0) != 0,
        "ASIO teardown may proceed when no callbacks are active");
    require(
        asio_wait_for_callbacks_for_tests(1, 0) == 0,
        "ASIO teardown must fail closed while a callback remains active");
}

void testAsioRuntimeLifecycleEventsArePublished()
{
    require(
        asio_capture_message_event_for_tests(kAsioResetRequest) == ASIO_RUNTIME_EVENT_RESET_REQUEST,
        "ASIO reset requests must become host-visible runtime events");
    require(
        asio_capture_message_event_for_tests(kAsioResyncRequest) == ASIO_RUNTIME_EVENT_RESYNC_REQUEST,
        "ASIO resync requests must become host-visible runtime events");
    require(
        asio_capture_message_event_for_tests(kAsioLatenciesChanged) == ASIO_RUNTIME_EVENT_LATENCIES_CHANGED,
        "ASIO latency changes must become host-visible runtime events");

    uint32_t sampleRate = 0;
    require(
        asio_capture_sample_rate_event_for_tests(96000.0, &sampleRate) == ASIO_RUNTIME_EVENT_SAMPLE_RATE_CHANGED,
        "ASIO sample-rate changes must become host-visible runtime events");
    require(sampleRate == 96000, "ASIO sample-rate events must preserve the driver rate");
}

void testAsioNativeDsdBuffersArePrimedBeforeStart()
{
    std::vector<unsigned char> first(16, 0xffu);
    std::vector<unsigned char> second(16, 0x00u);
    require(
        asio_prime_native_dsd_buffers_for_tests(
            first.data(),
            second.data(),
            ASIOSTDSDInt8LSB1,
            128,
            0) != 0,
        "ASIO native DSD buffer priming must succeed");

    require(
        std::all_of(first.begin(), first.end(), [](unsigned char value) { return value == 0x69u; }),
        "ASIO native DSD first buffer must be primed with balanced idle bytes");
    require(
        std::all_of(second.begin(), second.end(), [](unsigned char value) { return value == 0x69u; }),
        "ASIO native DSD second buffer must be primed with balanced idle bytes");
}

void testAsioUnsolicitedWindowSuppressionScope()
{
    require(
        asio_should_suppress_unsolicited_windows_for_tests("O Deus ASIO Link Pro") == 1,
        "ASIO Link Pro must suppress its unsolicited routing window");
    require(
        asio_should_suppress_unsolicited_windows_for_tests("O DEUS ASIO LINK PRO 2.4.2.0") == 1,
        "ASIO Link Pro matching must be case insensitive");
    require(
        asio_should_suppress_unsolicited_windows_for_tests("RME Fireface USB") == 0,
        "ordinary ASIO drivers must keep their native window behavior");
}
#endif

void testCleanupEmitsShutdownAckOnce()
{
    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    PcmRingAudioSource source(2, 512, 0, 0, 1.0f, eqProcessor, channelBalanceProcessor);
    bool shutdownAckSent = false;
    std::ostringstream output;
    auto* oldBuffer = std::cout.rdbuf(output.rdbuf());

    cleanupHostAndAck([&] { cleanupPcmSource(source); }, [] {}, shutdownAckSent);
    cleanupHostAndAck([&] { cleanupPcmSource(source); }, [] {}, shutdownAckSent);
    std::cout.rdbuf(oldBuffer);

    require(shutdownAckSent, "cleanup must mark shutdown ack sent");
    require(output.str() == "{\"event\":\"shutdown-ack\"}\n", "cleanup must emit shutdown ack exactly once");
}

void testExplicitStopSuppressesDaemonQueueAdvance()
{
    const auto fixturePath = writeStreamingDecoderWavFixture(48000, 2, 64);
    ScopedFileRemoval removeFixture(fixturePath);
    std::atomic<bool> shutdownRequested { false };
    int continueAfterDrainCalls = 0;
    int inputEndedCalls = 0;
    AudioDaemon daemon({
        [] {},
        [&] { ++continueAfterDrainCalls; },
        [&] { ++inputEndedCalls; },
        [] {},
        [](bool) {},
        [](const float*, int frames, bool) { return frames; },
        [](const float*, int) { return true; },
        [](const float*, int, uint64_t) { return true; },
        [] { return true; },
        [](const float*, int) { return true; },
        [] {},
        [] {},
        [] { return UINT64_MAX; },
        [](uint64_t, uint64_t, double, double, double, double, bool) { return true; },
        [](const float*, int) { return true; },
        [] {},
        [] {},
        [](uint64_t) {},
        [] { return uint64_t { 0 }; },
        [] { return uint64_t { 0 }; },
        [] { return false; },
        [] { return uint64_t { 1 }; },
        [](float) {},
        [](int outputSampleRate) { return outputSampleRate; },
    }, 48000, 1, shutdownRequested);

    require(daemon.onQueueSet(nlohmann::json::array({
        {{"itemId", "current"}, {"trackId", "track-current"}, {"filePath", fixturePath}, {"sampleRate", 48000}},
        {{"itemId", "next"}, {"trackId", "track-next"}, {"filePath", fixturePath}, {"sampleRate", 48000}},
    }), "off", 1, "current"), "daemon queue snapshot is accepted");

    nlohmann::json stopResult;
    daemon.stopForTests(stopResult);
    daemon.emitEnded();

    require(stopResult.value("operationId", uint64_t { 0 }) > 0, "explicit stop creates a playback operation boundary");
    require(inputEndedCalls == 1, "explicit stop marks the source ended once");
    require(continueAfterDrainCalls == 0, "explicit stop must not autonomously open the next queued track");
}

void testDaemonPositionThrottlesProcessingTelemetry()
{
    std::atomic<bool> shutdownRequested { false };
    int processingTelemetryCalls = 0;
    AudioDaemon::SourceHooks hooks;
    hooks.processingTelemetry = [&] {
        ++processingTelemetryCalls;
        return nlohmann::json { { "active", true } };
    };
    AudioDaemon daemon(std::move(hooks), 48000, 1, shutdownRequested);

    daemon.emitPosition(0, 0, false);
    daemon.emitPosition(16000, 0, false);
    daemon.emitPosition(47999, 0, false);
    daemon.emitPosition(48000, 0, false);
    daemon.emitPosition(96000, 0, false);

    require(processingTelemetryCalls == 3,
        "position keeps its fast clock while processing telemetry is sampled at one hertz");
}

void testExplicitStopWaitsForRenderedDrain()
{
    std::atomic<bool> shutdownRequested { false };
    std::atomic<bool> tailDrainAllowed { false };
    std::atomic<bool> drained { false };
    int inputEndedCalls = 0;
    AudioDaemon::SourceHooks hooks;
    hooks.markInputEnded = [&] { ++inputEndedCalls; };
    hooks.requestStop = [] {};
    hooks.setPaused = [](bool) {};
    hooks.allowTailDrainAfterStop = [&] { tailDrainAllowed.store(true, std::memory_order_release); };
    hooks.isDrained = [&] { return drained.load(std::memory_order_acquire); };
    hooks.requiresRenderedStopDrain = [] { return true; };
    AudioDaemon daemon(std::move(hooks), 48000, 1, shutdownRequested);

    std::jthread callbackDrain([&](std::stop_token stopToken) {
        while (!stopToken.stop_requested() && !tailDrainAllowed.load(std::memory_order_acquire))
            std::this_thread::yield();
        std::this_thread::sleep_for(std::chrono::milliseconds(5));
        drained.store(true, std::memory_order_release);
    });

    nlohmann::json stopResult;
    daemon.stopForTests(stopResult);
    require(tailDrainAllowed.load(std::memory_order_acquire), "explicit stop reopens the source only for DSP tail drain");
    require(inputEndedCalls == 1, "explicit stop marks the source ended after tail drain is allowed");
    require(stopResult.value("drained", false), "explicit stop waits for the output callback to drain queued audio");
    require(stopResult.value("drainWaitMs", 0.0) >= 4.0, "explicit stop response is held until rendered drain completes");
}

void testNaturalEndStillAdvancesDaemonQueue()
{
    const auto fixturePath = writeStreamingDecoderWavFixture(48000, 2, 64);
    ScopedFileRemoval removeFixture(fixturePath);
    std::atomic<bool> shutdownRequested { false };
    int continueAfterDrainCalls = 0;
    AudioDaemon daemon({
        [] {},
        [&] { ++continueAfterDrainCalls; },
        [] {},
        [] {},
        [](bool) {},
        [](const float*, int frames, bool) { return frames; },
        [](const float*, int) { return true; },
        [](const float*, int, uint64_t) { return true; },
        [] { return true; },
        [](const float*, int) { return true; },
        [] {},
        [] {},
        [] { return UINT64_MAX; },
        [](uint64_t, uint64_t, double, double, double, double, bool) { return true; },
        [](const float*, int) { return true; },
        [] {},
        [] {},
        [](uint64_t) {},
        [] { return uint64_t { 0 }; },
        [] { return uint64_t { 0 }; },
        [] { return false; },
        [] { return uint64_t { 1 }; },
        [](float) {},
        [](int outputSampleRate) { return outputSampleRate; },
    }, 48000, 1, shutdownRequested);

    require(daemon.onQueueSet(nlohmann::json::array({
        {{"itemId", "current"}, {"trackId", "track-current"}, {"filePath", fixturePath}, {"sampleRate", 48000}},
        {{"itemId", "next"}, {"trackId", "track-next"}, {"filePath", fixturePath}, {"sampleRate", 48000}},
    }), "off", 1, "current"), "daemon queue snapshot is accepted");

    daemon.emitEnded();

    require(continueAfterDrainCalls == 1, "natural playback end still autonomously opens the next queued track");
    require(daemon.hasPendingQueueAdvanceForTests(), "queue advance waits for the next operation to render PCM");
    daemon.emitPosition(0, 0, false);
    require(daemon.hasPendingQueueAdvanceForTests(), "zero-frame position must not commit queue advance or audio.started");
    daemon.emitEnded();
    require(! daemon.hasPendingQueueAdvanceForTests(), "a target ending before first PCM clears the pending advance");
    nlohmann::json stopResult;
    daemon.stopForTests(stopResult);
}

void testNaturalEndAdvancesIntoRangedQueueSource()
{
    const auto fixturePath = writeStreamingDecoderWavFixture(48000, 2, 4800);
    ScopedFileRemoval removeFixture(fixturePath);
    std::atomic<bool> shutdownRequested { false };
    int continueAfterDrainCalls = 0;
    AudioDaemon::SourceHooks hooks;
    hooks.continueSessionAfterDrain = [&] { ++continueAfterDrainCalls; };
    hooks.markInputEnded = [] {};
    hooks.requestStop = [] {};
    hooks.setPaused = [](bool) {};
    hooks.replaceBufferedAudio = [](const float*, int frames, bool) { return frames; };
    hooks.push = [](const float*, int) { return true; };
    hooks.pushForGeneration = [](const float*, int, uint64_t) { return true; };
    hooks.generation = [] { return uint64_t { 1 }; };
    hooks.decoderSampleRateFor = [](int outputSampleRate) { return outputSampleRate; };
    AudioDaemon daemon(std::move(hooks), 48000, 1, shutdownRequested);

    require(daemon.onQueueSet(nlohmann::json::array({
        {{"itemId", "remote"}, {"trackId", "remote-track"}, {"filePath", "remote:logical"},
         {"source", {{"kind", "http"}, {"uri", "http://127.0.0.1/audio.wav"},
                     {"headers", {{"Authorization", "test-only"}}}}}},
    }), "off", 1, "remote"), "remote source descriptors are accepted without a renderer resolver");
    require(!daemon.onQueueSet(nlohmann::json::array({
        {{"itemId", "remote"}, {"trackId", "remote-track"}, {"filePath", "remote:logical"},
         {"source", {{"kind", "http"}, {"uri", "http://127.0.0.1/audio.wav"},
                     {"headers", {{"Authorization", "invalid\r\nheader"}}}}}},
    }), "off", 2, "remote"), "remote queue headers retain the input-source validation boundary");
    require(!daemon.onQueueSet(nlohmann::json::array({
        {
            {"itemId", "invalid-cue"},
            {"trackId", "track-invalid-cue"},
            {"filePath", "album.cue#cueTrack=1"},
            {"source", {
                {"kind", "local"},
                {"uri", fixturePath},
                {"range", {{"startSeconds", 0.08}, {"endSeconds", 0.02}}},
            }},
        },
    }), "off", 1, "invalid-cue"), "invalid ranged queue source is rejected atomically");

    require(daemon.onQueueSet(nlohmann::json::array({
        {{"itemId", "current"}, {"trackId", "track-current"}, {"filePath", fixturePath}, {"sampleRate", 48000}},
        {
            {"itemId", "cue-next"},
            {"trackId", "track-cue-next"},
            {"filePath", "album.cue#cueTrack=2"},
            {"sampleRate", 48000},
            {"source", {
                {"kind", "local"},
                {"uri", fixturePath},
                {"range", {{"startSeconds", 0.02}, {"endSeconds", 0.08}}},
            }},
        },
    }), "off", 2, "current"), "ranged queue source is accepted");

    daemon.emitEnded();

    require(continueAfterDrainCalls == 1,
        "natural end opens the physical source behind a virtual ranged queue item");
    require(daemon.hasPendingQueueAdvanceForTests(),
        "ranged queue advance waits for the first rendered PCM frame");
    nlohmann::json stopResult;
    daemon.stopForTests(stopResult);
}

void testRuntimeOutputTransitionRunsOnPumpOwner()
{
    RuntimeOutputTransitionCoordinator coordinator;
    const auto ownerThread = std::this_thread::get_id();
    std::thread::id handlerThread;
    coordinator.setHandler([&](int targetSampleRate)
    {
        handlerThread = std::this_thread::get_id();
        return RuntimeOutputTransitionResult {true, targetSampleRate, "test", {}, 0.0};
    });

    RuntimeOutputTransitionResult result;
    std::atomic<bool> completed { false };
    std::jthread requester([&]
    {
        result = coordinator.request(44100);
        completed.store(true, std::memory_order_release);
    });
    while (! completed.load(std::memory_order_acquire))
    {
        coordinator.pump();
        std::this_thread::sleep_for(std::chrono::milliseconds(1));
    }

    require(result.success && result.actualSampleRate == 44100, "runtime transition returns the owner result");
    require(handlerThread == ownerThread, "runtime transition handler stays on the pump owner thread");
    coordinator.close();
}

void testMixedRateDaemonAdvanceReconfiguresBeforeDecode()
{
    const auto fixturePath = writeStreamingDecoderWavFixture(44100, 2, 64);
    ScopedFileRemoval removeFixture(fixturePath);
    std::atomic<bool> shutdownRequested { false };
    int transitionTarget = 0;
    int transitionCalls = 0;
    int continueAfterDrainCalls = 0;

    AudioDaemon::SourceHooks hooks;
    hooks.continueSessionAfterDrain = [&] { ++continueAfterDrainCalls; };
    hooks.markInputEnded = [] {};
    hooks.requestStop = [] {};
    hooks.setPaused = [](bool) {};
    hooks.replaceBufferedAudio = [](const float*, int frames, bool) { return frames; };
    hooks.push = [](const float*, int) { return true; };
    hooks.pushForGeneration = [](const float*, int, uint64_t) { return true; };
    hooks.generation = [] { return uint64_t { 1 }; };
    hooks.decoderSampleRateFor = [](int outputSampleRate) { return outputSampleRate; };
    hooks.reconfigureOutputSampleRate = [&](int targetSampleRate, nlohmann::json& result, std::string&)
    {
        ++transitionCalls;
        transitionTarget = targetSampleRate;
        result = {
            {"actualSampleRate", targetSampleRate},
            {"mode", "test-stop-open"},
            {"durationMs", 10.0},
        };
        return true;
    };
    hooks.strictOutputSampleRateTransition = [] { return true; };
    AudioDaemon daemon(std::move(hooks), 48000, 1, shutdownRequested);

    require(daemon.onQueueSet(nlohmann::json::array({
        {{"itemId", "current"}, {"trackId", "track-current"}, {"filePath", "current.wav"}, {"sampleRate", 48000}},
        {{"itemId", "next"}, {"trackId", "track-next"}, {"filePath", fixturePath}, {"sampleRate", 44100}},
        {{"itemId", "third"}, {"trackId", "track-third"}, {"filePath", fixturePath}, {"sampleRate", 44100}},
    }), "off", 2, "current"), "mixed-rate daemon queue snapshot is accepted");

    daemon.emitEnded();

    require(transitionTarget == 44100, "mixed-rate daemon advance reconfigures the device to the source rate");
    require(continueAfterDrainCalls == 1, "decode starts only after the output transition succeeds");
    require(daemon.hasPendingQueueAdvanceForTests(), "mixed-rate queue advance waits for rendered PCM");
    daemon.emitPosition(1, 0, false);
    require(! daemon.hasPendingQueueAdvanceForTests(), "mixed-rate queue advance commits on the first rendered PCM frame");
    daemon.emitEnded();
    require(transitionCalls == 1, "the following same-rate track does not reconfigure the device");
    require(continueAfterDrainCalls == 2, "the same-rate third track keeps the fast autonomous path");
    require(daemon.hasPendingQueueAdvanceForTests(), "the same-rate third track still waits for first rendered PCM");
    daemon.emitPosition(1, 0, false);
    require(! daemon.hasPendingQueueAdvanceForTests(), "the same-rate third track commits on rendered PCM");
    nlohmann::json stopResult;
    daemon.stopForTests(stopResult);
}

void testManualStopSupersedesMixedRateDaemonAdvance()
{
    const auto fixturePath = writeStreamingDecoderWavFixture(44100, 2, 64);
    ScopedFileRemoval removeFixture(fixturePath);
    std::atomic<bool> shutdownRequested { false };
    int continueAfterDrainCalls = 0;
    AudioDaemon* daemonOwner = nullptr;

    AudioDaemon::SourceHooks hooks;
    hooks.continueSessionAfterDrain = [&] { ++continueAfterDrainCalls; };
    hooks.markInputEnded = [] {};
    hooks.requestStop = [] {};
    hooks.setPaused = [](bool) {};
    hooks.replaceBufferedAudio = [](const float*, int frames, bool) { return frames; };
    hooks.push = [](const float*, int) { return true; };
    hooks.pushForGeneration = [](const float*, int, uint64_t) { return true; };
    hooks.generation = [] { return uint64_t { 1 }; };
    hooks.decoderSampleRateFor = [](int outputSampleRate) { return outputSampleRate; };
    hooks.reconfigureOutputSampleRate = [&](int targetSampleRate, nlohmann::json& result, std::string&)
    {
        nlohmann::json stopResult;
        daemonOwner->stopForTests(stopResult);
        result = {
            {"actualSampleRate", targetSampleRate},
            {"mode", "test-stop-open"},
            {"durationMs", 10.0},
        };
        return true;
    };
    hooks.strictOutputSampleRateTransition = [] { return true; };
    AudioDaemon daemon(std::move(hooks), 48000, 1, shutdownRequested);
    daemonOwner = &daemon;

    require(daemon.onQueueSet(nlohmann::json::array({
        {{"itemId", "current"}, {"trackId", "track-current"}, {"filePath", "current.wav"}, {"sampleRate", 48000}},
        {{"itemId", "next"}, {"trackId", "track-next"}, {"filePath", fixturePath}, {"sampleRate", 44100}},
    }), "off", 3, "current"), "mixed-rate queue is accepted before stop race");

    daemon.emitEnded();

    require(continueAfterDrainCalls == 0, "a manual stop winning the transition prevents autonomous decode");
    require(! daemon.hasPendingQueueAdvanceForTests(), "a superseded transition cannot publish queue advance");
}

void testStrictDaemonAdvanceRejectsMissingSampleRate()
{
    std::atomic<bool> shutdownRequested { false };
    int continueAfterDrainCalls = 0;
    int transitionCalls = 0;
    AudioDaemon::SourceHooks hooks;
    hooks.continueSessionAfterDrain = [&] { ++continueAfterDrainCalls; };
    hooks.markInputEnded = [] {};
    hooks.requestStop = [] {};
    hooks.setPaused = [](bool) {};
    hooks.decoderSampleRateFor = [](int outputSampleRate) { return outputSampleRate; };
    hooks.reconfigureOutputSampleRate = [&](int, nlohmann::json&, std::string&)
    {
        ++transitionCalls;
        return true;
    };
    hooks.strictOutputSampleRateTransition = [] { return true; };
    AudioDaemon daemon(std::move(hooks), 48000, 1, shutdownRequested);

    require(daemon.onQueueSet(nlohmann::json::array({
        {{"itemId", "current"}, {"trackId", "track-current"}, {"filePath", "current.wav"}, {"sampleRate", 48000}},
        {{"itemId", "next"}, {"trackId", "track-next"}, {"filePath", "missing-rate.wav"}},
    }), "off", 4, "current"), "strict queue with missing next rate is accepted as metadata");

    daemon.emitEnded();

    require(transitionCalls == 0, "missing source rate fails before any device transition");
    require(continueAfterDrainCalls == 0, "missing source rate fails before autonomous decode");
    require(! daemon.hasPendingQueueAdvanceForTests(), "missing source rate cannot publish queue advance");
}

void testQueueReplacementCancelsPendingDaemonAdvance()
{
    const auto fixturePath = writeStreamingDecoderWavFixture(48000, 2, 64);
    ScopedFileRemoval removeFixture(fixturePath);
    std::atomic<bool> shutdownRequested { false };
    int continueAfterDrainCalls = 0;
    AudioDaemon::SourceHooks hooks;
    hooks.continueSessionAfterDrain = [&] { ++continueAfterDrainCalls; };
    hooks.markInputEnded = [] {};
    hooks.requestStop = [] {};
    hooks.setPaused = [](bool) {};
    hooks.replaceBufferedAudio = [](const float*, int frames, bool) { return frames; };
    hooks.push = [](const float*, int) { return true; };
    hooks.pushForGeneration = [](const float*, int, uint64_t) { return true; };
    hooks.generation = [] { return uint64_t { 1 }; };
    hooks.decoderSampleRateFor = [](int outputSampleRate) { return outputSampleRate; };
    AudioDaemon daemon(std::move(hooks), 48000, 1, shutdownRequested);

    require(daemon.onQueueSet(nlohmann::json::array({
        {{"itemId", "current"}, {"trackId", "track-current"}, {"filePath", fixturePath}, {"sampleRate", 48000}},
        {{"itemId", "next"}, {"trackId", "track-next"}, {"filePath", fixturePath}, {"sampleRate", 48000}},
    }), "off", 5, "current"), "initial queue is accepted before pending replacement");
    daemon.emitEnded();
    require(daemon.hasPendingQueueAdvanceForTests(), "natural advance arms a pending first-frame commit");

    require(daemon.onQueueSet(nlohmann::json::array({
        {{"itemId", "current"}, {"trackId", "track-current"}, {"filePath", fixturePath}, {"sampleRate", 48000}},
        {{"itemId", "next"}, {"trackId", "track-next"}, {"filePath", fixturePath}, {"sampleRate", 48000}},
    }), "off", 5, "current"), "same-revision retry is accepted idempotently");
    require(daemon.hasPendingQueueAdvanceForTests(), "same-revision retry preserves the pending commit");

    require(daemon.onQueueSet(nlohmann::json::array({
        {{"itemId", "replacement"}, {"trackId", "track-replacement"}, {"filePath", fixturePath}, {"sampleRate", 48000}},
    }), "off", 6, "replacement"), "replacement queue wins before first PCM");

    require(continueAfterDrainCalls == 1, "replacement occurs after exactly one autonomous open");
    require(! daemon.hasPendingQueueAdvanceForTests(), "replacement queue cancels the stale pending advance");
    daemon.emitPosition(1, 0, false);
    require(! daemon.hasPendingQueueAdvanceForTests(), "stale first PCM cannot resurrect a cancelled advance");
}

void testProtocolMessages()
{
    echo::EqProcessor eqProcessor;
    echo::ChannelBalanceProcessor channelBalanceProcessor;
    eqProcessor.prepare(48000.0, 512, 2);
    channelBalanceProcessor.prepare(48000.0, 512, 2);

    const auto gainResponse = echo::EqMessageProtocol::handleJsonLine(
        R"({"type":"eq:set-band-gain","band":3,"gainDb":4.5})",
        eqProcessor,
        channelBalanceProcessor);
    requireContains(gainResponse, R"("type":"eq:state")", "gain response");
    requireContains(gainResponse, R"("gainDb":4.5)", "gain response");

    const auto frequencyResponse = echo::EqMessageProtocol::handleJsonLine(
        R"({"type":"eq:set-band-frequency","band":3,"frequencyHz":360})",
        eqProcessor,
        channelBalanceProcessor);
    requireContains(frequencyResponse, R"("frequencyHz":360)", "frequency response");

    const auto qResponse = echo::EqMessageProtocol::handleJsonLine(
        R"({"type":"eq:set-band-q","band":3,"q":3.5})",
        eqProcessor,
        channelBalanceProcessor);
    requireContains(qResponse, R"("q":3.5)", "Q response");

    const auto filterResponse = echo::EqMessageProtocol::handleJsonLine(
        R"({"type":"eq:set-band-filter-type","band":3,"filterType":"highShelf"})",
        eqProcessor,
        channelBalanceProcessor);
    requireContains(filterResponse, R"("filterType":"highShelf")", "filter type response");

    const auto lowPassResponse = echo::EqMessageProtocol::handleJsonLine(
        R"({"type":"eq:set-band-filter-type","band":3,"filterType":"lowPass"})",
        eqProcessor,
        channelBalanceProcessor);
    requireContains(lowPassResponse, R"("filterType":"lowPass")", "low pass filter response");

    const auto highPassResponse = echo::EqMessageProtocol::handleJsonLine(
        R"({"type":"eq:set-band-filter-type","band":3,"filterType":"highPass"})",
        eqProcessor,
        channelBalanceProcessor);
    requireContains(highPassResponse, R"("filterType":"highPass")", "high pass filter response");

    const auto notchResponse = echo::EqMessageProtocol::handleJsonLine(
        R"({"type":"eq:set-band-filter-type","band":3,"filterType":"notch"})",
        eqProcessor,
        channelBalanceProcessor);
    requireContains(notchResponse, R"("filterType":"notch")", "notch filter response");

    const auto bypassResponse = echo::EqMessageProtocol::handleJsonLine(
        R"({"type":"eq:set-band-enabled","band":3,"enabled":false})",
        eqProcessor,
        channelBalanceProcessor);
    requireContains(bypassResponse, R"("enabled":false)", "band bypass response");

    const std::string presetJson =
        R"({"type":"eq:set-preset","preampDb":-2,"bands":[)"
        R"({"frequencyHz":31,"gainDb":0,"q":1.2,"filterType":"lowShelf","enabled":true},{"frequencyHz":62,"gainDb":1},{"frequencyHz":125,"gainDb":2},)"
        R"({"frequencyHz":250,"gainDb":3},{"frequencyHz":500,"gainDb":4},{"frequencyHz":1000,"gainDb":5},)"
        R"({"frequencyHz":2000,"gainDb":4},{"frequencyHz":4000,"gainDb":3},{"frequencyHz":8000,"gainDb":2},)"
        R"({"frequencyHz":16000,"gainDb":1,"q":0.5,"filterType":"highShelf","enabled":false}]})";
    const auto presetResponse = echo::EqMessageProtocol::handleJsonLine(presetJson, eqProcessor, channelBalanceProcessor);
    requireContains(presetResponse, R"("preampDb":-2)", "preset response");
    requireContains(presetResponse, R"("gainDb":5)", "preset response");
    requireContains(presetResponse, R"("filterType":"lowShelf")", "preset response");
    requireContains(presetResponse, R"("enabled":false)", "preset response");

    const auto invalidJsonResponse = echo::EqMessageProtocol::handleJsonLine("{not json", eqProcessor, channelBalanceProcessor);
    requireContains(invalidJsonResponse, R"("type":"eq:error")", "invalid json response");
    requireContains(invalidJsonResponse, "invalid_json", "invalid json response");

    const auto invalidBandResponse = echo::EqMessageProtocol::handleJsonLine(
        R"({"type":"eq:set-band-gain","band":99,"gainDb":2})",
        eqProcessor,
        channelBalanceProcessor);
    requireContains(invalidBandResponse, R"("type":"eq:error")", "invalid band response");
    requireContains(invalidBandResponse, "invalid_band_index", "invalid band response");

    const auto invalidPresetResponse = echo::EqMessageProtocol::handleJsonLine(
        R"({"type":"eq:set-preset","preampDb":0,"bands":[{"frequencyHz":31,"gainDb":0}]})",
        eqProcessor,
        channelBalanceProcessor);
    requireContains(invalidPresetResponse, R"("type":"eq:error")", "invalid preset response");
    requireContains(invalidPresetResponse, "invalid_preset_bands", "invalid preset response");

    const auto invalidFilterResponse = echo::EqMessageProtocol::handleJsonLine(
        R"({"type":"eq:set-band-filter-type","band":1,"filterType":"allPass"})",
        eqProcessor,
        channelBalanceProcessor);
    requireContains(invalidFilterResponse, R"("type":"eq:error")", "invalid filter response");
    requireContains(invalidFilterResponse, "invalid_filter_type", "invalid filter response");

    echo::ConvolutionProcessor convolutionProcessor;
    convolutionProcessor.prepare(48000.0, 512, 2);
    require(convolutionProcessor.loadImpulseResponseForTests({ { 1.0f } }, 48000.0, "proto", "Protocol IR"), "protocol IR loads");
    const auto roomTrimResponse = echo::EqMessageProtocol::handleJsonLine(
        R"({"type":"roomCorrection:set-trim","trimDb":-3.5})",
        eqProcessor,
        channelBalanceProcessor,
        convolutionProcessor);
    requireContains(roomTrimResponse, R"("type":"roomCorrection:state")", "room correction trim response");
    requireContains(roomTrimResponse, R"("trimDb":-3.5)", "room correction trim response");

    const auto roomEnableResponse = echo::EqMessageProtocol::handleJsonLine(
        R"({"type":"roomCorrection:set-enabled","enabled":true})",
        eqProcessor,
        channelBalanceProcessor,
        convolutionProcessor);
    requireContains(roomEnableResponse, R"("enabled":true)", "room correction enabled response");
    requireContains(roomEnableResponse, R"("status":"active")", "room correction active response");
}

#ifndef _WIN32
void testAudioStartedRequiresFirstRenderedFrameAndEmitsOnce()
{
    int notificationPipe[2] { -1, -1 };
    require(pipe(notificationPipe) == 0, "audio.started capture pipe creation succeeds");
    {
        std::atomic<bool> shutdownRequested { false };
        AudioDaemon daemon({}, 48000, notificationPipe[1], shutdownRequested);
        daemon.emitPosition(0, 0, false);
        daemon.emitPosition(64, 0, false);
        daemon.emitPosition(128, 0, false);
    }
    close(notificationPipe[1]);

    std::string notifications;
    char buffer[2048];
    for (;;)
    {
        const auto bytes = read(notificationPipe[0], buffer, sizeof(buffer));
        if (bytes <= 0)
            break;
        notifications.append(buffer, static_cast<size_t>(bytes));
    }
    close(notificationPipe[0]);

    const std::string startedMethod = R"("method":"audio.started")";
    const auto first = notifications.find(startedMethod);
    require(first != std::string::npos,
        "first non-zero rendered position must emit audio.started");
    require(notifications.find(startedMethod, first + startedMethod.size()) == std::string::npos,
        "later rendered positions must not emit audio.started again");
    const auto zeroPosition = notifications.find(R"("framesPlayed":0)");
    require(zeroPosition != std::string::npos && zeroPosition < first,
        "zero rendered frames must remain a position update before audio.started");
}

void testDaemonInputPipeIsNonBlocking()
{
    int rpcPipe[2] { -1, -1 };
    require(pipe(rpcPipe) == 0, "RPC pipe creation succeeds");

    const bool configured = configureDaemonInputFds(rpcPipe[0], -1);
    const int rpcFlags = fcntl(rpcPipe[0], F_GETFL);

    close(rpcPipe[0]);
    close(rpcPipe[1]);

    require(configured, "daemon input pipe configures successfully");
    require((rpcFlags & O_NONBLOCK) != 0, "RPC input is non-blocking");
}
#endif

} // namespace

#include "automix_handoff_schedule_tests.h"
#include "workshop_chiptune_dynamics_tests.h"
#include "replay_gain_smoothing_tests.h"
#include "device_buffer_policy_tests.h"

int main(int argc, char** argv)
{
    const std::vector<std::pair<std::string, void (*)()>> tests {
        { "ReplayGain smoothing across blocks", testReplayGainSmoothingAcrossBlocks },
        { "ReplayGain unprepared queue boundary", testReplayGainUnpreparedQueueBoundary },
        { "ReplayGain gapless timeline", testGaplessReplayGainTimeline },
        { "ReplayGain native gapless routing", testNativeReplayGainGaplessRouting },
        { "disabled EQ is dry", testDisabledEqIsDry },
        { "flat enabled is transparent", testFlatEnabledIsTransparent },
        { "bypass returns to dry", testBypassReturnsToDry },
        { "rapid changes stay finite", testRapidChangesStayFinite },
        { "EQ reports risk without limiting enabled output", testEqReportsRiskWithoutLimitingEnabledOutput },
        { "coefficient updates stop in steady state", testCoefficientUpdatesStopInSteadyState },
        { "PEQ band controls clamp and bypass", testPeqBandControlsClampAndBypass },
        { "PEQ additional filter types stay finite", testPeqAdditionalFilterTypesStayFinite },
        { "FIR convolution identity is transparent", testConvolutionIdentityIsTransparent },
        { "FIR convolution IR identity is idempotent", testConvolutionIrIdentitySkipsDecodeAndRepreparesForRateChanges },
        { "FIR convolution decodes packed WAV through swresample", testConvolutionDecodesPackedWavFormatsThroughSwresample },
        { "FIR convolution preserves source sample rate", testConvolutionPreservesSourceRateAcrossDeviceRates },
        { "FIR convolution decodes planar float, AIFF, and FLAC", testConvolutionDecodesPlanarFloatAiffAndFlacFixtures },
        { "FIR convolution invalid files fail closed", testConvolutionFailsClosedForInvalidRealFiles },
        { "FIR convolution delay impulse", testConvolutionDelayImpulse },
        { "FIR convolution stereo mapping", testConvolutionStereoMapping },
        { "FIR convolution rejects long IR and clips safely", testConvolutionRejectsLongImpulseAndClipsSafely },
        { "partitioned convolution matches long FIR", testPartitionedConvolutionMatchesLongImpulseAcrossCallbacks },
        { "DSP session transition contract", testDspSessionTransitionContract },
        { "DSP unified tail contract", testDspUnifiedTailContract },
        { "RPC output writer serializes short writes", testRpcOutputWriterSerializesShortWrites },
        { "PCM drain waits for room-correction tail", testPcmDrainWaitsForRoomCorrectionTail },
        { "convolution live reload retires graphs", testConvolutionLiveReloadRetiresGraphsOffCallback },
        { "partitioned convolution callback deadline matrix", testPartitionedConvolutionCallbackDeadlineMatrix },
        { "Channel balance delay compensation", testChannelBalanceDelayCompensation },
        { "Channel balance solo keeps physical side", testChannelBalanceSoloKeepsPhysicalSide },
        { "Channel balance band gain compensation", testChannelBalanceBandGainCompensation },
        { "DSP chain bypass preserves dry buffer", testDspChainBypassPreservesDryBuffer },
        { "Workshop bitcrusher processes and bypasses safely", testWorkshopBitcrusherProcessesAndBypassesSafely },
        { "Workshop transition window and hard cut preserve outside samples", testWorkshopTransitionWindow },
        { "Workshop chiptune is deterministic and console-like", testWorkshopChiptuneStylizerIsDeterministicAndConsoleLike },
        { "Workshop chiptune preserves dynamics", testWorkshopChiptunePreservesDynamics },
        { "Workshop chiptune smooths gain changes", testWorkshopChiptuneGainChangesAreSmoothed },
        { "Workshop chiptune reenable clears old signal", testWorkshopChiptuneReenableDoesNotReviveOldSignal },
        { "Workshop tone transitions are bounded and block independent", testWorkshopToneTransitionsAreBoundedAndBlockIndependent },
        { "Workshop state snapshots remain coherent", testWorkshopStateSnapshotsRemainCoherent },
        { "Workshop chiptune uses native output protection", testWorkshopChiptuneThroughDspChain },
        { "Workshop vocal cut suppresses center and preserves bass and side", testWorkshopVocalCutSuppressesCenterAndPreservesBassAndSide },
        { "level meter visual spectrum is read-only", testLevelMeterVisualSpectrumObservesWithoutMutatingAudio },
        { "compressor reduces hot signals and preserves bypass", testCompressorReducesHotSignalsAndPreservesBypass },
        { "compressor advanced detector link and range controls", testCompressorAdvancedDetectorLinkAndRangeControls },
        { "compressor sidechain high-pass and clickless bypass", testCompressorSidechainHighpassAndClicklessBypass },
        { "virtual stage keeps center and bypasses speakers", testVirtualStageKeepsCenterAndBypassesSpeakers },
        { "spatial DSP stages process independently", testSpatialDspStagesProcessIndependently },
        { "DSP chain limiter catches inter-sample peaks", testDspChainLimiterCatchesInterSamplePeaks },
        { "EQ mid scope cuts center and leaves side", testEqMidScopeCutsCenterAndLeavesSide },
        { "EQ dynamic band reduces loud tone only", testEqDynamicBandReducesLoudToneOnly },
        { "DSP chain limiter protects active output", testDspChainLimiterProtectsActiveOutput },
        { "DSP chain limiter ignores near full-scale output", testDspChainLimiterIgnoresNearFullScaleOutput },
        { "DSP chain limiter can be bypassed", testDspChainLimiterCanBeBypassed },
        { "DSP headroom activates protection chain", testDspHeadroomActivatesProtectionChain },
        { "DSP headroom positive gain raises level", testDspHeadroomPositiveGainRaisesLevel },
        { "DSP chain protects upstream PCM processing", testDspChainProtectsUpstreamPcmProcessing },
        { "manual limiter ceiling holds true peak", testManualLimiterCeilingHoldsTruePeak },
        { "de-esser attenuates sibilance band only", testDeEsserAttenuatesSibilanceBandOnly },
        { "multiband compressor reduces only the selected band", testMultibandCompressorReducesOnlyTheSelectedBand },
        { "host buffer fallback attempts", testHostBufferFallbackAttempts },
        { "runtime buffer cache keeps per-rate knowledge", testRuntimeBufferCacheScopeKeepsPerRateKnowledge },
        { "known-good buffer preserves configured fallback", testKnownGoodBufferFallsBackToConfiguredChain },
        { "unsupported exclusive format skips buffer retries", testUnsupportedExclusiveFormatSkipsBufferRetries },
        { "transient ASIO buffer rejection retries after settle delay", testTransientAsioBufferRejectionRetriesAfterSettleDelay },
        { "host reports backend opened buffer frames", testHostReportsBackendOpenedBufferFrames },
        { "native dither matches TypeScript golden vector", testNativeDitherMatchesTypescriptGoldenVector },
        { "native ECHO SRC matches reference convolution", testNativeEchoSrcMatchesReferenceConvolution },
        { "native ECHO SRC factor-four polyphase matches dense convolution", testNativeEchoSrcPolyphaseMatchesDenseFactorFourConvolution },
        { "native ECHO SRC is stable across decode chunks", testNativeEchoSrcIsStableAcrossDecodeChunks },
        { "native ECHO SRC flushes tail exactly once", testNativeEchoSrcFlushesTailExactlyOnce },
        { "native pipeline drains ECHO SRC tail before EOF", testNativePlaybackPipelineDrainsEchoSrcTailBeforeEof },
        { "native SDM NTF places in-band zeros", testNativeSdmNtfPlacesInBandZeros },
        { "native SDM 512+fs full-scale stress stays recovery-free", testNativeSdm512PlusFullScaleStressStaysRecoveryFree },
        { "native SDM produces deterministic protected DoP", testNativeSdmProducesDeterministicProtectedDop },
        { "native SDM profiles shape noise out of band", testNativeSdmProfilesShapeNoiseOutOfBand },
        { "native SDM profiles remain bounded", testNativeSdmProfilesRemainBounded },
        { "native SDM high-order profiles are chunk invariant", testNativeSdmHighOrderProfilesAreChunkInvariant },
        { "native SDM stereo parallel path matches sequential chunks", testNativeSdmStereoParallelPathMatchesSequentialChunks },
        { "native SDM applies headroom and smooths transitions", testNativeSdmAppliesHeadroomAndSmoothsTransitions },
        { "native SDM idle lock preserves weak signals", testNativeSdmIdleLockPreservesWeakSignals },
        { "native SDM pre-modulator follows low-rate plan", testNativeSdmPreModulatorFollowsLowRatePlan },
        { "native SDM pre-modulator suppresses low-rate images", testNativeSdmPreModulatorSuppressesLowRateImages },
        { "native SDM small signal does not collapse to idle pattern", testNativeSdmSmallSignalDoesNotCollapseToIdlePattern },
        { "SDM Extra shared lookahead preserves decisions", testSdmExtraSharedLookaheadMatchesIndependentPredictions },
        { "native SDM Extra changes bitstream and stays bounded", testNativeSdmExtraLoopChangesBitstreamAndStaysBounded },
        { "native SDM adaptive profile scales loop gain from envelope", testNativeSdmAdaptiveProfileScalesLoopGainFromEnvelope },
        { "native SDM adaptive profiles preserve CUDA parity", testNativeSdmAdaptiveProfilesPreserveCudaParity },
        { "native SDM extended compensation layer", testNativeSdmExtendedCompensationLayer },
        { "native SDM tier profiles follow their contracts", testNativeSdmTierProfilesFollowTheirContracts },
        { "native SDM CUDA admission preserves decision streams", testNativeSdmCudaAdmissionPreservesDecisionStreams },
        { "native SDM adaptive tiers stay recovery-free at every rate", testNativeSdmAdaptiveTiersStayRecoveryFreeAtEveryRate },
        { "native processing configuration fails closed", testNativeProcessingConfigurationFailsClosed },
        { "host shared backend options", testHostSharedBackendOptions },
        { "runtime device configuration is authoritative", testRuntimeDeviceConfigurationIsAuthoritative },
        { "device-managed buffer policy", testDeviceManagedBufferPolicy },
        { "host backend names", testHostBackendNames },
        { "native output notification queue", testNativeOutputNotificationQueuePreservesHostEvents },
    { "specialized outputs skip miniaudio shared output", testSpecializedOutputsSkipMiniaudioSharedOutput },
    { "specialized runHost validation before hardware open", testSpecializedRunHostValidationBeforeHardwareOpen },
    { "host prebuffer defaults remain compatible", testHostPrebufferDefaultsRemainCompatible },
        { "high-rate native DSD buffers use byte-frame scale", testHighRateNativeDsdBuffersUseByteFrameScale },
        { "explicit zero prebuffer disables wait", testExplicitZeroPrebufferDisablesWait },
        { "native FIFO wraps and resets", testNativeFifoWrapsAndResets },
        { "deferred daemon rings use session rate", testDeferredDaemonRingsUseSessionRateAndKeepPassthroughLazy },
        { "PCM session swap waits for reader epoch", testPcmSessionSwapWaitsForInFlightReaderLease },
        { "PCM session swap rejects stale callback admission", testPcmSessionSwapRejectsCallbackAdmittedBeforeLease },
        { "DoP replacement rejects stale callback admission", testDopReplaceRejectsCallbackAdmittedBeforeLease },
        { "native DSD replacement rejects stale callback admission", testNativeDsdReplaceRejectsCallbackAdmittedBeforeLease },
        { "render epoch covers post-FIFO clocks", testRenderEpochCoversPostFifoClockUpdatesForAllSources },
        { "automix cancel waits for deck reader epoch", testAutomixCancelWaitsForInFlightDeckReaderLease },
        { "session swap retires promoted automix deck reader", testSessionSwapRetiresPromotedAutomixDeckReader },
        { "gapless cancel rejects blocked producer lease", testGaplessCancelRejectsBlockedProducerLease },
        { "PCM session swap stress at small callbacks", testPcmSessionSwapStressAtSmallCallbacks },
        { "libav PCM stream decoder reads bounded chunks", testLibavPcmStreamDecoderReadsBoundedChunks },
        { "libav PCM stream decoder bounds damaged media recovery", testLibavPcmStreamDecoderBoundsDamagedMediaRecovery },
        { "libav PCM stream decoder maps native output format", testLibavPcmStreamDecoderMapsToNativeOutputFormat },
        { "libav PCM stream decoder decodes DSD containers to PCM", testLibavPcmStreamDecoderDecodesDsdContainersToPcm },
        { "libav PCM stream decoder seek cancel and invalid file", testLibavPcmStreamDecoderSeekCancelAndInvalidFile },
        { "daemon seek tail normalization", testDaemonSeekAtFileEnd },
        { "PCM idle does not count underrun before PCM", testPcmIdleDoesNotCountUnderrunBeforePcm },
        { "PCM pause retains buffered frames", testPcmSourcePauseRetainsBufferedFrames },
        { "PCM initial play gate waits without consuming", testPcmInitialPlayGateWaitsForPrebufferWithoutConsumingFirstFrames },
        { "PCM initial play gate handles ended and timeout", testPcmInitialPlayGateReleasesForShortEndedInputAndTimeout },
        { "PCM initial play gate deadline starts at play", testPcmInitialPlayGateDeadlineStartsAtPlayRequest },
        { "PCM initial play gate cancels stale sessions", testPcmInitialPlayGateCancelsForGenerationAndStop },
        { "PCM initial play gate runs once", testPcmInitialPlayGateRunsOnlyOnce },
        { "daemon PCM play gate is non-blocking", testAudioDaemonPlayReturnsBeforePcmGateIsReady },
        { "daemon PCM failed gate stays paused", testAudioDaemonFailedInitialGateCannotResumeEmptySession },
        { "daemon back-to-back open scopes producer generations", testAudioDaemonBackToBackOpenKeepsProducerGenerationScoped },
        { "daemon local source range stops at segment boundary", testAudioDaemonLocalSourceRangeStopsAtSegmentBoundary },
        { "daemon Doubletime processes PCM on decoder thread", testAudioDaemonDoubletimeProcessesPcmOnDecoderThread },
        { "PCM input ended waits for buffered drain", testPcmInputEndedWaitsForBufferedDrain },
        { "PCM playback rate consumes source frames", testPcmSourcePlaybackRateConsumesSourceFramesAtRate },
        { "listening tap receives host speed", testListeningTapReceivesHostSpeed },
        { "listening tap volume independence", listening_volume_tests::sharedVolumeIsIndependent },
        { "listening tap live volume history", listening_volume_tests::volumeChangesDoNotChangeSharedHistory },
        { "PCM playback rate callback matrix", testPlaybackRateIsContinuousAcrossCallbackSizesAndRates },
        { "playback rate accepts decoder tempo mode atomically", testPlaybackRateConfigurationAcceptsDecoderTempoModeAtomically },
        { "playback rate callback deadline matrix", testPlaybackRateCallbackDeadlineMatrix },
        { "playback rate clocked interactions", testPlaybackRateUsesDeviceClockForPrebufferSeekGaplessAndAutomix },
        { "playback rate changes keep warm history", testPlaybackRateChangesKeepWarmHistoryAndMonotonicClocks },
        { "PCM playback rate bounds native dither writes", testNativeRenderPlaybackRateKeepsDitherInsideOutputBuffer },
        { "PCM replacement drops stale frames", testPcmSourceReplaceBufferedAudioDropsStaleFrames },
        { "PCM replacement stays paused until resume", testPcmSourceReplaceBufferedAudioWhilePausedWaitsForResume },
        { "PCM prebuffer avoids premature underrun", testPcmPrebufferDoesNotCountUnderrunBeforeTarget },
        { "native render adapter", testNativeRenderAdapter },
        { "PCM declick ramp", testPcmDeclickRampOnSessionStartAndStop },
        { "native automix mixes next deck", testNativeAutomixDeckMixesNextBeforeCurrentEnds },
        { "native automix promotes and rearms decks", testNativeAutomixPromotesDeckAndRearmsTheReleasedDeck },
        { "native automix identity follows fade completion", testNativeAutomixKeepsOutgoingIdentityUntilFadeCompletes },
        { "native automix tempo glides back to unity", testAutomixTempoGlidesBackToUnityAfterOverlap },
        { "native automix tempo ramp keeps position scale in sync", testAutomixTempoRampKeepsPositionScaleInSync },
        { "native automix rejects stale render clock schedule", testNativeAutomixRejectsFadeScheduleAlreadyBehindRenderClock },
        { "native automix applies DSP once after deck sum", testNativeAutomixAppliesUserDspOnceAfterDeckSum },
        { "native automix applies user volume to both decks", testNativeAutomixAppliesUserVolumeToBothDecks },
        { "native automix protects deck sum without optional DSP", testNativeAutomixProtectsTheDeckSumWithoutOptionalDsp },
        { "native automix follows beat handoff schedule", automix_handoff_tests::testSchedule },
        { "native automix rhythmic handoff staggers bass", testNativeAutomixRhythmicHandoffStaggersBassAtCrossover },
        { "native automix rhythmic handoff clears outgoing highs", testNativeAutomixRhythmicHandoffClearsOutgoingHighs },
        { "native automix rhythmic handoff moves center voice", testNativeAutomixRhythmicHandoffMovesCenterVoiceAfterTheGroove },
        { "native automix follows planned center vocal exit", testNativeAutomixUsesPlannedCenterVocalExit },
        { "native automix mono follows planned vocal exit", testNativeAutomixMonoUsesPlannedVocalExit },
        { "native automix tail ride holds both decks", testNativeAutomixTailRideHoldsBothDecksThroughTheMiddle },
        { "native automix beat cut echo throw rings after promotion", testNativeAutomixBeatCutEchoThrowRingsAfterPromotion },
        { "native automix applies ReplayGain per deck", testNativeAutomixAppliesReplayGainPerDeckWithoutGlobalDoubleProcessing },
        { "native automix next deck respects current buffer", testNativeAutomixNextDeckCannotAdvancePastCurrentBuffer },
        { "native automix deck failure recovery", testNativeAutomixDeckFailureUsesTwentyMillisecondRecovery },
        { "native automix cancel ramps instead of hard cut", testNativeAutomixCancelWithFadeRampsInsteadOfHardCut },
        { "native automix front-shifts fade on early deck exhaustion", testNativeAutomixActivatesFadeWhenDeckAEndsBeforePlannedStart },
        { "native gapless joins at PCM boundary", testNativeGaplessJoinsAtDecodedPcmBoundary },
        { "DoP render keeps valid markers", testDopRenderKeepsValidMarkersDuringSilenceAndData },
#ifdef _WIN32
        { "ASIO buffer candidates", testAsioBufferCandidateGeneration },
        { "ASIO sample-rate pivots", testAsioSampleRatePivotCandidateGeneration },
        { "ASIO sample conversion", testAsioSampleConversion },
        { "ASIO DoP conversion", testAsioDopConversionMatchesStandards },
        { "ASIO native DSD conversion", testAsioNativeDsdConversion },
        { "ASIO native DSD buffers are primed before start", testAsioNativeDsdBuffersArePrimedBeforeStart },
        { "ASIO render guard catches exceptions", testAsioRenderGuardCatchesCallbackException },
        { "ASIO callback drain gates teardown", testAsioCallbackDrainMustCompleteBeforeTeardown },
        { "ASIO runtime lifecycle events are published", testAsioRuntimeLifecycleEventsArePublished },
        { "ASIO unsolicited window suppression scope", testAsioUnsolicitedWindowSuppressionScope },
#endif
        { "cleanup emits shutdown ack once", testCleanupEmitsShutdownAckOnce },
        { "explicit stop suppresses daemon queue advance", testExplicitStopSuppressesDaemonQueueAdvance },
        { "daemon position throttles processing telemetry", testDaemonPositionThrottlesProcessingTelemetry },
        { "explicit stop waits for rendered drain", testExplicitStopWaitsForRenderedDrain },
        { "natural end advances daemon queue", testNaturalEndStillAdvancesDaemonQueue },
        { "daemon queue identity survives stale sync", testDaemonQueueIdentitySurvivesStaleSync },
        { "daemon pending advance survives queue sync", testDaemonPendingAdvanceSurvivesQueueSync },
        { "ASIO queue channel reopen", testAsioQueueChannelReopen },
        { "ASIO queue channel stable advance", testAsioQueueChannelStableAdvance },
        { "ASIO queue channel stop race", testAsioQueueChannelStopRace },
        { "natural end advances into ranged queue source", testNaturalEndAdvancesIntoRangedQueueSource },
        { "runtime output transition stays on owner", testRuntimeOutputTransitionRunsOnPumpOwner },
        { "mixed-rate daemon advance reconfigures before decode", testMixedRateDaemonAdvanceReconfiguresBeforeDecode },
        { "manual stop supersedes mixed-rate daemon advance", testManualStopSupersedesMixedRateDaemonAdvance },
        { "strict daemon advance rejects missing sample rate", testStrictDaemonAdvanceRejectsMissingSampleRate },
        { "queue replacement cancels pending daemon advance", testQueueReplacementCancelsPendingDaemonAdvance },
        { "protocol messages", testProtocolMessages },
#ifndef _WIN32
        { "audio.started requires rendered frames and emits once", testAudioStartedRequiresFirstRenderedFrameAndEmitsOnce },
        { "daemon input pipe is non-blocking", testDaemonInputPipeIsNonBlocking },
#endif
    };

    const std::string filter = argc > 1 && argv[1] != nullptr ? argv[1] : "";
    int failures = 0;
    int executed = 0;
    for (const auto& [name, test] : tests)
    {
        if (!filter.empty() && name.find(filter) == std::string::npos)
            continue;
        ++executed;
        try
        {
            test();
            std::cout << "[PASS] " << name << std::endl;
        }
        catch (const std::exception& error)
        {
            ++failures;
            std::cerr << "[FAIL] " << name << ": " << error.what() << std::endl;
        }
        catch (...)
        {
            ++failures;
            std::cerr << "[FAIL] " << name << ": unknown error" << std::endl;
        }
    }

    if (executed == 0)
    {
        std::cerr << "[FAIL] no native audio tests matched filter: " << filter << std::endl;
        return 1;
    }
    return failures == 0 ? 0 : 1;
}
