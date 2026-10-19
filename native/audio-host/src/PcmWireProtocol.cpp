#include "PcmWireProtocol.h"

#include <algorithm>
#include <bit>
#include <charconv>
#include <chrono>
#include <cmath>
#include <cstring>
#include <limits>
#include <ostream>
#include <stdexcept>
#include <utility>

namespace echo::pcm_wire {
namespace {
constexpr uint16_t beginPacket = 1, dataPacket = 2, endPacket = 3;
static_assert(sizeof(float) == 4 && std::numeric_limits<float>::is_iec559);
[[noreturn]] void invalid() { throw std::runtime_error("invalid or stale native PCM stream"); }
template<class T> T get(const std::byte* p) {
    uint64_t result = 0;
    for (size_t i = 0; i < sizeof(T); ++i)
        result |= static_cast<uint64_t>(std::to_integer<uint8_t>(p[i])) << (i * 8);
    return static_cast<T>(result);
}
template<class T> void put(std::byte* p, T value) {
    for (size_t i = 0; i < sizeof(T); ++i)
        p[i] = static_cast<std::byte>((value >> (i * 8)) & 255);
}
uint64_t timestamp() {
    return static_cast<uint64_t>(std::chrono::duration_cast<std::chrono::nanoseconds>(
        std::chrono::steady_clock::now().time_since_epoch()).count());
}
}

uint64_t parseEpoch(std::string_view text) {
    uint64_t value = 0;
    const auto result = std::from_chars(text.data(), text.data() + text.size(), value);
    if (result.ec != std::errc{} || result.ptr != text.data() + text.size() || value == 0)
        throw std::runtime_error("PCM epoch must be a nonzero unsigned integer");
    return value;
}
void validate(Format format) {
    if (!format.epoch || format.sampleRate < 8000 || format.sampleRate > 768000
        || !format.channels || format.channels > 8)
        invalid();
}

Writer::Writer(std::ostream& output, Format format) : output_(output), format_(format) {
    validate(format);
    packet(beginPacket, 0, {});
}
void Writer::push(std::span<const float> samples) {
    if (ended_ || samples.empty() || samples.size() % format_.channels != 0)
        invalid();
    const size_t blockSamples = maximumPayloadBytes / (sizeof(float) * format_.channels)
        * format_.channels;
    while (!samples.empty()) {
        const auto block = samples.first(std::min(samples.size(), blockSamples));
        for (float value : block)
            if (!std::isfinite(value)) invalid();
        packet(dataPacket, static_cast<uint32_t>(block.size() / format_.channels), block);
        samples = samples.subspan(block.size());
    }
}
void Writer::end() {
    if (ended_) invalid();
    packet(endPacket, 0, {});
    output_.flush();
    if (!output_) throw std::runtime_error("native PCM stream flush failed");
    ended_ = true;
}
void Writer::packet(uint16_t kind, uint32_t frames, std::span<const float> samples) {
    if (sequence_ == UINT64_MAX || frames > UINT64_MAX - position_) invalid();
    std::array<std::byte, headerBytes> h{};
    std::memcpy(h.data(), "EPCM", 4);
    put<uint16_t>(&h[4], 1); put(&h[6], kind);
    put(&h[8], format_.epoch); put(&h[16], sequence_);
    put(&h[24], position_); put(&h[32], format_.sampleRate);
    put(&h[36], format_.channels); put<uint16_t>(&h[38], 1); // float32 LE interleaved
    put(&h[40], frames); put<uint32_t>(&h[44], static_cast<uint32_t>(samples.size_bytes()));
    put(&h[48], timestamp()); // sender clock; never compared to receiver wall time
    output_.write(reinterpret_cast<const char*>(h.data()), h.size());
    if constexpr (std::endian::native == std::endian::little) {
        if (!samples.empty())
            output_.write(reinterpret_cast<const char*>(samples.data()), samples.size_bytes());
    } else {
        std::array<std::byte, 4> bytes{};
        for (float value : samples) {
            put(bytes.data(), std::bit_cast<uint32_t>(value));
            output_.write(reinterpret_cast<const char*>(bytes.data()), bytes.size());
        }
    }
    if (!output_) throw std::runtime_error("native PCM stream write failed");
    ++sequence_; position_ += frames;
}

Reader::Reader(Format expected, Push push, std::function<void()> begin, std::function<void()> end)
    : expected_(expected), push_(std::move(push)), begin_(std::move(begin)), end_(std::move(end)) {
    validate(expected);
    if (!push_ || !begin_ || !end_) invalid();
}
void Reader::feed(std::span<const std::byte> bytes) {
    if (failed_) invalid();
    try {
        while (!bytes.empty()) {
            if (ended_) invalid();
            if (headerUsed_ < headerBytes) {
                const auto n = std::min(bytes.size(), headerBytes - headerUsed_);
                std::memcpy(header_.data() + headerUsed_, bytes.data(), n);
                bytes = bytes.subspan(n); headerUsed_ += n;
                if (headerUsed_ != headerBytes) continue;
                readHeader();
            }
            const auto n = std::min(bytes.size(), payloadBytes_ - payloadUsed_);
            if (n) std::memcpy(reinterpret_cast<std::byte*>(samples_.data()) + payloadUsed_, bytes.data(), n);
            bytes = bytes.subspan(n); payloadUsed_ += n;
            if (payloadUsed_ == payloadBytes_) {
                deliver();
                headerUsed_ = payloadUsed_ = 0;
            }
        }
    } catch (...) { failed_ = true; throw; }
}
void Reader::readHeader() {
    const auto* h = header_.data();
    kind_ = get<uint16_t>(h + 6);
    frames_ = get<uint32_t>(h + 40);
    payloadBytes_ = get<uint32_t>(h + 44);
    if (std::memcmp(h, "EPCM", 4) || get<uint16_t>(h + 4) != 1
        || get<uint64_t>(h + 8) != expected_.epoch
        || get<uint64_t>(h + 16) != sequence_ || sequence_ == UINT64_MAX
        || get<uint64_t>(h + 24) != position_
        || get<uint32_t>(h + 32) != expected_.sampleRate
        || get<uint16_t>(h + 36) != expected_.channels || get<uint16_t>(h + 38) != 1
        || get<uint64_t>(h + 56) != 0 || payloadBytes_ > maximumPayloadBytes
        || frames_ > UINT64_MAX - position_) invalid();
    if (kind_ == dataPacket) {
        if (!begun_ || !frames_
            || static_cast<uint64_t>(frames_) * expected_.channels * sizeof(float) != payloadBytes_)
            invalid();
    } else if ((kind_ != beginPacket && kind_ != endPacket) || frames_ || payloadBytes_
        || (kind_ == beginPacket && begun_) || (kind_ == endPacket && !begun_)) invalid();
}
void Reader::deliver() {
    if (kind_ == dataPacket) {
        for (size_t i = 0; i < payloadBytes_ / sizeof(float); ++i) {
            const auto bits = get<uint32_t>(reinterpret_cast<const std::byte*>(samples_.data() + i));
            samples_[i] = std::bit_cast<float>(bits);
            if (!std::isfinite(samples_[i])) invalid();
        }
        if (!push_(samples_.data(), static_cast<int>(frames_)))
            throw std::runtime_error("native PCM output session stopped");
    } else if (kind_ == beginPacket) { begin_(); begun_ = true; }
    else { end_(); ended_ = true; }
    ++sequence_; position_ += frames_;
}
void Reader::finish() {
    if (failed_ || !ended_ || headerUsed_ || payloadUsed_) {
        failed_ = true;
        throw std::runtime_error("native PCM stream disconnected before End");
    }
}
} // namespace echo::pcm_wire
