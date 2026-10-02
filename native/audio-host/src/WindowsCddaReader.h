#pragma once

#include <cstdint>
#include <functional>
#include <memory>
#include <string>
#include <vector>

struct CddaTrackInfo
{
    int number = 0;
    std::uint32_t startLba = 0;
    std::uint32_t endLba = 0;
    bool audio = false;
    bool preEmphasis = false;
};

class CddaDevice
{
public:
    virtual ~CddaDevice() = default;
    virtual std::vector<CddaTrackInfo> readToc() = 0;
    virtual std::vector<std::uint8_t> readCdText() { return {}; }
    virtual std::vector<std::uint8_t> readAudioSectors(
        std::uint32_t startLba,
        std::uint32_t sectorCount) = 0;
};

class WindowsCddaReader final
{
public:
    using DeviceFactory = std::function<std::unique_ptr<CddaDevice>(const std::string& drive)>;
    using PcmSink = std::function<void(const std::vector<float>& samples)>;

    explicit WindowsCddaReader(DeviceFactory deviceFactory = {}, std::function<double()> readClock = {});

    std::vector<CddaTrackInfo> inspectDisc(const std::string& drive) const;
    void streamTrackTo(const std::string& drive, int trackNumber, const PcmSink& sink, double startSeconds = 0, int expectedStartLba = -1, int expectedEndLba = -1, int lastTrackNumber = 0, int outputSampleRate = 44100) const;

    int writeCapabilities() const;
    int writeStatus(const std::string& drive) const;
    int writeToc(const std::string& drive) const;
    int streamTrack(const std::string& drive, int trackNumber, double startSeconds = 0, int expectedStartLba = -1, int expectedEndLba = -1, int lastTrackNumber = 0, int outputSampleRate = 44100) const;
    int eject(const std::string& drive) const;

private:
    DeviceFactory deviceFactory;
    std::function<double()> readClock;
};
