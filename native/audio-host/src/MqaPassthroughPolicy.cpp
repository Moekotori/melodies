#include "MqaPassthroughPolicy.h"

#include <algorithm>
#include <cctype>

namespace
{
std::string asciiLower(std::string value)
{
    std::transform(value.begin(), value.end(), value.begin(), [](unsigned char ch) {
        return static_cast<char>(std::tolower(ch));
    });
    return value;
}

int endpointValidBits(const std::string& rawFormat)
{
    const std::string format = asciiLower(rawFormat);
    if (format.find("int32lsb16") != std::string::npos || format.find("int32msb16") != std::string::npos)
        return 16;
    if (format.find("int32lsb18") != std::string::npos || format.find("int32msb18") != std::string::npos)
        return 18;
    if (format.find("int32lsb20") != std::string::npos || format.find("int32msb20") != std::string::npos)
        return 20;
    if (format == "pcm24packed" || format == "pcm24in32"
        || format.find("int24") != std::string::npos
        || format.find("int32lsb24") != std::string::npos
        || format.find("int32msb24") != std::string::npos)
        return 24;
    if (format == "pcm16" || format.find("int16") != std::string::npos)
        return 16;
    if (format == "pcm32" || format.find("int32") != std::string::npos)
        return 32;
    return 0;
}
}

MqaPassthroughDecision evaluateMqaPassthrough(
    const NativePlaybackPipeline::ProcessingStatus& processing,
    const echo::AudioProbe& probe,
    const MqaPassthroughEndpoint& endpoint)
{
    const auto& request = processing.mqaPassthrough;
    if (! request.requested)
        return {false, "not_requested"};
    if (! probe.mqaTagged)
        return {false, "source_not_tagged"};
    if (processing.outputFormat != NativePlaybackPipeline::OutputFormat::Pcm)
        return {false, "pcm_output_required"};
    if (endpoint.backend != "wasapi-exclusive" && endpoint.backend != "asio")
        return {false, "exclusive_or_asio_required"};
    if (probe.sampleRate <= 0
        || probe.sampleRate != request.sourceSampleRate
        || endpoint.sampleRate != probe.sampleRate
        || endpoint.hardwareSampleRate != probe.sampleRate)
        return {false, "sample_rate_mismatch"};
    if ((probe.bitDepth != 16 && probe.bitDepth != 24)
        || probe.bitDepth != request.sourceBitDepth)
        return {false, "source_bit_depth_mismatch"};
    if (endpointValidBits(endpoint.format) < probe.bitDepth)
        return {false, "integer_endpoint_format_required"};
    if (processing.echoSrc.active || processing.sdm.active || processing.ditherActive)
        return {false, "native_processing_active"};
    return {true, "active"};
}
