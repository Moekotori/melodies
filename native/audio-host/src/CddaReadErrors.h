#pragma once

#include <cstdint>
#include <span>
#include <stdexcept>
#include <string>
#include <utility>

enum class CddaReadFault : std::uint8_t
{
    None = 0, Io = 1, ShortRead = 2, Overlap = 3, MediaChanged = 4,
    NoMedia = 5, DeviceGone = 6, MediaStateChanged = 7, AccessDenied = 8, RetryTimeout = 9,
};

struct CddaSense
{
    bool valid = false;
    std::uint8_t key = 0;
    std::uint8_t asc = 0;
    std::uint8_t ascq = 0;
};

/** Only consume bytes both returned and declared by fixed/descriptor sense headers. */
inline CddaSense parseCddaSense(std::span<const std::uint8_t> bytes)
{
    if (bytes.empty()) return {};
    const auto response = bytes[0] & 0x7fu;
    if ((response == 0x70 || response == 0x71) && bytes.size() >= 14 && bytes[7] >= 6)
        return { true, static_cast<std::uint8_t>(bytes[2] & 0x0fu), bytes[12], bytes[13] };
    if ((response == 0x72 || response == 0x73) && bytes.size() >= 8)
        return { true, static_cast<std::uint8_t>(bytes[1] & 0x0fu), bytes[2], bytes[3] };
    return {};
}

class CddaReadError : public std::runtime_error
{
public:
    CddaReadError(std::string message, CddaReadFault faultToUse, std::uint32_t nativeErrorToUse = 0, CddaSense senseToUse = {})
        : std::runtime_error(std::move(message)), fault(faultToUse), nativeError(nativeErrorToUse), sense(senseToUse) {}
    const CddaReadFault fault;
    const std::uint32_t nativeError;
    const CddaSense sense;
};

class CddaFatalReadError final : public CddaReadError
{
public:
    using CddaReadError::CddaReadError;
};

inline void rejectCddaMediaStateChange(CddaSense sense)
{
    if (!sense.valid) return;
    if (sense.key == 0x06)
        throw CddaFatalReadError(sense.asc == 0x28 ? "audio_cd_disc_changed" : "audio_cd_media_state_changed",
            sense.asc == 0x28 ? CddaReadFault::MediaChanged : CddaReadFault::MediaStateChanged, 0, sense);
    if (sense.key == 0x02 && sense.asc == 0x3a)
        throw CddaFatalReadError("audio_cd_no_media", CddaReadFault::NoMedia, 0, sense);
}
