#include "PcmWireInputBinding.h"
#include "Options.h"
#include "RawPcmInputReader.h"
#include "NativePlaybackPipeline.h"
#include <stdexcept>

void validatePcmWireOptions(const Options& options)
{
    if (!options.pcmWireEpoch) return;
    echo::pcm_wire::validate({options.pcmWireEpoch,
        static_cast<uint32_t>(options.sampleRate), static_cast<uint16_t>(options.channels)});
    // Experimental one-epoch process contract. No unframed input, Opus guest,
    // DoP, local file daemon or reused RPC/data descriptor on this stream.
    if (options.list || options.buildCapabilities || options.asioSdmFamilyProbe
        || options.cddaRead || options.cddaToc || options.cddaStatus || options.cddaEject || options.cddaCapabilities
        || options.dopOutput || options.asioNativeDsdOutput
        || options.listenTogetherInputFd >= 0 || options.listenTogetherOutputFd >= 0
        || (options.decodePcm && (options.pcmInputFd >= 0
            || options.rpcStdinFd >= 0 || options.rpcStdoutFd >= 0))
        || (!options.decodePcm && (options.pcmInputFd < 0
            || options.rpcStdinFd < 0 || options.rpcStdoutFd < 0
            || options.pcmInputFd == options.rpcStdinFd || options.pcmInputFd == options.rpcStdoutFd)))
        throw std::runtime_error("PCM wire requires a separate PCM-only native data descriptor");
}

void bindPcmWireInput(RawPcmInputReader& reader, NativePlaybackPipeline& pipeline,
    const Options& options)
{
    // The launcher must first obtain audio.sessionBegin(sessionId=1). Changing
    // track/seek requires a new process/epoch until dynamic arm is implemented.
    // Fixed native generation prevents a delayed Begin binding a newer session.
    reader.enableFraming({options.pcmWireEpoch, static_cast<uint32_t>(options.sampleRate),
        static_cast<uint16_t>(options.channels)}, [&pipeline] {
            if (pipeline.generation() != 1
                || pipeline.outputFormat() != NativePlaybackPipeline::OutputFormat::Pcm)
                throw std::runtime_error("PCM input requires the first confirmed native PCM session");
        }, [&pipeline] { pipeline.markInputEnded(1); });
}
