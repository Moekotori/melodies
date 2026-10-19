#include "Eac3BurstReader.h"
#include <algorithm>
#include <cmath>
#include <cstring>
#include <filesystem>
#include <stdexcept>
extern "C" {
#include <libavformat/avformat.h>
#include <libavcodec/defs.h>
#include <libavutil/mem.h>
}

namespace echo {
struct Eac3BurstReader::Impl {
    AVFormatContext* input = nullptr;
    AVFormatContext* mux = nullptr;
    AVIOContext* io = nullptr;
    AVPacket* packet = nullptr;
    bool muxStarted = false;
    int stream = -1;
    std::array<uint8_t, eac3BurstBytes> output {};
    std::size_t written = 0;
    std::size_t accumulatedBytes = 0;
    int pendingBlocks = 0;
    int blocksPerPacket = 0;
    double position = 0;
    double seekTarget = 0;
    bool seeking = false;

    ~Impl() { close(); }
    void clearMux() {
        if (mux) {
            if (muxStarted) av_write_trailer(mux); // Releases muxer's bounded aggregation storage on seek/close.
            muxStarted = false;
            mux->pb = nullptr; avformat_free_context(mux); mux = nullptr;
        }
        if (io) { av_freep(&io->buffer); avio_context_free(&io); }
        written = accumulatedBytes = 0;
        pendingBlocks = blocksPerPacket = 0;
    }
    void close() {
        clearMux();
        av_packet_free(&packet);
        avformat_close_input(&input);
        stream = -1;
    }
    static int write(void* opaque, const uint8_t* data, int count) {
        auto& self = *static_cast<Impl*>(opaque);
        if (count < 0 || static_cast<std::size_t>(count) > self.output.size() - self.written)
            return AVERROR(ENOSPC);
        std::memcpy(self.output.data() + self.written, data, count);
        self.written += count;
        return count;
    }
    void prepareMux() {
        clearMux();
        if (avformat_alloc_output_context2(&mux, nullptr, "spdif", nullptr) < 0 || !mux)
            throw std::runtime_error("eac3_hdmi_muxer_unavailable");
        auto* buffer = static_cast<uint8_t*>(av_malloc(4096));
        if (!buffer) throw std::bad_alloc();
        io = avio_alloc_context(buffer, 4096, 1, this, nullptr, write, nullptr);
        if (!io) { av_free(buffer); throw std::bad_alloc(); }
        mux->pb = io;
        mux->flags |= AVFMT_FLAG_CUSTOM_IO;
        auto* destination = avformat_new_stream(mux, nullptr);
        if (!destination || avcodec_parameters_copy(destination->codecpar, input->streams[stream]->codecpar) < 0)
            throw std::runtime_error("eac3_hdmi_muxer_setup_failed");
        destination->time_base = input->streams[stream]->time_base;
        if (avformat_write_header(mux, nullptr) < 0)
            throw std::runtime_error("eac3_hdmi_muxer_setup_failed");
        muxStarted = true;
    }
};

Eac3BurstReader::Eac3BurstReader() : impl_(std::make_unique<Impl>()) {}
Eac3BurstReader::~Eac3BurstReader() = default;
void Eac3BurstReader::close() { impl_->close(); }

Eac3SourceInfo Eac3BurstReader::open(const std::string& path) {
    impl_->close();
    if (path.empty() || path.find("://") != std::string::npos
        || path.starts_with("\\\\") || path.starts_with("//")
        || !std::filesystem::is_regular_file(std::filesystem::path(std::u8string(path.begin(), path.end()))))
        throw std::runtime_error("eac3_hdmi_local_file_required");
    AVDictionary* options = nullptr;
    av_dict_set(&options, "protocol_whitelist", "file", 0);
    av_dict_set(&options, "format_whitelist", "eac3,mov,matroska,webm", 0);
    av_dict_set(&options, "probesize", "1048576", 0);
    av_dict_set(&options, "analyzeduration", "2000000", 0);
    const int opened = avformat_open_input(&impl_->input, path.c_str(), nullptr, &options);
    av_dict_free(&options);
    if (opened < 0 || avformat_find_stream_info(impl_->input, nullptr) < 0)
        throw std::runtime_error("eac3_hdmi_source_open_failed");
    for (unsigned i = 0; i < impl_->input->nb_streams; ++i) {
        if (impl_->input->streams[i]->codecpar->codec_type == AVMEDIA_TYPE_AUDIO) {
            impl_->stream = static_cast<int>(i); break;
        }
    }
    if (impl_->stream < 0) throw std::runtime_error("eac3_hdmi_audio_stream_missing");
    for (unsigned i = 0; i < impl_->input->nb_streams; ++i)
        if (static_cast<int>(i) != impl_->stream) impl_->input->streams[i]->discard = AVDISCARD_ALL;
    const auto* parameters = impl_->input->streams[impl_->stream]->codecpar;
    if (parameters->codec_id != AV_CODEC_ID_EAC3 || parameters->sample_rate != eac3SourceRate)
        throw std::runtime_error("eac3_hdmi_requires_eac3_48khz");
    impl_->packet = av_packet_alloc();
    if (!impl_->packet) throw std::bad_alloc();
    impl_->position = impl_->seekTarget = 0;
    impl_->seeking = false;
    impl_->prepareMux();
    return { parameters->ch_layout.nb_channels,
        impl_->input->duration > 0 ? static_cast<double>(impl_->input->duration) / AV_TIME_BASE : 0,
        parameters->profile == AV_PROFILE_EAC3_DDP_ATMOS };
}

void Eac3BurstReader::seek(double seconds) {
    if (!impl_->input || !std::isfinite(seconds) || seconds < 0)
        throw std::runtime_error("eac3_hdmi_invalid_seek");
    const auto* stream = impl_->input->streams[impl_->stream];
    const double offset = stream->start_time == AV_NOPTS_VALUE ? 0 : stream->start_time * av_q2d(stream->time_base);
    const auto target = static_cast<int64_t>((seconds + offset) / av_q2d(stream->time_base));
    if (avformat_seek_file(impl_->input, impl_->stream, INT64_MIN, target, target, 0) < 0)
        throw std::runtime_error("eac3_hdmi_seek_unavailable");
    avformat_flush(impl_->input);
    impl_->prepareMux();
    impl_->position = impl_->seekTarget = seconds;
    impl_->seeking = true;
}

bool Eac3BurstReader::next(std::array<uint8_t, eac3BurstBytes>& burst) {
    if (!impl_->input) throw std::runtime_error("eac3_hdmi_source_not_open");
    impl_->written = 0;
    // Bound non-audio packets as well as the retained compressed working set.
    for (int skipped = 0; skipped < 4096; ++skipped) {
        av_packet_unref(impl_->packet);
        const int result = av_read_frame(impl_->input, impl_->packet);
        if (result == AVERROR_EOF) {
            if (impl_->pendingBlocks != 0) throw std::runtime_error("eac3_hdmi_incomplete_burst");
            return false;
        }
        if (result < 0) throw std::runtime_error("eac3_hdmi_source_read_failed");
        auto* packet = impl_->packet;
        if (packet->stream_index != impl_->stream) continue;
        if (packet->size < 6 || packet->size > static_cast<int>(eac3BurstBytes - 8)
            || packet->data[0] != 0x0b || packet->data[1] != 0x77
            || (packet->data[5] >> 3) <= 10 || (packet->data[5] >> 3) > 16
            || (packet->data[4] >> 6) != 0)
            throw std::runtime_error("eac3_hdmi_invalid_frame");
        constexpr int blocks[] { 1, 2, 3, 6 };
        const int count = blocks[(packet->data[4] >> 4) & 3];
        // A demux packet must contain one independent stream and only its
        // dependent frames. Never count separately packetized dependent audio
        // as another 1536-sample time interval.
        int frameCount = 0;
        for (int offset = 0; offset < packet->size;) {
            if (++frameCount > 8 || packet->size - offset < 6)
                throw std::runtime_error("eac3_hdmi_invalid_frame_group");
            const auto* frame = packet->data + offset;
            const int size = ((((frame[2] & 7) << 8) | frame[3]) + 1) * 2;
            const int type = frame[2] >> 6;
            if (frame[0] != 0x0b || frame[1] != 0x77 || size < 6 || size > packet->size - offset
                || (frame[5] >> 3) <= 10 || (frame[5] >> 3) > 16 || (frame[4] >> 6) != 0
                || blocks[(frame[4] >> 4) & 3] != count
                || (offset == 0 ? type != 0 || (frame[2] & 0x38) != 0 : type != 1))
                throw std::runtime_error("eac3_hdmi_invalid_frame_group");
            offset += size;
        }
        const auto* stream = impl_->input->streams[impl_->stream];
        const auto pts = packet->pts != AV_NOPTS_VALUE ? packet->pts : packet->dts;
        const double stamp = pts == AV_NOPTS_VALUE ? impl_->position
            : (pts - (stream->start_time == AV_NOPTS_VALUE ? 0 : stream->start_time)) * av_q2d(stream->time_base);
        if (impl_->seeking) {
            if (pts == AV_NOPTS_VALUE) throw std::runtime_error("eac3_hdmi_seek_timestamp_missing");
            if (stamp + count * 256.0 / eac3SourceRate <= impl_->seekTarget) continue;
            impl_->position = std::max(0.0, stamp);
            impl_->seeking = false;
        }
        if ((impl_->pendingBlocks && count != impl_->blocksPerPacket)
            || impl_->accumulatedBytes + packet->size > eac3BurstBytes - 8)
            throw std::runtime_error("eac3_hdmi_burst_exceeds_limit");
        impl_->blocksPerPacket = count;
        impl_->pendingBlocks += count;
        impl_->accumulatedBytes += packet->size;
        packet->stream_index = 0;
        // The pinned FFmpeg IEC 61937 muxer copies the encoded payload, including
        // JOC metadata. ECHO never decodes, modifies, or re-encodes it.
        if (av_write_frame(impl_->mux, packet) < 0) throw std::runtime_error("eac3_hdmi_mux_failed");
        avio_flush(impl_->io);
        if (impl_->io->error < 0) throw std::runtime_error("eac3_hdmi_mux_failed");
        if (!impl_->written) continue;
        if (impl_->written != eac3BurstBytes || impl_->pendingBlocks != 6)
            throw std::runtime_error("eac3_hdmi_invalid_burst");
        burst = impl_->output;
        impl_->position += 1536.0 / eac3SourceRate;
        impl_->pendingBlocks = 0;
        impl_->accumulatedBytes = 0;
        return true;
    }
    throw std::runtime_error("eac3_hdmi_packet_limit_exceeded");
}
double Eac3BurstReader::positionSeconds() const { return impl_->position; }
}
