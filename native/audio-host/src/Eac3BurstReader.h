#pragma once

#include <array>
#include <cstdint>
#include <memory>
#include <string>

// One IEC 61937 E-AC-3 burst carries 1536 source samples over 6144
// stereo 16-bit carrier frames. No decoded PCM or source writes exist here.
namespace echo {
constexpr std::size_t eac3BurstBytes = 24576;
constexpr int eac3SourceRate = 48000;
constexpr int eac3CarrierRate = 192000;

struct Eac3SourceInfo {
    int channels = 0;
    double durationSeconds = 0;
    bool atmosSignalled = false;
};

class Eac3BurstReader {
public:
    Eac3BurstReader();
    ~Eac3BurstReader();
    Eac3BurstReader(const Eac3BurstReader&) = delete;
    Eac3BurstReader& operator=(const Eac3BurstReader&) = delete;
    Eac3SourceInfo open(const std::string& localPath);
    void close();
    void seek(double seconds);
    bool next(std::array<uint8_t, eac3BurstBytes>& burst);
    double positionSeconds() const;
private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
};
}
