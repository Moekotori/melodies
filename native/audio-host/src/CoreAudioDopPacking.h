#pragma once

#include <cstdint>

namespace echo_audio_host
{
inline void writeCoreAudioDopSample(
    std::uint8_t* destination,
    std::uint32_t dopSample,
    std::uint32_t containerBytes,
    bool bigEndian,
    bool alignedHigh) noexcept
{
    if (destination == nullptr || (containerBytes != 3 && containerBytes != 4))
        return;

    std::uint32_t word = dopSample & 0x00ffffffu;
    if (containerBytes == 4 && alignedHigh)
        word <<= 8u;

    if (bigEndian)
    {
        for (std::uint32_t byte = 0; byte < containerBytes; ++byte)
            destination[byte] = static_cast<std::uint8_t>(word >> (8u * (containerBytes - 1u - byte)));
        return;
    }

    for (std::uint32_t byte = 0; byte < containerBytes; ++byte)
        destination[byte] = static_cast<std::uint8_t>(word >> (8u * byte));
}
}
