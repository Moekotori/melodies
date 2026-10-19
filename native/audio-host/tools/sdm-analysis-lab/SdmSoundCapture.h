#pragma once
#include <cstdint>
#include <filesystem>
#include <fstream>
#include <string>
#include <vector>

namespace echo::sdmLab {
// Owns only generated lab assets; streams each block instead of retaining PCM.
class SoundCapture final {
public:
    SoundCapture(const std::filesystem::path& directory, const std::string& name,
        int sourceRate, int sourceFrames);
    void writeSource(const std::vector<float>& source);
    void writeDsd(const std::vector<uint8_t>& bytes);
    void finish();
private:
    std::ofstream source_;
    std::ofstream dsd_;
    bool enabled_ = false;
};
}
