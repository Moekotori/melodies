#include "../src/CoreAudioDopPacking.h"

#include <array>
#include <cstdint>
#include <iostream>
#include <stdexcept>

namespace
{
void require(bool condition, const char* message)
{
    if (! condition)
        throw std::runtime_error(message);
}

void testPacked24LittleEndian()
{
    std::array<std::uint8_t, 4> bytes { 0xcc, 0xcc, 0xcc, 0xcc };
    echo_audio_host::writeCoreAudioDopSample(bytes.data(), 0x05abcd, 3, false, false);
    require(bytes == std::array<std::uint8_t, 4> { 0xcd, 0xab, 0x05, 0xcc },
        "24-bit little-endian DoP packing preserves payload and marker");
}

void testAlignedHigh32LittleEndian()
{
    std::array<std::uint8_t, 4> bytes {};
    echo_audio_host::writeCoreAudioDopSample(bytes.data(), 0xfa6969, 4, false, true);
    require(bytes == std::array<std::uint8_t, 4> { 0x00, 0x69, 0x69, 0xfa },
        "32-bit aligned-high DoP packing keeps marker in the most significant byte");
}

void testPacked24BigEndian()
{
    std::array<std::uint8_t, 4> bytes { 0xcc, 0xcc, 0xcc, 0xcc };
    echo_audio_host::writeCoreAudioDopSample(bytes.data(), 0x051234, 3, true, false);
    require(bytes == std::array<std::uint8_t, 4> { 0x05, 0x12, 0x34, 0xcc },
        "24-bit big-endian DoP packing preserves payload and marker");
}
}

int main()
{
    try
    {
        testPacked24LittleEndian();
        testAlignedHigh32LittleEndian();
        testPacked24BigEndian();
        std::cout << "CoreAudio DoP packing tests passed\n";
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << "CoreAudio DoP packing tests failed: " << error.what() << '\n';
        return 1;
    }
}
