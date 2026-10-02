#include "libav_decoder.h"
#include <cmath>
#include <filesystem>
#include <iostream>
#include <stdexcept>
#include <vector>

namespace {

void require(bool condition, const char* message)
{
    if (!condition)
        throw std::runtime_error(message);
}

std::vector<float> readRemaining(echo::LibavPcmStreamDecoder& decoder)
{
    std::vector<float> samples;
    const size_t limit = static_cast<size_t>(decoder.sampleRate()) * decoder.channels() * 4;
    while (!decoder.eof())
    {
        const auto chunk = decoder.readFrames(137);
        require(chunk.frames <= 137, "seek decode must keep each chunk bounded");
        require(chunk.samples.size() == static_cast<size_t>(chunk.frames) * decoder.channels(),
            "seek decode must preserve channel alignment");
        if (chunk.frames == 0)
            break;
        require(samples.size() + chunk.samples.size() <= limit, "seek decode exceeded fixture bounds");
        for (const float sample : chunk.samples)
            require(std::isfinite(sample), "seek decode produced non-finite PCM");
        samples.insert(samples.end(), chunk.samples.begin(), chunk.samples.end());
    }
    return samples;
}

void checkSeek(const std::filesystem::path& path, int outputSampleRate)
{
    echo::LibavPcmStreamDecoder decoder;
    decoder.open(path.string(), outputSampleRate, 2);
    const auto reference = readRemaining(decoder);
    require(!reference.empty(), "seek fixture must decode real PCM");

    // Exercise block/page boundaries, a partial first block, backward seeks
    // after EOF, and resetting the trimming state at the source origin.
    for (const double seconds : {2.0, 0.2, 1.4, 0.0})
    {
        decoder.seek(seconds);
        const auto actual = readRemaining(decoder);
        const auto targetFrame = static_cast<size_t>(std::llround(seconds * outputSampleRate));
        const auto expectedFrames = static_cast<long long>(reference.size() / 2 - targetFrame);
        const auto actualFrames = static_cast<long long>(actual.size() / 2);
        // swresample rounds its fractional delay to output samples; the seek
        // boundary may differ by at most two frames, never a whole codec block.
        require(std::abs(actualFrames - expectedFrames) <= 2,
            "seek replayed audio before the requested time or truncated its tail");
        require(actual.size() > 2048, "seek must leave the expected audible tail");
        if (outputSampleRate == 48000)
        {
            // Skip only the initial overlap window of lossy codecs. The rest
            // must match a full decode at the requested sample, not another block.
            for (size_t index = 512; index < 2048; ++index)
                require(std::abs(actual[index] - reference[targetFrame * 2 + index]) < 0.0001f,
                    "seek PCM does not match the requested source position");
        }
    }

    decoder.seek(0.2);
    decoder.cancel();
    require(decoder.readFrames(137).frames == 0, "cancel must stop pending seek trimming");
    decoder.open(path.string(), outputSampleRate, 2);
    require(readRemaining(decoder) == reference, "reopen must clear cancelled seek state");
    std::cout << "PASS " << path.filename().string() << " output=" << outputSampleRate << '\n';
}

} // namespace

int main(int argc, char** argv)
{
    try
    {
        require(argc == 2, "expected the seek fixture directory");
        for (const auto* name : {"seek-vorbis.ogg", "seek-tta.tta"})
        {
            const auto path = std::filesystem::path(argv[1]) / name;
            checkSeek(path, 48000);
            checkSeek(path, 44100);
        }
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << "FAIL " << error.what() << '\n';
        return 1;
    }
}
