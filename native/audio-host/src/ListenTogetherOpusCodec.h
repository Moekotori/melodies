#pragma once

#include <cstdint>
#include <memory>
#include <vector>

namespace echo::listen_together {

struct OpusCodecConfig {
    int bitrate = 160'000;
    int expectedPacketLossPercent = 5;
};

class OpusEncoder {
public:
    explicit OpusEncoder(OpusCodecConfig config = {});
    ~OpusEncoder();

    OpusEncoder(const OpusEncoder&) = delete;
    OpusEncoder& operator=(const OpusEncoder&) = delete;
    OpusEncoder(OpusEncoder&&) noexcept;
    OpusEncoder& operator=(OpusEncoder&&) noexcept;

    std::vector<std::uint8_t> encode(const float* interleavedStereo, int frames);
    int bitrate() const noexcept;
    int frameSize() const noexcept;

private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
};

class OpusDecoder {
public:
    OpusDecoder();
    ~OpusDecoder();

    OpusDecoder(const OpusDecoder&) = delete;
    OpusDecoder& operator=(const OpusDecoder&) = delete;
    OpusDecoder(OpusDecoder&&) noexcept;
    OpusDecoder& operator=(OpusDecoder&&) noexcept;

    std::vector<float> decode(const std::uint8_t* payload, int payloadBytes);

private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
};

} // namespace echo::listen_together
