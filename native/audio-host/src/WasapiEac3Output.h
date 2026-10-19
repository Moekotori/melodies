#pragma once
#ifdef _WIN32
#include <cstddef>
#include <cstdint>
#include <memory>
#include <string>

namespace echo {
// COM, device and clock belong to the host's single playback worker.
class WasapiEac3Output {
public:
    WasapiEac3Output();
    ~WasapiEac3Output();
    void open(const std::string& deviceId, const std::string& deviceName, int sourceChannels);
    void close();
    uint32_t availableFrames() const;
    uint32_t paddingFrames() const;
    void write(const uint8_t* bytes, uint32_t carrierFrames);
    void start();
    void pause();
    void reset();
    double elapsedSeconds() const;
    const std::string& deviceName() const;
private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
};
}
#endif
