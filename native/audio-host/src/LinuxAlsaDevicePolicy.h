#pragma once

#include <string_view>

// Miniaudio's simplified ALSA enumeration uses :CARD,DEV for hardware.
// Plugin aliases (front, surround, dmix, default, etc.) are not independent
// exclusive endpoints, even when their descriptions look like sound cards.
inline bool isLinuxAlsaHardwareDevice(std::string_view id)
{
    return (id.size() > 1 && id.front() == ':')
        || (id.size() > 3 && id.substr(0, 3) == "hw:");
}

// Validate the converted hardware coordinates without opening a PCM stream.
// ALSA HDMI aliases can otherwise produce nonexistent card/device pairs.
bool isLinuxAlsaPlaybackDevice(std::string_view id);

// PipeWire releases idle ALSA devices asynchronously (normally after 5 s).
// Retry only EBUSY, on the device-control thread, with no retained buffers.
// Callers must not repeat this budget for different buffer sizes.
template <typename Open, typename Wait, typename Result>
Result openLinuxAlsaAfterIdleRelease(Open open, Wait wait, Result busy)
{
    auto result = open();
    for (int retry = 0; result == busy && retry < 60; ++retry)
    {
        wait(100);
        result = open();
    }
    return result;
}
