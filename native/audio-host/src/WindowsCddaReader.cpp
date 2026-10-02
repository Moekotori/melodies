#include "WindowsCddaReader.h"
#include "CddaReadPolicy.h"
#include "CddaSectorStream.h"
#include "CddaPcmProcessor.h"
#include "CddaOutputResampler.h"

#include "HostUtils.h"
#if defined(__linux__)
#include "LinuxCddaDevice.h"
#endif

#include <algorithm>
#include <cstdint>
#include <cmath>
#include <cstdio>
#include <exception>
#include <stdexcept>
#include <string>
#include <utility>
#include <vector>

#ifdef _WIN32
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <fcntl.h>
#include <io.h>
#include <windows.h>
#include <ntddcdrm.h>
#endif

namespace
{
constexpr std::uint32_t cddaRawSectorBytes = 2352;
constexpr std::uint32_t cddaLogicalSectorBytes = 2048;
constexpr std::uint32_t readBatchSectors = 16;

#ifdef _WIN32
class ScopedHandle final
{
public:
    explicit ScopedHandle(HANDLE value) : handle(value) {}
    ~ScopedHandle()
    {
        if (handle != INVALID_HANDLE_VALUE)
            CloseHandle(handle);
    }

    ScopedHandle(const ScopedHandle&) = delete;
    ScopedHandle& operator=(const ScopedHandle&) = delete;
    ScopedHandle(ScopedHandle&& other) noexcept : handle(other.handle)
    {
        other.handle = INVALID_HANDLE_VALUE;
    }
    ScopedHandle& operator=(ScopedHandle&&) = delete;

    HANDLE get() const { return handle; }

private:
    HANDLE handle = INVALID_HANDLE_VALUE;
};

std::wstring normalizeDrivePath(const std::string& drive)
{
    if (drive.size() != 2 || drive[1] != ':'
        || ! ((drive[0] >= 'A' && drive[0] <= 'Z') || (drive[0] >= 'a' && drive[0] <= 'z')))
        throw std::runtime_error("audio_cd_invalid_drive");

    wchar_t letter = static_cast<wchar_t>(drive[0]);
    if (letter >= L'a' && letter <= L'z')
        letter = static_cast<wchar_t>(letter - L'a' + L'A');
    return std::wstring(L"\\\\.\\") + letter + L":";
}

ScopedHandle openDrive(const std::string& drive)
{
    const auto path = normalizeDrivePath(drive);
    ScopedHandle handle(CreateFileW(
        path.c_str(),
        GENERIC_READ,
        FILE_SHARE_READ | FILE_SHARE_WRITE,
        nullptr,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        nullptr));
    if (handle.get() == INVALID_HANDLE_VALUE)
        throw std::runtime_error("audio_cd_open_failed_win32_" + std::to_string(GetLastError()));
    return handle;
}

std::uint32_t readBigEndianLba(const UCHAR address[4])
{
    return (static_cast<std::uint32_t>(address[0]) << 24u)
        | (static_cast<std::uint32_t>(address[1]) << 16u)
        | (static_cast<std::uint32_t>(address[2]) << 8u)
        | static_cast<std::uint32_t>(address[3]);
}

class WindowsCddaDevice final : public CddaDevice
{
public:
    explicit WindowsCddaDevice(const std::string& drive) : handle(openDrive(drive)) {}

    std::vector<CddaTrackInfo> readToc() override
    {
        CDROM_READ_TOC_EX request {};
        request.Format = CDROM_READ_TOC_EX_FORMAT_TOC;
        request.Msf = 0;
        request.SessionTrack = 0;

        CDROM_TOC toc {};
        DWORD bytesReturned = 0;
        if (! DeviceIoControl(
                handle.get(),
                IOCTL_CDROM_READ_TOC_EX,
                &request,
                sizeof(request),
                &toc,
                sizeof(toc),
                &bytesReturned,
                nullptr))
            throw std::runtime_error("audio_cd_read_toc_failed_win32_" + std::to_string(GetLastError()));

        if (toc.FirstTrack < 1 || toc.LastTrack < toc.FirstTrack)
            throw std::runtime_error("audio_cd_invalid_toc");

        const auto trackCount = static_cast<std::size_t>(toc.LastTrack - toc.FirstTrack + 1);
        if (trackCount == 0 || trackCount >= MAXIMUM_NUMBER_TRACKS)
            throw std::runtime_error("audio_cd_invalid_track_count");

        std::vector<CddaTrackInfo> tracks;
        tracks.reserve(trackCount);
        for (std::size_t index = 0; index < trackCount; ++index)
        {
            const auto& current = toc.TrackData[index];
            const auto& next = toc.TrackData[index + 1];
            const auto startLba = readBigEndianLba(current.Address);
            const auto endLba = readBigEndianLba(next.Address);
            if (endLba <= startLba)
                throw std::runtime_error("audio_cd_invalid_track_bounds");

            tracks.push_back({
                static_cast<int>(current.TrackNumber),
                startLba,
                endLba,
                (current.Control & 0x04u) == 0,
                (current.Control & 0x04u) == 0 && (current.Control & AUDIO_WITH_PREEMPHASIS) != 0,
            });
        }
        return tracks;
    }

    std::vector<std::uint8_t> readCdText() override
    {
        CDROM_READ_TOC_EX request {};
        request.Format = CDROM_READ_TOC_EX_FORMAT_CDTEXT;
        std::vector<std::uint8_t> data(32768);
        DWORD returned = 0;
        // CD-Text is optional. Its absence must never prevent audio playback.
        if (!DeviceIoControl(handle.get(), IOCTL_CDROM_READ_TOC_EX, &request, sizeof(request),
                             data.data(), static_cast<DWORD>(data.size()), &returned, nullptr) || returned < 4)
            return {};
        const auto declared = (static_cast<std::size_t>(data[0]) << 8u) + data[1] + 2u;
        data.resize(std::min({declared, static_cast<std::size_t>(returned), data.size()}));
        return data;
    }

    std::vector<std::uint8_t> readAudioSectors(
        std::uint32_t startLba,
        std::uint32_t sectorCount) override
    {
        if (sectorCount == 0 || sectorCount > readBatchSectors)
            throw std::runtime_error("audio_cd_invalid_sector_count");

        RAW_READ_INFO request {};
        request.DiskOffset.QuadPart = static_cast<LONGLONG>(startLba) * cddaLogicalSectorBytes;
        request.SectorCount = sectorCount;
        request.TrackMode = CDDA;

        std::vector<std::uint8_t> raw(sectorCount * cddaRawSectorBytes);
        DWORD bytesReturned = 0;
        if (! DeviceIoControl(
                handle.get(),
                IOCTL_CDROM_RAW_READ,
                &request,
                sizeof(request),
                raw.data(),
                static_cast<DWORD>(raw.size()),
                &bytesReturned,
                nullptr))
        {
            const auto code = GetLastError();
            if (code == ERROR_MEDIA_CHANGED) throw CddaFatalReadError("audio_cd_disc_changed", CddaReadFault::MediaChanged, code);
            if (code == ERROR_NO_MEDIA_IN_DRIVE) throw CddaFatalReadError("audio_cd_no_media", CddaReadFault::NoMedia, code);
            if (code == ERROR_DEVICE_NOT_CONNECTED) throw CddaFatalReadError("audio_cd_device_removed", CddaReadFault::DeviceGone, code);
            if (code == ERROR_ACCESS_DENIED) throw CddaFatalReadError("audio_cd_access_denied", CddaReadFault::AccessDenied, code);
            throw CddaReadError("audio_cd_raw_read_failed_win32_" + std::to_string(code), CddaReadFault::Io, code);
        }
        if (bytesReturned != raw.size())
            throw CddaReadError("audio_cd_short_raw_read", CddaReadFault::ShortRead);
        return raw;
    }

private:
    ScopedHandle handle;
};
#endif

std::unique_ptr<CddaDevice> createPlatformDevice(const std::string& drive)
{
#ifdef _WIN32
    return std::make_unique<WindowsCddaDevice>(drive);
#elif defined(__linux__)
    return std::make_unique<LinuxCddaDevice>(drive);
#else
    (void) drive;
    throw std::runtime_error("audio_cd_windows_backend_unavailable");
#endif
}

void writeTrackJson(const std::vector<CddaTrackInfo>& tracks, const std::vector<std::uint8_t>& cdText)
{
    std::string json = "{\"tracks\":[";
    for (std::size_t index = 0; index < tracks.size(); ++index)
    {
        if (index > 0)
            json += ',';
        const auto& track = tracks[index];
        json += "{\"index\":" + std::to_string(track.number)
            + ",\"startLba\":" + std::to_string(track.startLba)
            + ",\"endLba\":" + std::to_string(track.endLba)
            + ",\"audio\":" + (track.audio ? "true" : "false")
            + ",\"preEmphasis\":" + (track.preEmphasis ? "true" : "false") + '}';
    }
    json += "],\"cdText\":[";
    for (std::size_t index = 0; index < cdText.size(); ++index)
    {
        if (index) json += ',';
        json += std::to_string(cdText[index]);
    }
    json += "]}";
    writeJsonLine(json);
}
} // namespace

WindowsCddaReader::WindowsCddaReader(DeviceFactory factory, std::function<double()> clock)
    : deviceFactory(factory ? std::move(factory) : DeviceFactory(createPlatformDevice)), readClock(std::move(clock))
{
}

std::vector<CddaTrackInfo> WindowsCddaReader::inspectDisc(const std::string& drive) const
{
    auto device = deviceFactory(drive);
    if (! device)
        throw std::runtime_error("audio_cd_device_factory_failed");
    return device->readToc();
}

void WindowsCddaReader::streamTrackTo(
    const std::string& drive,
    int trackNumber,
    const PcmSink& sink, double startSeconds, int expectedStartLba, int expectedEndLba, int lastTrackNumber, int outputSampleRate) const
{
    if (! sink)
        throw std::runtime_error("audio_cd_pcm_sink_required");
    auto device = deviceFactory(drive);
    if (! device)
        throw std::runtime_error("audio_cd_device_factory_failed");
    const auto tracks = device->readToc();
    const auto found = std::find_if(tracks.begin(), tracks.end(), [trackNumber] (const auto& track)
    {
        return track.number == trackNumber;
    });
    if (found == tracks.end())
        throw std::runtime_error("audio_cd_track_not_found");
    if (!found->audio) throw std::runtime_error("audio_cd_track_is_data");
    const auto last = lastTrackNumber > 0 ? std::find_if(found, tracks.end(), [lastTrackNumber](const auto& track) {
        return track.number == lastTrackNumber;
    }) : found;
    if (last == tracks.end() || !std::all_of(found, last + 1, [](const auto& track) { return track.audio; }))
        throw std::runtime_error("audio_cd_invalid_sequence");
    const auto endLba = last->endLba;
    if ((expectedStartLba >= 0 && found->startLba != static_cast<std::uint32_t>(expectedStartLba)) ||
        (expectedEndLba >= 0 && endLba != static_cast<std::uint32_t>(expectedEndLba)))
        throw std::runtime_error("audio_cd_disc_changed");
    if (!std::isfinite(startSeconds) || startSeconds < 0)
        throw std::runtime_error("audio_cd_invalid_position");
    const auto offset = std::min(std::floor(startSeconds * 75.0), static_cast<double>(endLba - found->startLba));
    const auto requestedLba = found->startLba + static_cast<std::uint32_t>(offset);
    CddaReadPolicy readPolicy(readClock);
    const CddaReadSummary readSummary(readPolicy);
    // One prior sector primes both processing stages; never read before the selected audio track.
    const auto readStart = (found->preEmphasis || outputSampleRate != 44100) && requestedLba > found->startLba && requestedLba < endLba
        ? requestedLba - 1 : requestedLba;
    CddaSectorStream sectorStream(*device, readPolicy, readStart, endLba);
    CddaPcmProcessor processor(tracks);
    const int discardFrames = static_cast<int>(static_cast<std::int64_t>(requestedLba - readStart) * outputSampleRate / 75);
    CddaOutputResampler resampler(outputSampleRate, sink, discardFrames);
    while (const auto chunk = sectorStream.next())
    {
        auto pcm = processor.convert(chunk->bytes, chunk->startLba);
        readPolicy.setDeemphasisSectors(processor.deemphasizedSectors());
        if (!pcm.empty()) resampler.process(pcm);
    }
    resampler.finish();
}

int WindowsCddaReader::writeToc(const std::string& drive) const
{
    auto device = deviceFactory(drive);
    if (!device) throw std::runtime_error("audio_cd_device_factory_failed");
    const auto tracks = device->readToc();
    writeTrackJson(tracks, device->readCdText());
    return 0;
}

int WindowsCddaReader::writeCapabilities() const
{
#ifdef _WIN32
    writeJsonLine("{\"windowsNativeCdda\":true,\"linuxNativeCdda\":false,\"nativeCdda\":true,\"nativeCddaOutputRate\":true}");
    return 0;
#elif defined(__linux__)
    writeJsonLine("{\"windowsNativeCdda\":false,\"linuxNativeCdda\":true,\"nativeCdda\":true,\"nativeCddaOutputRate\":true}");
    return 0;
#else
    writeJsonLine("{\"windowsNativeCdda\":false,\"linuxNativeCdda\":false,\"nativeCdda\":false}");
    return 1;
#endif
}

int WindowsCddaReader::writeStatus(const std::string& drive) const
{
#if defined(__linux__)
    LinuxCddaDevice device(drive);
    writeJsonLine(device.mediaLoaded() ? "{\"mediaLoaded\":true}" : "{\"mediaLoaded\":false}");
    return 0;
#else
    (void) drive;
    writeJsonLine("{\"mediaLoaded\":true}");
    return 0;
#endif
}

int WindowsCddaReader::streamTrack(const std::string& drive, int trackNumber, double startSeconds, int expectedStartLba, int expectedEndLba, int lastTrackNumber, int outputSampleRate) const
{
#ifdef _WIN32
    if (_setmode(_fileno(stdout), _O_BINARY) == -1)
        throw std::runtime_error("audio_cd_stdout_binary_mode_failed");
#endif
    streamTrackTo(drive, trackNumber, [] (const std::vector<float>& samples)
    {
        const auto written = std::fwrite(samples.data(), sizeof(float), samples.size(), stdout);
        if (written != samples.size())
            throw std::runtime_error("audio_cd_stdout_write_failed");
    }, startSeconds, expectedStartLba, expectedEndLba, lastTrackNumber, outputSampleRate);
    std::fflush(stdout);
    return 0;
}

int WindowsCddaReader::eject(const std::string& drive) const
{
#ifdef _WIN32
    auto handle = openDrive(drive);
    DWORD returned = 0;
    if (!DeviceIoControl(handle.get(), IOCTL_STORAGE_EJECT_MEDIA, nullptr, 0, nullptr, 0, &returned, nullptr))
        throw std::runtime_error("audio_cd_eject_failed");
    return 0;
#elif defined(__linux__)
    LinuxCddaDevice device(drive);
    device.eject();
    return 0;
#else
    (void) drive;
    throw std::runtime_error("audio_cd_windows_backend_unavailable");
#endif
}
