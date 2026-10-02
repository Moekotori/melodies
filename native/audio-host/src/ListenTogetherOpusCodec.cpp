#include "ListenTogetherOpusCodec.h"

#include "ListenTogetherAudioTransport.h"

#include <algorithm>
#include <array>
#include <cerrno>
#include <cmath>
#include <cstring>
#include <stdexcept>
#include <string>

extern "C" {
#include <libavcodec/avcodec.h>
#include <libavutil/error.h>
#include <libavutil/mem.h>
#include <libavutil/opt.h>
#include <libavutil/samplefmt.h>
}

namespace echo::listen_together {
namespace {

std::string ffmpegError(const char* operation, int code)
{
    std::array<char, AV_ERROR_MAX_STRING_SIZE> buffer{};
    av_strerror(code, buffer.data(), buffer.size());
    return std::string(operation) + ": " + buffer.data();
}

void requireFfmpeg(int result, const char* operation)
{
    if (result < 0)
        throw std::runtime_error(ffmpegError(operation, result));
}

AVSampleFormat selectEncoderSampleFormat(const AVCodec* codec)
{
    int formatCount = 0;
#if LIBAVCODEC_VERSION_INT >= AV_VERSION_INT(61, 13, 100)
    const void* rawFormats = nullptr;
    requireFfmpeg(
        avcodec_get_supported_config(nullptr, codec, AV_CODEC_CONFIG_SAMPLE_FORMAT, 0, &rawFormats, &formatCount),
        "query Opus encoder sample formats");
    const auto* formats = static_cast<const AVSampleFormat*>(rawFormats);
#else
    const auto* formats = codec->sample_fmts;
    if (formats != nullptr) {
        while (formats[formatCount] != AV_SAMPLE_FMT_NONE)
            ++formatCount;
    }
#endif
    if (formats == nullptr)
        return AV_SAMPLE_FMT_FLT;

    for (const auto preferred : {AV_SAMPLE_FMT_FLT, AV_SAMPLE_FMT_FLTP, AV_SAMPLE_FMT_S16, AV_SAMPLE_FMT_S16P}) {
        for (int index = 0; index < formatCount; ++index) {
            if (formats[index] == preferred)
                return preferred;
        }
    }
    throw std::runtime_error("libopus encoder has no supported PCM input format");
}

std::int16_t floatToS16(float sample) noexcept
{
    const float clamped = std::clamp(sample, -1.0f, 1.0f);
    return static_cast<std::int16_t>(std::lrintf(clamped * 32767.0f));
}

void fillEncoderFrame(AVFrame* frame, const float* interleavedStereo, int frames)
{
    requireFfmpeg(av_frame_make_writable(frame), "make Opus encoder frame writable");
    const auto format = static_cast<AVSampleFormat>(frame->format);
    if (format == AV_SAMPLE_FMT_FLT) {
        std::memcpy(frame->data[0], interleavedStereo, static_cast<std::size_t>(frames) * 2 * sizeof(float));
        return;
    }
    if (format == AV_SAMPLE_FMT_FLTP) {
        auto* left = reinterpret_cast<float*>(frame->data[0]);
        auto* right = reinterpret_cast<float*>(frame->data[1]);
        for (int index = 0; index < frames; ++index) {
            left[index] = interleavedStereo[index * 2];
            right[index] = interleavedStereo[index * 2 + 1];
        }
        return;
    }
    if (format == AV_SAMPLE_FMT_S16) {
        auto* output = reinterpret_cast<std::int16_t*>(frame->data[0]);
        for (int index = 0; index < frames * 2; ++index)
            output[index] = floatToS16(interleavedStereo[index]);
        return;
    }
    if (format == AV_SAMPLE_FMT_S16P) {
        auto* left = reinterpret_cast<std::int16_t*>(frame->data[0]);
        auto* right = reinterpret_cast<std::int16_t*>(frame->data[1]);
        for (int index = 0; index < frames; ++index) {
            left[index] = floatToS16(interleavedStereo[index * 2]);
            right[index] = floatToS16(interleavedStereo[index * 2 + 1]);
        }
        return;
    }
    throw std::runtime_error("unsupported Opus encoder PCM format");
}

std::vector<float> readDecodedFrame(const AVFrame* frame)
{
    if (frame->sample_rate != static_cast<int>(kCanonicalSampleRate) || frame->ch_layout.nb_channels != kCanonicalChannels)
        throw std::runtime_error("Opus decoder returned a non-canonical PCM format");

    const int frames = frame->nb_samples;
    std::vector<float> output(static_cast<std::size_t>(frames) * kCanonicalChannels);
    const auto format = static_cast<AVSampleFormat>(frame->format);
    if (format == AV_SAMPLE_FMT_FLT) {
        std::memcpy(output.data(), frame->data[0], output.size() * sizeof(float));
        return output;
    }
    if (format == AV_SAMPLE_FMT_FLTP) {
        const auto* left = reinterpret_cast<const float*>(frame->data[0]);
        const auto* right = reinterpret_cast<const float*>(frame->data[1]);
        for (int index = 0; index < frames; ++index) {
            output[static_cast<std::size_t>(index) * 2] = left[index];
            output[static_cast<std::size_t>(index) * 2 + 1] = right[index];
        }
        return output;
    }
    if (format == AV_SAMPLE_FMT_S16) {
        const auto* samples = reinterpret_cast<const std::int16_t*>(frame->data[0]);
        std::transform(samples, samples + output.size(), output.begin(), [](std::int16_t value) {
            return static_cast<float>(value) / 32768.0f;
        });
        return output;
    }
    if (format == AV_SAMPLE_FMT_S16P) {
        const auto* left = reinterpret_cast<const std::int16_t*>(frame->data[0]);
        const auto* right = reinterpret_cast<const std::int16_t*>(frame->data[1]);
        for (int index = 0; index < frames; ++index) {
            output[static_cast<std::size_t>(index) * 2] = static_cast<float>(left[index]) / 32768.0f;
            output[static_cast<std::size_t>(index) * 2 + 1] = static_cast<float>(right[index]) / 32768.0f;
        }
        return output;
    }
    throw std::runtime_error("unsupported Opus decoder PCM format");
}

void setBestEffortOption(void* object, const char* name, const std::string& value)
{
    if (object != nullptr)
        av_opt_set(object, name, value.c_str(), 0);
}

} // namespace

struct OpusEncoder::Impl {
    AVCodecContext* context = nullptr;
    AVFrame* frame = nullptr;
    AVPacket* packet = nullptr;
    int configuredBitrate = 0;
    std::int64_t nextPts = 0;

    explicit Impl(OpusCodecConfig config)
    {
      try {
        if (config.bitrate < 64'000 || config.bitrate > 320'000)
            throw std::invalid_argument("Opus bitrate must be between 64 and 320 kbps");
        if (config.expectedPacketLossPercent < 0 || config.expectedPacketLossPercent > 30)
            throw std::invalid_argument("Opus expected packet loss must be between 0 and 30 percent");

        const AVCodec* codec = avcodec_find_encoder_by_name("libopus");
        if (codec == nullptr)
            throw std::runtime_error("FFmpeg libopus encoder is unavailable");
        context = avcodec_alloc_context3(codec);
        if (context == nullptr)
            throw std::bad_alloc();

        context->sample_rate = static_cast<int>(kCanonicalSampleRate);
        av_channel_layout_default(&context->ch_layout, kCanonicalChannels);
        context->sample_fmt = selectEncoderSampleFormat(codec);
        context->bit_rate = config.bitrate;
        context->time_base = {1, static_cast<int>(kCanonicalSampleRate)};
        context->flags |= AV_CODEC_FLAG_LOW_DELAY;
        context->compression_level = 8;
        setBestEffortOption(context->priv_data, "application", "audio");
        // Unconstrained VBR can exceed the ELTA payload ceiling on transients,
        // especially at 320 kbps. CBR keeps each 20 ms packet within its budget
        // (800 bytes at the highest supported rate). Do not silently ignore it.
        requireFfmpeg(av_opt_set(context->priv_data, "vbr", "off", 0), "configure bounded Opus packets");
        setBestEffortOption(context->priv_data, "frame_duration", "20");
        setBestEffortOption(context->priv_data, "packet_loss", std::to_string(config.expectedPacketLossPercent));
        setBestEffortOption(context->priv_data, "fec", config.expectedPacketLossPercent > 0 ? "1" : "0");
        requireFfmpeg(avcodec_open2(context, codec, nullptr), "open FFmpeg libopus encoder");
        if (context->frame_size != static_cast<int>(kFramesPerPacket))
            throw std::runtime_error("libopus encoder did not accept 20 ms frames");

        frame = av_frame_alloc();
        packet = av_packet_alloc();
        if (frame == nullptr || packet == nullptr)
            throw std::bad_alloc();
        frame->format = context->sample_fmt;
        frame->sample_rate = context->sample_rate;
        frame->nb_samples = context->frame_size;
        requireFfmpeg(av_channel_layout_copy(&frame->ch_layout, &context->ch_layout), "copy Opus channel layout");
        requireFfmpeg(av_frame_get_buffer(frame, 0), "allocate Opus encoder frame");
        configuredBitrate = config.bitrate;
    }
    catch (...) {
        av_packet_free(&packet);
        av_frame_free(&frame);
        avcodec_free_context(&context);
        throw;
      }
    }

    ~Impl()
    {
        av_packet_free(&packet);
        av_frame_free(&frame);
        avcodec_free_context(&context);
    }
};

OpusEncoder::OpusEncoder(OpusCodecConfig config) : impl_(std::make_unique<Impl>(config)) {}
OpusEncoder::~OpusEncoder() = default;
OpusEncoder::OpusEncoder(OpusEncoder&&) noexcept = default;
OpusEncoder& OpusEncoder::operator=(OpusEncoder&&) noexcept = default;

std::vector<std::uint8_t> OpusEncoder::encode(const float* interleavedStereo, int frames)
{
    if (interleavedStereo == nullptr || frames != impl_->context->frame_size)
        throw std::invalid_argument("Opus encoder requires exactly one 20 ms stereo frame");

    fillEncoderFrame(impl_->frame, interleavedStereo, frames);
    impl_->frame->pts = impl_->nextPts;
    impl_->nextPts += frames;
    av_packet_unref(impl_->packet);
    requireFfmpeg(avcodec_send_frame(impl_->context, impl_->frame), "send Opus encoder frame");
    requireFfmpeg(avcodec_receive_packet(impl_->context, impl_->packet), "receive Opus packet");
    if (impl_->packet->size <= 0 || impl_->packet->size > static_cast<int>(kMaxOpusPayloadBytes))
        throw std::runtime_error("Opus encoder returned an invalid realtime packet size");
    return {impl_->packet->data, impl_->packet->data + impl_->packet->size};
}

int OpusEncoder::bitrate() const noexcept { return impl_->configuredBitrate; }
int OpusEncoder::frameSize() const noexcept { return impl_->context->frame_size; }

struct OpusDecoder::Impl {
    AVCodecContext* context = nullptr;
    AVFrame* frame = nullptr;
    AVPacket* packet = nullptr;

    Impl()
    {
      try {
        const AVCodec* codec = avcodec_find_decoder_by_name("libopus");
        if (codec == nullptr)
            codec = avcodec_find_decoder(AV_CODEC_ID_OPUS);
        if (codec == nullptr)
            throw std::runtime_error("FFmpeg Opus decoder is unavailable");
        context = avcodec_alloc_context3(codec);
        if (context == nullptr)
            throw std::bad_alloc();
        context->sample_rate = static_cast<int>(kCanonicalSampleRate);
        av_channel_layout_default(&context->ch_layout, kCanonicalChannels);
        context->request_sample_fmt = AV_SAMPLE_FMT_FLT;

        constexpr std::array<std::uint8_t, 19> opusHead{
            'O', 'p', 'u', 's', 'H', 'e', 'a', 'd',
            1, kCanonicalChannels,
            0, 0,
            0x80, 0xbb, 0x00, 0x00,
            0, 0,
            0,
        };
        context->extradata = static_cast<std::uint8_t*>(av_mallocz(opusHead.size() + AV_INPUT_BUFFER_PADDING_SIZE));
        if (context->extradata == nullptr)
            throw std::bad_alloc();
        std::memcpy(context->extradata, opusHead.data(), opusHead.size());
        context->extradata_size = static_cast<int>(opusHead.size());
        requireFfmpeg(avcodec_open2(context, codec, nullptr), "open FFmpeg Opus decoder");

        frame = av_frame_alloc();
        packet = av_packet_alloc();
        if (frame == nullptr || packet == nullptr)
            throw std::bad_alloc();
    }
    catch (...) {
        av_packet_free(&packet);
        av_frame_free(&frame);
        avcodec_free_context(&context);
        throw;
      }
    }

    ~Impl()
    {
        av_packet_free(&packet);
        av_frame_free(&frame);
        avcodec_free_context(&context);
    }
};

OpusDecoder::OpusDecoder() : impl_(std::make_unique<Impl>()) {}
OpusDecoder::~OpusDecoder() = default;
OpusDecoder::OpusDecoder(OpusDecoder&&) noexcept = default;
OpusDecoder& OpusDecoder::operator=(OpusDecoder&&) noexcept = default;

std::vector<float> OpusDecoder::decode(const std::uint8_t* payload, int payloadBytes)
{
    if (payload == nullptr || payloadBytes <= 0 || payloadBytes > static_cast<int>(kMaxOpusPayloadBytes))
        throw std::invalid_argument("invalid realtime Opus payload");

    av_packet_unref(impl_->packet);
    requireFfmpeg(av_new_packet(impl_->packet, payloadBytes), "allocate Opus decoder packet");
    std::memcpy(impl_->packet->data, payload, static_cast<std::size_t>(payloadBytes));
    av_frame_unref(impl_->frame);
    requireFfmpeg(avcodec_send_packet(impl_->context, impl_->packet), "send Opus decoder packet");
    requireFfmpeg(avcodec_receive_frame(impl_->context, impl_->frame), "receive Opus decoder frame");
    return readDecodedFrame(impl_->frame);
}

} // namespace echo::listen_together
