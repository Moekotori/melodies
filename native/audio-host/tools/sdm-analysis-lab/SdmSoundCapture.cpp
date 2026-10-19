#include "SdmSoundCapture.h"
#include <bit>
#include <cstdint>
#include <stdexcept>

namespace echo::sdmLab {
namespace {
void u32(std::ostream& stream, uint32_t value)
{
    const char bytes[] {static_cast<char>(value), static_cast<char>(value >> 8),
        static_cast<char>(value >> 16), static_cast<char>(value >> 24)};
    stream.write(bytes, 4);
}
void u16(std::ostream& stream, uint16_t value)
{
    const char bytes[] {static_cast<char>(value), static_cast<char>(value >> 8)};
    stream.write(bytes, 2);
}
}
SoundCapture::SoundCapture(const std::filesystem::path& directory, const std::string& name,
    int sourceRate, int sourceFrames)
{
    if (directory.empty()) return;
    if (name.empty() || name.find_first_not_of("abcdefghijklmnopqrstuvwxyz0123456789-") != std::string::npos)
        throw std::runtime_error("sound_capture_invalid_name");
    std::filesystem::create_directories(directory);
    const auto wavePath = directory / (name + ".wav");
    const auto dsdPath = directory / (name + ".dsdraw");
    if (std::filesystem::exists(wavePath) || std::filesystem::exists(dsdPath))
        throw std::runtime_error("sound_capture_refuses_existing_asset");
    source_.open(wavePath, std::ios::binary);
    dsd_.open(dsdPath, std::ios::binary);
    if (!source_ || !dsd_) throw std::runtime_error("sound_capture_open_failed");
    // Mono IEEE float WAVEFORMATEX with fact chunk, little endian.
    const auto dataBytes = static_cast<uint32_t>(sourceFrames) * 4;
    source_.write("RIFF", 4); u32(source_, dataBytes + 50); source_.write("WAVEfmt ", 8);
    u32(source_, 18); u16(source_, 3); u16(source_, 1); u32(source_, sourceRate);
    u32(source_, static_cast<uint32_t>(sourceRate) * 4); u16(source_, 4); u16(source_, 32); u16(source_, 0);
    source_.write("fact", 4); u32(source_, 4); u32(source_, sourceFrames);
    source_.write("data", 4); u32(source_, dataBytes);
    enabled_ = true;
}
void SoundCapture::writeSource(const std::vector<float>& source)
{
    if (!enabled_) return;
    if constexpr (std::endian::native == std::endian::little)
        source_.write(reinterpret_cast<const char*>(source.data()), static_cast<std::streamsize>(source.size() * sizeof(float)));
    else
        for (float value : source) u32(source_, std::bit_cast<uint32_t>(value));
    if (!source_) throw std::runtime_error("sound_capture_wave_write_failed");
}
void SoundCapture::writeDsd(const std::vector<uint8_t>& bytes)
{
    if (!enabled_) return;
    dsd_.write(reinterpret_cast<const char*>(bytes.data()), static_cast<std::streamsize>(bytes.size()));
    if (!dsd_) throw std::runtime_error("sound_capture_dsd_write_failed");
}
void SoundCapture::finish()
{
    if (!enabled_) return;
    source_.flush(); dsd_.flush();
    if (!source_ || !dsd_) throw std::runtime_error("sound_capture_flush_failed");
}
}
