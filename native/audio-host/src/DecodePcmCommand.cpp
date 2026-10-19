#include "DecodePcmCommand.h"
#include "Options.h"
#include "NativePcmChannelPolicy.h"
#include "PcmWireProtocol.h"
#include "../../audio-engine/libav_decoder.h"
#include <iostream>
#include <memory>
#ifdef _WIN32
#include <fcntl.h>
#include <io.h>
#endif

int runLibavDecodePcm(const Options& options)
{
    if (options.decodeFile.empty())
        throw std::runtime_error("libav decode failed: missing input file");
#ifdef _WIN32
    _setmode(_fileno(stdout), _O_BINARY);
#endif
    echo::LibavPcmStreamDecoder decoder;
    decoder.open(options.decodeFile);
    const int rate = decoder.sampleRate(), channels = decoder.channels();
    if (rate <= 0)
        throw std::runtime_error("libav decode failed: source sample rate unavailable");
    if (rate != options.sampleRate)
        throw std::runtime_error("libav decode resampling unsupported: source="
            + std::to_string(rate) + " requested=" + std::to_string(options.sampleRate));
    if (channels <= 0 || channels > maxNativePcmChannels)
        throw std::runtime_error("libav decode unsupported channel count: " + std::to_string(channels));
    if (channels != options.channels)
        throw std::runtime_error("libav decode channel remap unsupported: source="
            + std::to_string(channels) + " requested=" + std::to_string(options.channels));
    if (options.decodeStartSeconds > 0.0)
        decoder.seek(options.decodeStartSeconds);

    std::unique_ptr<echo::pcm_wire::Writer> writer;
    if (options.pcmWireEpoch)
        writer = std::make_unique<echo::pcm_wire::Writer>(std::cout,
            echo::pcm_wire::Format{options.pcmWireEpoch, static_cast<uint32_t>(rate),
                static_cast<uint16_t>(channels)});
    while (!decoder.eof())
    {
        auto chunk = decoder.readFrames(4096);
        if (chunk.frames <= 0) break;
        if (writer)
            writer->push(std::span<const float>(chunk.samples.data(),
                static_cast<size_t>(chunk.frames) * channels));
        else
            std::cout.write(reinterpret_cast<const char*>(chunk.samples.data()),
                static_cast<std::streamsize>(chunk.frames * channels * sizeof(float)));
        if (!std::cout.good())
            throw std::runtime_error("libav decode failed while writing PCM");
    }
    if (writer) writer->end(); // decoder EOF is input End, never device drain
    return 0;
}
