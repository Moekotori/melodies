#include "libav_decoder.h"
#include <cstring>
#include <fstream>
#include <stdexcept>
#include <string>
#include <vector>

void testNativePcmDecodeLayouts(const std::string& root)
{
    for (const auto* name : {"pcm-3point0.wav", "pcm-5point1.wav", "pcm-7point1.wav"})
    {
        const auto path = root + "/" + name;
        std::ifstream file(path, std::ios::binary | std::ios::ate);
        const std::streamoff bytes = file.tellg();
        if (!file || bytes < 68 || bytes > 128 * 1024) throw std::runtime_error("invalid PCM fixture");
        std::vector<float> reference(static_cast<size_t>(bytes - 68) / sizeof(float));
        file.seekg(68);
        file.read(reinterpret_cast<char*>(reference.data()), bytes - 68);
        const int channels = name[4] == '3' ? 3 : name[4] == '5' ? 6 : 8;
        echo::LibavPcmStreamDecoder decoder;
        decoder.open(path, 48000, channels);
        size_t offset = 0;
        while (!decoder.eof())
        {
            const auto chunk = decoder.readFrames(137);
            if (!chunk.frames) break;
            if (offset + chunk.samples.size() > reference.size()
                || std::memcmp(chunk.samples.data(), reference.data() + offset,
                    chunk.samples.size() * sizeof(float)) != 0)
                throw std::runtime_error("PCM channel order or sample identity changed");
            offset += chunk.samples.size();
        }
        if (offset != reference.size()) throw std::runtime_error("PCM frame count changed");
    }
}
