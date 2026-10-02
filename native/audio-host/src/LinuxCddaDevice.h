#pragma once

#include "WindowsCddaReader.h"
#include "CddaReadErrors.h"

#include <cctype>
#include <string>
#include <string_view>
#include <stdexcept>

/** Shared with platform-neutral simulations so short transfers cannot look like silence. */
inline void validateLinuxCddaTransfer(unsigned char status, unsigned short hostStatus,
    unsigned short driverStatus, int residualBytes, std::span<const std::uint8_t> senseBytes = {})
{
    const auto sense = parseCddaSense(senseBytes);
    if (hostStatus != 0 || driverStatus != 0 || (status & 0x7e) != 0) rejectCddaMediaStateChange(sense);
    if (hostStatus != 0 || driverStatus != 0 || (status & 0x7e) != 0)
        throw CddaReadError("audio_cd_raw_read_failed_linux_scsi", CddaReadFault::Io, 0, sense);
    if (residualBytes != 0)
        throw CddaReadError("audio_cd_short_raw_read", CddaReadFault::ShortRead);
}

inline bool isAllowedLinuxCddaDevicePath(std::string_view drive)
{
    if (drive.find("..") != std::string_view::npos || drive.find('\\') != std::string_view::npos)
        return false;
    if (drive == "/dev/cdrom" || drive == "/dev/dvd")
        return true;
    if (drive.size() < 8 || ! drive.starts_with("/dev/sr"))
        return false;
    const auto suffix = drive.substr(7);
    if (suffix.empty() || suffix.size() > 3)
        return false;
    for (const unsigned char character : suffix)
    {
        if (! std::isdigit(character))
            return false;
    }
    return true;
}

#if defined(__linux__)
class LinuxCddaDevice final : public CddaDevice
{
public:
    explicit LinuxCddaDevice(const std::string& drive);
    ~LinuxCddaDevice() override;

    LinuxCddaDevice(const LinuxCddaDevice&) = delete;
    LinuxCddaDevice& operator=(const LinuxCddaDevice&) = delete;

    std::vector<CddaTrackInfo> readToc() override;
    std::vector<std::uint8_t> readAudioSectors(std::uint32_t startLba, std::uint32_t sectorCount) override;
    bool mediaLoaded() const;
    void eject();

private:
    int fd = -1;
};
#endif
