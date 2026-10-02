#if defined(__linux__)

#include "LinuxCddaDevice.h"

#include <cerrno>
#include <fcntl.h>
#include <linux/cdrom.h>
#include <scsi/sg.h>
#include <stdexcept>
#include <sys/ioctl.h>
#include <unistd.h>
#include <vector>
#include <algorithm>

namespace
{
constexpr std::uint32_t cddaRawSectorBytes = 2352;
constexpr std::uint32_t readBatchSectors = 16;
constexpr unsigned char scsiReadCd = 0xBE;

void rejectTerminalLinuxReadError(int code)
{
    if (code == ENOMEDIUM) throw CddaFatalReadError("audio_cd_no_media", CddaReadFault::NoMedia, code);
    if (code == ENODEV || code == ENXIO) throw CddaFatalReadError("audio_cd_device_removed", CddaReadFault::DeviceGone, code);
    if (code == EACCES || code == EPERM) throw CddaFatalReadError("audio_cd_access_denied", CddaReadFault::AccessDenied, code);
}

int openLinuxCddaDevice(const std::string& drive)
{
    if (! isAllowedLinuxCddaDevicePath(drive))
        throw std::runtime_error("audio_cd_invalid_drive");
    const int fd = ::open(drive.c_str(), O_RDONLY | O_NONBLOCK);
    if (fd < 0)
        throw std::runtime_error("audio_cd_open_failed_linux_" + std::to_string(errno));
    return fd;
}

std::vector<std::uint8_t> readAudioSectorsSgIo(int fd, std::uint32_t startLba, std::uint32_t sectorCount)
{
    std::vector<std::uint8_t> raw(static_cast<std::size_t>(sectorCount) * cddaRawSectorBytes);
    unsigned char command[12] {};
    command[0] = scsiReadCd;
    command[1] = 0x04;
    command[2] = static_cast<unsigned char>((startLba >> 24) & 0xffu);
    command[3] = static_cast<unsigned char>((startLba >> 16) & 0xffu);
    command[4] = static_cast<unsigned char>((startLba >> 8) & 0xffu);
    command[5] = static_cast<unsigned char>(startLba & 0xffu);
    command[6] = static_cast<unsigned char>((sectorCount >> 16) & 0xffu);
    command[7] = static_cast<unsigned char>((sectorCount >> 8) & 0xffu);
    command[8] = static_cast<unsigned char>(sectorCount & 0xffu);
    command[9] = 0x10;

    sg_io_hdr header {};
    header.interface_id = 'S';
    header.dxfer_direction = SG_DXFER_FROM_DEV;
    header.cmd_len = sizeof(command);
    unsigned char sense[32] {};
    header.mx_sb_len = sizeof(sense);
    header.sbp = sense;
    header.dxfer_len = static_cast<unsigned>(raw.size());
    header.dxferp = raw.data();
    header.cmdp = command;
    header.timeout = 8'000;
    if (ioctl(fd, SG_IO, &header) < 0)
    {
        const int code = errno;
        rejectTerminalLinuxReadError(code);
        throw CddaReadError("audio_cd_raw_read_failed_linux_" + std::to_string(code), CddaReadFault::Io, code);
    }
    validateLinuxCddaTransfer(header.status, header.host_status, header.driver_status, header.resid,
        std::span<const std::uint8_t>(sense, std::min<std::size_t>(header.sb_len_wr, sizeof(sense))));
    return raw;
}
} // namespace

LinuxCddaDevice::LinuxCddaDevice(const std::string& drive) : fd(openLinuxCddaDevice(drive)) {}

LinuxCddaDevice::~LinuxCddaDevice()
{
    if (fd >= 0)
        ::close(fd);
}

bool LinuxCddaDevice::mediaLoaded() const
{
    const int status = ioctl(fd, CDROM_DRIVE_STATUS, CDSL_CURRENT);
    return status == CDS_DISC_OK;
}

void LinuxCddaDevice::eject()
{
    if (ioctl(fd, CDROMEJECT) < 0)
        throw std::runtime_error("audio_cd_eject_failed_linux_" + std::to_string(errno));
}

std::vector<CddaTrackInfo> LinuxCddaDevice::readToc()
{
    cdrom_tochdr header {};
    if (ioctl(fd, CDROMREADTOCHDR, &header) < 0)
        throw std::runtime_error("audio_cd_read_toc_failed_linux_" + std::to_string(errno));
    if (header.cdth_trk0 < 1 || header.cdth_trk1 < header.cdth_trk0)
        throw std::runtime_error("audio_cd_invalid_toc");

    std::vector<CddaTrackInfo> tracks;
    for (int number = header.cdth_trk0; number <= header.cdth_trk1; ++number)
    {
        cdrom_tocentry entry {};
        entry.cdte_track = static_cast<unsigned char>(number);
        entry.cdte_format = CDROM_LBA;
        if (ioctl(fd, CDROMREADTOCENTRY, &entry) < 0)
            throw std::runtime_error("audio_cd_read_toc_failed_linux_" + std::to_string(errno));
        CddaTrackInfo track;
        track.number = number;
        track.startLba = static_cast<std::uint32_t>(entry.cdte_addr.lba);
        track.audio = (entry.cdte_ctrl & CDROM_DATA_TRACK) == 0;
        track.preEmphasis = track.audio && (entry.cdte_ctrl & 0x01u) != 0;
        tracks.push_back(track);
    }

    cdrom_tocentry leadout {};
    leadout.cdte_track = CDROM_LEADOUT;
    leadout.cdte_format = CDROM_LBA;
    if (ioctl(fd, CDROMREADTOCENTRY, &leadout) < 0)
        throw std::runtime_error("audio_cd_read_toc_failed_linux_" + std::to_string(errno));

    for (std::size_t index = 0; index < tracks.size(); ++index)
    {
        const auto endLba = index + 1 < tracks.size()
            ? tracks[index + 1].startLba
            : static_cast<std::uint32_t>(leadout.cdte_addr.lba);
        tracks[index].endLba = endLba;
    }
    return tracks;
}

std::vector<std::uint8_t> LinuxCddaDevice::readAudioSectors(
    std::uint32_t startLba,
    std::uint32_t sectorCount)
{
    if (sectorCount == 0 || sectorCount > readBatchSectors)
        throw std::runtime_error("audio_cd_invalid_sector_count");

    cdrom_read_audio request {};
    request.addr.lba = static_cast<int>(startLba);
    request.addr_format = CDROM_LBA;
    request.nframes = static_cast<int>(sectorCount);
    std::vector<std::uint8_t> raw(static_cast<std::size_t>(sectorCount) * cddaRawSectorBytes);
    request.buf = raw.data();
    if (ioctl(fd, CDROMREADAUDIO, &request) == 0)
        return raw;
    const int code = errno;
    rejectTerminalLinuxReadError(code);
    return readAudioSectorsSgIo(fd, startLba, sectorCount);
}

#endif
