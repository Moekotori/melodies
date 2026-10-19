#pragma once

#include <array>
#include <cstddef>
#include <cstdint>
#include <functional>
#include <iosfwd>
#include <span>
#include <string_view>

// Native data-plane framing. No sockets, credentials or JSON in this module.
// Feed/write run on workers, never on a device callback. One stream per epoch.
namespace echo::pcm_wire {
inline constexpr size_t headerBytes = 64;
inline constexpr size_t maximumPayloadBytes = 64 * 1024;
struct Format {
    uint64_t epoch;
    uint32_t sampleRate;
    uint16_t channels;
};
uint64_t parseEpoch(std::string_view text);
void validate(Format format);

class Writer final {
public:
    Writer(std::ostream& output, Format format);
    void push(std::span<const float> samples);
    void end();
private:
    void packet(uint16_t kind, uint32_t frames, std::span<const float> samples);
    std::ostream& output_;
    Format format_;
    uint64_t sequence_ = 0, position_ = 0;
    bool ended_ = false;
};

class Reader final {
public:
    // Callbacks are synchronous: the next packet is not read until push accepts
    // this one. The output ring supplies backpressure without another queue.
    using Push = std::function<bool(const float*, int)>;
    Reader(Format expected, Push push, std::function<void()> begin, std::function<void()> end);
    void feed(std::span<const std::byte> bytes);
    void finish(); // clean transport EOF requires an explicit End packet
    bool ended() const noexcept { return ended_; }
private:
    void readHeader();
    void deliver();
    Format expected_;
    Push push_;
    std::function<void()> begin_;
    std::function<void()> end_;
    std::array<std::byte, headerBytes> header_{};
    // Aligned payload storage, allocated once with Reader, including for BE hosts.
    std::array<float, maximumPayloadBytes / sizeof(float)> samples_{};
    size_t headerUsed_ = 0, payloadUsed_ = 0, payloadBytes_ = 0;
    uint64_t sequence_ = 0, position_ = 0;
    uint32_t frames_ = 0;
    uint16_t kind_ = 0;
    bool begun_ = false, ended_ = false, failed_ = false;
};
} // namespace echo::pcm_wire
