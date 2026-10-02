#include "metadata_file_tail.hpp"
#include <algorithm>
#include <array>
#include <cstring>
#include <string>
#include <vector>

static std::vector<unsigned char> readTail(std::ifstream& stream, std::size_t limit) {
  const auto position = stream.tellg();
  stream.clear();
  stream.seekg(0, std::ios::end);
  const auto length = stream.tellg();
  std::vector<unsigned char> bytes;
  if (length > 0) {
    const auto size = static_cast<std::size_t>(std::min<std::streamoff>(length, static_cast<std::streamoff>(limit)));
    bytes.resize(size);
    stream.seekg(length - static_cast<std::streamoff>(size), std::ios::beg);
    stream.read(reinterpret_cast<char*>(bytes.data()), static_cast<std::streamsize>(size));
    if (static_cast<std::size_t>(stream.gcount()) != size) bytes.clear();
  }
  stream.clear();
  stream.seekg(position);
  return bytes;
}

bool hasMp3TailTags(std::ifstream& stream) {
  const auto bytes = readTail(stream, 512);
  if (bytes.empty()) return true;
  const std::string tail(reinterpret_cast<const char*>(bytes.data()), bytes.size());
  // Retain TS recovery for ID3v1, APE, Lyrics3 and footer-based ID3v2 tags.
  return (tail.size() >= 128 && tail.compare(tail.size() - 128, 3, "TAG") == 0)
    || tail.find("APETAGEX") != std::string::npos || tail.find("LYRICSEND") != std::string::npos
    || tail.find("LYRICS200") != std::string::npos
    || (tail.size() >= 10 && tail.compare(tail.size() - 10, 3, "3DI") == 0);
}

static std::uint32_t le32(const unsigned char* value) {
  return static_cast<std::uint32_t>(value[0]) | (static_cast<std::uint32_t>(value[1]) << 8U)
    | (static_cast<std::uint32_t>(value[2]) << 16U) | (static_cast<std::uint32_t>(value[3]) << 24U);
}

static std::uint32_t pageCrc(const unsigned char* bytes, std::size_t size) {
  static const auto table = [] {
    std::array<std::uint32_t, 256> result{};
    for (std::uint32_t i = 0; i < result.size(); ++i) {
      auto crc = i << 24U;
      for (int bit = 0; bit < 8; ++bit) crc = (crc << 1U) ^ ((crc & 0x80000000U) ? 0x04c11db7U : 0U);
      result[i] = crc;
    }
    return result;
  }();
  std::uint32_t crc = 0;
  for (std::size_t i = 0; i < size; ++i) {
    const auto byte = i >= 22 && i < 26 ? 0U : bytes[i];
    crc = (crc << 8U) ^ table[((crc >> 24U) ^ byte) & 0xffU];
  }
  return crc;
}

std::optional<std::uint64_t> readOggTerminalGranule(std::ifstream& stream, std::uint32_t serial) {
  // An Ogg page is at most 27 + 255 + 255*255 = 65,307 bytes.
  const auto tail = readTail(stream, 65536);
  for (std::size_t start = 0; start + 27 <= tail.size(); ++start) {
    const auto* page = tail.data() + start;
    if (page[0] != 'O' || std::memcmp(page, "OggS", 4) != 0 || page[4] != 0
        || (page[5] & 4U) == 0 || (page[5] & ~7U) != 0 || le32(page + 14) != serial) continue;
    const auto segments = static_cast<std::size_t>(page[26]);
    if (start + 27 + segments > tail.size()) continue;
    std::size_t size = 27 + segments;
    for (std::size_t i = 0; i < segments; ++i) size += page[27 + i];
    // Trailing junk, chained streams, truncated or forged pages keep the sequential path.
    if (start + size != tail.size() || pageCrc(page, size) != le32(page + 22)) continue;
    const auto granule = static_cast<std::uint64_t>(le32(page + 6))
      | (static_cast<std::uint64_t>(le32(page + 10)) << 32U);
    if (granule > 0 && granule != UINT64_MAX) return granule;
  }
  return std::nullopt;
}
