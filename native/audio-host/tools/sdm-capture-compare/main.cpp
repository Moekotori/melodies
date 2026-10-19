#include "../sdm-analysis-lab/SdmSpectrumAnalysis.h"
#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <stdexcept>
#include <string>
#include <vector>

namespace {
constexpr double pi = 3.14159265358979323846;
constexpr size_t windowSamples = 1u << 18u;
constexpr std::array<echo::sdmLab::FrequencyBand, 4> bands {{{20, 20'000},
    {20'000, 100'000}, {100'000, 200'000}, {200'000, 500'000}}};
uint64_t word(std::istream& input, int bytes)
{
    uint64_t value = 0;
    for (int index = 0; index < bytes; ++index)
    {
        const int byte = input.get();
        if (byte == EOF) throw std::runtime_error("capture_truncated_header");
        value |= static_cast<uint64_t>(static_cast<uint8_t>(byte)) << (index * 8);
    }
    return value;
}
std::string tag(std::istream& input)
{
    std::string value(4, '\0'); input.read(value.data(), 4);
    if (!input) throw std::runtime_error("capture_truncated_chunk");
    return value;
}
struct Dsf {
    std::ifstream input;
    int channels = 0, rate = 0, bitOrder = 0, blockSize = 0;
    uint64_t sampleCount = 0, dataOffset = 0, dataBytes = 0;
    explicit Dsf(const std::string& path) : input(path, std::ios::binary)
    {
        if (tag(input) != "DSD " || word(input, 8) != 28) throw std::runtime_error("capture_not_dsf");
        const auto fileBytes = word(input, 8); word(input, 8);
        if (tag(input) != "fmt " || word(input, 8) != 52) throw std::runtime_error("capture_invalid_fmt");
        if (word(input, 4) != 1 || word(input, 4) != 0) throw std::runtime_error("capture_unsupported_dsf_version");
        word(input, 4); channels = static_cast<int>(word(input, 4));
        rate = static_cast<int>(word(input, 4)); bitOrder = static_cast<int>(word(input, 4));
        sampleCount = word(input, 8); blockSize = static_cast<int>(word(input, 4)); word(input, 4);
        if (channels < 1 || channels > 2 || rate < 2'822'400 || rate > 24'576'000
            || (bitOrder != 1 && bitOrder != 8) || blockSize != 4096)
            throw std::runtime_error("capture_unsupported_dsf_layout");
        if (tag(input) != "data") throw std::runtime_error("capture_missing_data");
        const uint64_t chunkBytes = word(input, 8);
        dataOffset = static_cast<uint64_t>(input.tellg());
        if (chunkBytes < 12 || fileBytes < dataOffset || chunkBytes - 12 > fileBytes - dataOffset)
            throw std::runtime_error("capture_invalid_data_size");
        dataBytes = chunkBytes - 12;
        input.seekg(0, std::ios::end);
        if (static_cast<uint64_t>(input.tellg()) != fileBytes) throw std::runtime_error("capture_file_size_mismatch");
    }
    std::vector<double> readWindow(uint64_t startBit, int channel)
    {
        if (startBit > sampleCount || windowSamples > sampleCount - startBit)
            throw std::runtime_error("capture_window_exceeds_declared_samples");
        std::vector<double> output(windowSamples);
        std::array<uint8_t, 4096> block {};
        uint64_t loaded = UINT64_MAX;
        for (size_t sample = 0; sample < output.size(); ++sample)
        {
            const uint64_t bit = startBit + sample, byte = bit / 8, blockIndex = byte / 4096;
            if (loaded != blockIndex)
            {
                const auto offset = blockIndex * static_cast<uint64_t>(blockSize * channels)
                    + static_cast<uint64_t>(channel * blockSize);
                if (offset > dataBytes || 4096 > dataBytes - offset) throw std::runtime_error("capture_truncated_audio_block");
                input.seekg(static_cast<std::streamoff>(dataOffset + offset));
                input.read(reinterpret_cast<char*>(block.data()), block.size());
                if (!input) throw std::runtime_error("capture_read_failed");
                loaded = blockIndex;
            }
            const unsigned shift = bitOrder == 1 ? static_cast<unsigned>(bit % 8) : 7u - static_cast<unsigned>(bit % 8);
            output[sample] = (block[byte % 4096] & (1u << shift)) ? 1.0 : -1.0;
        }
        return output;
    }
};
void values(const std::array<double, 4>& values)
{
    std::cout << '[';
    for (size_t index = 0; index < values.size(); ++index) std::cout << (index ? "," : "") << values[index];
    std::cout << ']';
}
void analyze(const std::string& path, double delayBits, double gainDb, const std::string& stimulus,
    double frequency, double amplitude, double sourceStartSeconds, int declaredRate, int declaredChannels)
{
    Dsf dsf(path);
    if (dsf.rate != declaredRate || dsf.channels != declaredChannels)
        throw std::runtime_error("capture_format_declaration_mismatch");
    if (!std::isfinite(delayBits) || delayBits < 0 || !std::isfinite(gainDb)
        || !std::isfinite(sourceStartSeconds) || sourceStartSeconds < 0
        || !std::isfinite(amplitude) || amplitude < 0 || amplitude > 1
        || !std::isfinite(frequency) || frequency < 0 || frequency > 20'000
        || (stimulus != "sine" && stimulus != "dc" && stimulus != "digital-zero"))
        throw std::runtime_error("capture_invalid_analysis_spec");
    const auto start = static_cast<uint64_t>(std::ceil(sourceStartSeconds * dsf.rate + delayBits));
    const auto tones = stimulus == "sine" ? std::vector<double>{frequency} : std::vector<double>{};
    std::cout << "{\"sampleRate\":" << dsf.rate << ",\"channels\":" << dsf.channels
        << ",\"validBitCount\":" << dsf.sampleCount << ",\"analysisStartBit\":" << start
        << ",\"windowSamples\":" << windowSamples << ",\"channelMeasurements\":[";
    for (int channel = 0; channel < dsf.channels; ++channel)
    {
        auto bits = dsf.readWindow(start, channel);
        std::vector<double> expected(bits.size()), error(bits.size());
        const double gain = std::pow(10.0, gainDb / 20.0);
        double dc = 0.0;
        for (size_t sample = 0; sample < bits.size(); ++sample)
        {
            const double time = (static_cast<double>(start + sample) - delayBits) / dsf.rate;
            expected[sample] = gain * (stimulus == "sine" ? amplitude * std::sin(2 * pi * frequency * time)
                : stimulus == "dc" ? amplitude : 0.0);
            error[sample] = bits[sample] - expected[sample];
            dc += bits[sample];
        }
        const auto output = echo::sdmLab::analyzeSpectrum(bits, dsf.rate, bands, tones);
        const auto residual = echo::sdmLab::analyzeSpectrum(error, dsf.rate, bands);
        const auto floor = echo::sdmLab::analyzeSpectrum(expected, dsf.rate, bands, tones);
        std::cout << (channel ? "," : "") << "{\"channel\":" << channel
            << ",\"dcMean\":" << dc / bits.size() << ",\"binWidthHz\":" << output.binWidthHz
            << ",\"equivalentNoiseBandwidthHz\":" << output.equivalentNoiseBandwidthHz
            << ",\"bandPowerDb\":"; values(output.bandPowerDb);
        std::cout << ",\"sourceErrorBandPowerDb\":"; values(residual.bandPowerDb);
        std::cout << ",\"sourceAnalyzerToneExcludedFloorDb\":"; values(floor.bandPowerWithoutExcludedTonesDb);
        std::cout << ",\"strongestNonStimulusToneHz\":" << output.strongestToneHz
            << ",\"strongestNonStimulusTonePeakDb\":" << output.strongestTonePeakDb << '}';
    }
    std::cout << "]}";
}
}
int main(int argc, char** argv)
{
    try
    {
        if (argc != 11) throw std::runtime_error("usage: echo-sdm-capture-compare <dsf> <delayBits> <gainDb> <sine|dc|digital-zero> <frequencyHz> <amplitude> <sourceStartSeconds> <rate> <channels> <specVersion=1>");
        if (std::string(argv[10]) != "1") throw std::runtime_error("unsupported_capture_spec");
        std::cout << std::setprecision(12);
        analyze(argv[1], std::stod(argv[2]), std::stod(argv[3]), argv[4], std::stod(argv[5]),
            std::stod(argv[6]), std::stod(argv[7]), std::stoi(argv[8]), std::stoi(argv[9]));
        std::cout << '\n'; return 0;
    }
    catch (const std::exception& error) { std::cerr << error.what() << '\n'; return 1; }
}
