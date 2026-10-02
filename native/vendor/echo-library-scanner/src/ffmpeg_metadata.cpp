#include "ffmpeg_metadata.hpp"
#include <algorithm>
#include <chrono>
#include <cmath>
#include <cctype>
#include <cerrno>
#include <cstdint>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <limits>
#include <memory>

#ifdef ECHO_SCANNER_HAS_FFMPEG
extern "C" {
#include <libavformat/avformat.h>
#include <libavcodec/avcodec.h>
#include <libavutil/error.h>
#include <libavutil/log.h>
}
namespace {
using Clock = std::chrono::steady_clock;
struct LocalProbe {
  std::ifstream file;
  std::int64_t size = 0, position = 0;
  std::size_t bytesRead = 0;
  Clock::time_point deadline = Clock::now() + std::chrono::seconds(3);
  bool limited() const { return bytesRead >= 16 * 1024 * 1024 || Clock::now() >= deadline; }
};
int interrupt(void* opaque) { return static_cast<LocalProbe*>(opaque)->limited() ? 1 : 0; }
int readPacket(void* opaque, std::uint8_t* buffer, int count) {
  auto& input = *static_cast<LocalProbe*>(opaque);
  if (input.limited()) return AVERROR_EXIT;
  const auto remaining = 16 * 1024 * 1024 - input.bytesRead;
  count = static_cast<int>(std::min<std::size_t>(remaining, static_cast<std::size_t>(count)));
  input.file.read(reinterpret_cast<char*>(buffer), count);
  const auto read = input.file.gcount();
  input.bytesRead += static_cast<std::size_t>(read); input.position += read;
  return read > 0 ? static_cast<int>(read) : AVERROR_EOF;
}
std::int64_t seek(void* opaque, std::int64_t offset, int whence) {
  auto& input = *static_cast<LocalProbe*>(opaque);
  if (whence == AVSEEK_SIZE) return input.size;
  if (input.limited()) return AVERROR_EXIT;
  whence &= ~AVSEEK_FORCE;
  const auto base = whence == SEEK_SET ? 0 : whence == SEEK_CUR ? input.position : whence == SEEK_END ? input.size : -1;
  if (base < 0 || offset < -base || offset > input.size - base) return AVERROR(EINVAL);
  input.file.clear(); input.file.seekg(base + offset);
  if (!input.file) return AVERROR(EIO);
  input.position = base + offset;
  return input.position;
}
struct FormatOwner {
  AVFormatContext* format = nullptr;
  AVIOContext* io = nullptr;
  ~FormatOwner() { if (format) avformat_close_input(&format); if (io) { av_freep(&io->buffer); avio_context_free(&io); } }
};
std::string tag(AVDictionary* dictionary, const char* key) {
  const auto* entry = av_dict_get(dictionary, key, nullptr, 0);
  if (!entry || !entry->value) return {};
  std::size_t size = 0;
  while (size < 4096 && entry->value[size]) ++size;
  return std::string(entry->value, size);
}
std::optional<int> tagNumber(const std::string& value, int maximum) {
  if (value.empty()) return {};
  char* end = nullptr; const auto n = std::strtol(value.c_str(), &end, 10);
  return end != value.c_str() && n > 0 && n <= maximum ? std::optional<int>(static_cast<int>(n)) : std::nullopt;
}
}
#endif

const char* ffmpegMetadataExtensionsJson() {
  // Cross-language protocol declaration; smoke checks parity with AudioFormatCatalog.
  return R"formats([".mp3",".flac",".fla",".wav",".wave",".bwf",".w64",".m4a",".m4b",".m4p",".aac",".ogg",".oga",".opus",".wma",".alac",".aiff",".aif",".aifc",".au",".snd",".voc",".ape",".wv",".tta",".tak",".caf",".dsf",".dff",".mka",".mkv",".mp4",".mov",".webm",".mp2",".mp1",".mpc",".spx",".amr",".ac3",".eac3",".ec3",".dd",".ddp",".thd",".truehd",".mlp",".dts"])formats";
}

bool fillFfmpegAudioMetadata(const std::string& path, NativeMetadataResult& result, std::string& error) {
#ifndef ECHO_SCANNER_HAS_FFMPEG
  (void)path; (void)result; (void)error;
  return false;
#else
  av_log_set_level(AV_LOG_QUIET);
  if (path.find("://") != std::string::npos) { error = "metadata probe only accepts local files"; return false; }
  LocalProbe input;
  input.file.open(std::filesystem::u8path(path), std::ios::binary);
  if (!input.file) { error = "metadata source unavailable"; return false; }
  input.file.seekg(0, std::ios::end); input.size = input.file.tellg(); input.file.seekg(0);
  if (input.size <= 0) { error = "empty audio source"; return false; }
  FormatOwner owner;
  owner.format = avformat_alloc_context();
  auto* buffer = static_cast<unsigned char*>(av_malloc(32768));
  if (!owner.format || !buffer) { av_free(buffer); error = "metadata allocation failed"; return false; }
  owner.io = avio_alloc_context(buffer, 32768, 0, &input, readPacket, nullptr, seek);
  if (!owner.io) { av_free(buffer); error = "metadata IO allocation failed"; return false; }
  owner.format->pb = owner.io;
  owner.format->flags |= AVFMT_FLAG_CUSTOM_IO;
  owner.format->interrupt_callback = { interrupt, &input };
  owner.format->probesize = 1024 * 1024;
  owner.format->max_analyze_duration = 500000;
  owner.format->max_probe_packets = 64;
  owner.format->max_streams = 32;
  // Supply the filename for demuxer hints while all IO remains our bounded local reader.
  if (avformat_open_input(&owner.format, path.c_str(), nullptr, nullptr) < 0) { error = "unreadable audio container"; return false; }
  if (avformat_find_stream_info(owner.format, nullptr) < 0) { error = "audio stream information unavailable"; return false; }
  const auto index = av_find_best_stream(owner.format, AVMEDIA_TYPE_AUDIO, -1, -1, nullptr, 0);
  if (index < 0) { error = "container has no decodable audio stream"; return false; }
  const auto* stream = owner.format->streams[index];
  const auto* properties = stream->codecpar;
  if (!avcodec_find_decoder(properties->codec_id) || properties->sample_rate <= 0 || properties->ch_layout.nb_channels <= 0) {
    error = "audio codec unavailable"; return false;
  }
  double duration = stream->duration != AV_NOPTS_VALUE && stream->duration > 0
    ? static_cast<double>(stream->duration) * av_q2d(stream->time_base)
    : owner.format->duration != AV_NOPTS_VALUE ? static_cast<double>(owner.format->duration) / AV_TIME_BASE : 0;
  // Raw TrueHD/DTS may not carry timestamps or a duration. Preserve unknown duration;
  // a valid, decodable audio stream is still importable without decoding its whole body.
  if (!std::isfinite(duration) || duration < 0) duration = 0;
  result.duration = duration; result.sampleRate = properties->sample_rate;
  const auto bits = properties->bits_per_raw_sample > 0 ? properties->bits_per_raw_sample
    : properties->bits_per_coded_sample > 0 ? properties->bits_per_coded_sample : av_get_bits_per_sample(properties->codec_id);
  if (bits > 0 && bits <= 64) result.bitDepth = bits;
  const auto bitrate = properties->bit_rate > 0 ? properties->bit_rate : owner.format->bit_rate;
  if (bitrate > 0 && bitrate <= std::numeric_limits<int>::max()) result.bitrate = static_cast<int>(bitrate);
  result.codec = avcodec_get_name(properties->codec_id);
  std::transform(result.codec.begin(), result.codec.end(), result.codec.begin(), [](unsigned char c) { return static_cast<char>(std::toupper(c)); });
  result.sources["duration"] = duration > 0 ? "technical" : "unknown";
  result.sources["sampleRate"] = result.sources["codec"] = "technical";
  if (result.bitDepth) result.sources["bitDepth"] = "technical";
  if (result.bitrate) result.sources["bitrate"] = "technical";
  const auto text = [&](const char* key) {
    auto value = tag(stream->metadata, key);
    return value.empty() ? tag(owner.format->metadata, key) : value;
  };
  const auto assign = [&](std::string& destination, const char* field, const char* key) {
    const auto value = text(key);
    if (!value.empty() && (destination.empty() || result.sources[field] != "embedded")) {
      destination = value; result.sources[field] = "embedded"; result.hasEmbeddedMetadata = true;
    }
  };
  assign(result.title, "title", "title"); assign(result.artist, "artist", "artist");
  assign(result.album, "album", "album"); assign(result.albumArtist, "albumArtist", "album_artist");
  if (!result.trackNo) result.trackNo = tagNumber(text("track"), 99999);
  if (!result.discNo) result.discNo = tagNumber(text("disc"), 99999);
  if (!result.year) result.year = tagNumber(text("date"), 9999);
  const auto genre = text("genre"); if (!genre.empty() && !result.genre) result.genre = genre;
  return true;
#endif
}
