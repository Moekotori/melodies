#include "../../src/NativeFirProcessor.h"
#include "../../src/NativeSdmProcessor.h"
#include "../../../audio-engine/third_party/nlohmann_json.hpp"

#include <algorithm>
#include <bit>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <cstdint>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <functional>
#include <iostream>
#include <limits>
#include <memory>
#include <random>
#include <stdexcept>
#include <string>
#include <vector>

#if defined(_WIN32)
#include <fcntl.h>
#include <io.h>
#endif

namespace {

using Json = nlohmann::json;

constexpr int connectSdmStartupIdleMilliseconds = 100;
constexpr uint8_t dsdSilenceByte = 0x69u;

struct Config
{
    std::string mode = "src";
    int channels = 2;
    int maximumInputFrames = 4096;
    bool requestCuda = false;
    float inputGain = 1.0f;
    int transportSampleRate = 0;
    int nativeSampleRate = 0;
    std::string qualityProfile = "safe";
    echo::SdmDecisionMode decisionMode = echo::SdmDecisionMode::Greedy;
    std::vector<echo::EchoSrcStageConfig> stages;
};

Config readConfig(const std::filesystem::path& path)
{
    std::ifstream stream(path, std::ios::binary);
    if (!stream)
        throw std::runtime_error("connect_src_config_open_failed");
    stream.seekg(0, std::ios::end);
    const auto size = stream.tellg();
    if (size <= 0 || size > 16 * 1024 * 1024)
        throw std::runtime_error("connect_src_config_size_invalid");
    stream.seekg(0, std::ios::beg);
    const Json json = Json::parse(stream);

    Config config;
    config.mode = json.value("mode", "src");
    config.channels = json.value("channels", 2);
    config.maximumInputFrames = json.value("maximumInputFrames", 4096);
    config.requestCuda = json.value("requestCuda", false);
    config.inputGain = json.value("inputGain", 1.0f);
    config.transportSampleRate = json.value("transportSampleRate", 0);
    config.nativeSampleRate = json.value("nativeSampleRate", 0);
    config.qualityProfile = json.value("qualityProfile", "safe");
    if (json.contains("extraLoop") && json["extraLoop"].is_string())
    {
        const auto mode = json["extraLoop"].get<std::string>();
        if (mode == "horizon")
            config.decisionMode = echo::SdmDecisionMode::Horizon;
        else if (mode == "extra")
            config.decisionMode = echo::SdmDecisionMode::Extra;
        else if (mode != "off")
            throw std::runtime_error("invalid_sdm_decision_mode");
    }
    else if (json.value("extraLoop", false))
    {
        config.decisionMode = echo::SdmDecisionMode::Extra;
    }
    if (config.mode != "src" && config.mode != "sdm-dsf")
        throw std::runtime_error("connect_src_mode_invalid");
    if (config.channels < 1 || config.channels > 8)
        throw std::runtime_error("connect_src_channels_invalid");
    if (config.maximumInputFrames < 64 || config.maximumInputFrames > 65536)
        throw std::runtime_error("connect_src_block_size_invalid");
    if (!std::isfinite(config.inputGain) || config.inputGain <= 0.0f || config.inputGain > 1.0f)
        throw std::runtime_error("connect_src_input_gain_invalid");

    const auto& stages = json.at("stages");
    if (!stages.is_array() || stages.empty() || stages.size() > 5)
        throw std::runtime_error("connect_src_stages_invalid");
    int totalFactor = 1;
    for (const auto& item : stages)
    {
        echo::EchoSrcStageConfig stage;
        stage.upsampleFactor = item.value("upsampleFactor", 0);
        const auto& taps = item.at("taps");
        if (!taps.is_array() || taps.empty() || taps.size() > 8191)
            throw std::runtime_error("connect_src_taps_invalid");
        stage.taps.reserve(taps.size());
        for (const auto& tap : taps)
        {
            const double value = tap.get<double>();
            if (!std::isfinite(value) || value < -16.0 || value > 16.0)
                throw std::runtime_error("connect_src_tap_value_invalid");
            stage.taps.push_back(static_cast<float>(value));
        }
        if (stage.upsampleFactor < 1
            || totalFactor > 32 / stage.upsampleFactor)
            throw std::runtime_error("connect_src_factor_invalid");
        totalFactor *= stage.upsampleFactor;
        config.stages.push_back(std::move(stage));
    }
    if (totalFactor < 2 || totalFactor > 32)
        throw std::runtime_error("connect_src_total_factor_invalid");
    if (config.mode == "sdm-dsf")
    {
        if (config.channels < 1 || config.channels > 2)
            throw std::runtime_error("connect_sdm_dsf_channels_unsupported");
        if (config.transportSampleRate <= 0
            || config.nativeSampleRate != config.transportSampleRate * echo::sdmBitsPerTransportSample)
            throw std::runtime_error("connect_sdm_rate_invalid");
        if (config.qualityProfile != "safe" && config.qualityProfile != "hifi"
            && config.qualityProfile != "reference" && config.qualityProfile != "insane"
            && config.qualityProfile != "adaptive" && config.qualityProfile != "adaptive-light"
            && config.qualityProfile != "adaptive-super" && config.qualityProfile != "reference-ec")
            throw std::runtime_error("connect_sdm_quality_invalid");
    }
    return config;
}

echo::SdmQualityProfile parseSdmProfile(const std::string& profile)
{
    if (profile == "hifi") return echo::SdmQualityProfile::Hifi;
    if (profile == "reference") return echo::SdmQualityProfile::Reference;
    if (profile == "insane") return echo::SdmQualityProfile::Insane;
    if (profile == "adaptive") return echo::SdmQualityProfile::Adaptive;
    if (profile == "adaptive-light") return echo::SdmQualityProfile::AdaptiveLight;
    if (profile == "adaptive-super") return echo::SdmQualityProfile::AdaptiveSuper;
    if (profile == "reference-ec") return echo::SdmQualityProfile::ReferenceEc;
    return echo::SdmQualityProfile::Safe;
}

void writeU32(std::ostream& output, uint32_t value)
{
    const uint8_t bytes[4] {
        static_cast<uint8_t>(value),
        static_cast<uint8_t>(value >> 8u),
        static_cast<uint8_t>(value >> 16u),
        static_cast<uint8_t>(value >> 24u),
    };
    output.write(reinterpret_cast<const char*>(bytes), 4);
}

void writeU64(std::ostream& output, uint64_t value)
{
    uint8_t bytes[8] {};
    for (int index = 0; index < 8; ++index)
        bytes[index] = static_cast<uint8_t>(value >> static_cast<unsigned int>(index * 8));
    output.write(reinterpret_cast<const char*>(bytes), 8);
}

class DsfWriter final
{
public:
    explicit DsfWriter(int channels)
        : channels_(channels), channelBuffers_(static_cast<size_t>(channels))
    {
        const auto nonce = std::chrono::high_resolution_clock::now().time_since_epoch().count()
            ^ static_cast<long long>(std::random_device {}());
        path_ = std::filesystem::temp_directory_path()
            / ("echo-connect-sdm-" + std::to_string(nonce) + ".raw");
        raw_.open(path_, std::ios::binary | std::ios::trunc);
        if (!raw_)
            throw std::runtime_error("connect_sdm_temp_open_failed");
    }

    ~DsfWriter()
    {
        raw_.close();
        std::error_code ignored;
        std::filesystem::remove(path_, ignored);
    }

    void append(const std::vector<uint8_t>& interleaved, int transportFrames)
    {
        const size_t expected = static_cast<size_t>(transportFrames * channels_ * 2);
        if (transportFrames <= 0 || interleaved.size() != expected)
            throw std::runtime_error("connect_sdm_native_block_invalid");
        for (int frame = 0; frame < transportFrames; ++frame)
        {
            const size_t base = static_cast<size_t>(frame * channels_ * 2);
            for (int channel = 0; channel < channels_; ++channel)
            {
                auto& buffer = channelBuffers_[static_cast<size_t>(channel)];
                buffer.push_back(interleaved[base + static_cast<size_t>(channel)]);
                buffer.push_back(interleaved[base + static_cast<size_t>(channels_ + channel)]);
            }
        }
        sampleCount_ += static_cast<uint64_t>(transportFrames) * 16u;
        flushCompleteBlocks();
    }

    void appendIdleSamples(uint64_t samples)
    {
        if (samples == 0 || samples % 8u != 0)
            throw std::runtime_error("connect_sdm_idle_samples_invalid");
        const size_t bytes = static_cast<size_t>(samples / 8u);
        for (auto& buffer : channelBuffers_)
            buffer.insert(buffer.end(), bytes, dsdSilenceByte);
        sampleCount_ += samples;
        flushCompleteBlocks();
    }

    void finish(int nativeSampleRate)
    {
        if (sampleCount_ == 0)
            throw std::runtime_error("connect_sdm_empty_output");
        if (!channelBuffers_[0].empty())
        {
            for (auto& buffer : channelBuffers_)
                buffer.resize(blockSize, dsdSilenceByte);
            writeBlocks();
        }
        raw_.flush();
        raw_.close();

        const uint64_t dataChunkSize = 12u + dataBytes_;
        const uint64_t fileSize = 28u + 52u + dataChunkSize;
        std::cout.write("DSD ", 4);
        writeU64(std::cout, 28u);
        writeU64(std::cout, fileSize);
        writeU64(std::cout, 0u);
        std::cout.write("fmt ", 4);
        writeU64(std::cout, 52u);
        writeU32(std::cout, 1u);
        writeU32(std::cout, 0u);
        writeU32(std::cout, channels_ == 1 ? 1u : 2u);
        writeU32(std::cout, static_cast<uint32_t>(channels_));
        writeU32(std::cout, static_cast<uint32_t>(nativeSampleRate));
        writeU32(std::cout, 1u);
        writeU64(std::cout, sampleCount_);
        writeU32(std::cout, blockSize);
        writeU32(std::cout, 0u);
        std::cout.write("data", 4);
        writeU64(std::cout, dataChunkSize);

        std::ifstream input(path_, std::ios::binary);
        if (!input)
            throw std::runtime_error("connect_sdm_temp_reopen_failed");
        std::vector<char> copyBuffer(64 * 1024);
        while (input)
        {
            input.read(copyBuffer.data(), static_cast<std::streamsize>(copyBuffer.size()));
            const auto count = input.gcount();
            if (count > 0)
                std::cout.write(copyBuffer.data(), count);
        }
        if (!std::cout)
            throw std::runtime_error("connect_sdm_dsf_write_failed");
    }

    uint64_t sampleCount() const noexcept { return sampleCount_; }

private:
    void flushCompleteBlocks()
    {
        while (!channelBuffers_.empty()
            && std::all_of(channelBuffers_.begin(), channelBuffers_.end(), [](const auto& buffer) {
                return buffer.size() >= blockSize;
            }))
            writeBlocks();
    }

    void writeBlocks()
    {
        for (auto& buffer : channelBuffers_)
        {
            raw_.write(reinterpret_cast<const char*>(buffer.data()), blockSize);
            if (!raw_)
                throw std::runtime_error("connect_sdm_temp_write_failed");
            buffer.erase(buffer.begin(), buffer.begin() + blockSize);
            dataBytes_ += blockSize;
        }
    }

    static constexpr uint32_t blockSize = 4096;
    int channels_ = 2;
    std::vector<std::vector<uint8_t>> channelBuffers_;
    std::filesystem::path path_;
    std::ofstream raw_;
    uint64_t sampleCount_ = 0;
    uint64_t dataBytes_ = 0;
};

void writeSamples(const std::vector<float>& samples)
{
    if (samples.empty())
        return;
    const auto bytes = static_cast<std::streamsize>(samples.size() * sizeof(float));
    std::cout.write(reinterpret_cast<const char*>(samples.data()), bytes);
    if (!std::cout)
        throw std::runtime_error("connect_src_stdout_write_failed");
}

} // namespace

int main(int argc, char** argv)
{
    try
    {
        if constexpr (std::endian::native != std::endian::little)
            throw std::runtime_error("connect_src_little_endian_required");
        if (argc != 3 || std::string(argv[1]) != "--config")
            throw std::runtime_error("usage: echo-connect-src-renderer --config <path>");
#if defined(_WIN32)
        _setmode(_fileno(stdin), _O_BINARY);
        _setmode(_fileno(stdout), _O_BINARY);
#endif

        const Config config = readConfig(std::filesystem::path(argv[2]));
        NativeFirProcessor processor;
        std::string error;
        if (!processor.configure(
                config.channels,
                config.stages,
                config.requestCuda,
                config.maximumInputFrames,
                error))
            throw std::runtime_error(error.empty() ? "connect_src_configure_failed" : error);

        NativeSdmProcessor sdm;
        std::unique_ptr<DsfWriter> dsf;
        if (config.mode == "sdm-dsf")
        {
            if (!sdm.configure(
                    config.channels,
                    parseSdmProfile(config.qualityProfile),
                    config.transportSampleRate,
                    false,
                    config.maximumInputFrames * processor.outputFactor(),
                    error,
                    config.decisionMode))
                throw std::runtime_error(error.empty() ? "connect_sdm_configure_failed" : error);
            dsf = std::make_unique<DsfWriter>(config.channels);
            const uint64_t startupIdleSamples = static_cast<uint64_t>(config.nativeSampleRate)
                * static_cast<uint64_t>(connectSdmStartupIdleMilliseconds) / 1000u;
            dsf->appendIdleSamples(startupIdleSamples);
        }

        const size_t frameBytes = static_cast<size_t>(config.channels) * sizeof(float);
        const size_t blockBytes = static_cast<size_t>(config.maximumInputFrames) * frameBytes;
        std::vector<char> pending;
        pending.reserve(blockBytes + frameBytes);
        std::vector<char> readBuffer(blockBytes);
        uint64_t inputFrames = 0;
        uint64_t outputFrames = 0;

        while (std::cin)
        {
            std::cin.read(readBuffer.data(), static_cast<std::streamsize>(readBuffer.size()));
            const auto received = std::max<std::streamsize>(0, std::cin.gcount());
            pending.insert(pending.end(), readBuffer.begin(), readBuffer.begin() + received);
            const size_t alignedBytes = pending.size() - (pending.size() % frameBytes);
            if (alignedBytes == 0)
                continue;
            const int frames = static_cast<int>(alignedBytes / frameBytes);
            std::vector<float> samples(alignedBytes / sizeof(float));
            std::memcpy(samples.data(), pending.data(), alignedBytes);
            for (auto& sample : samples)
                sample *= config.inputGain;
            auto output = processor.process(samples.data(), frames);
            if (dsf)
            {
                const int transportFrames = static_cast<int>(output.size() / static_cast<size_t>(config.channels));
                dsf->append(sdm.processNativeDsd(output.data(), transportFrames), transportFrames);
            }
            else
            {
                writeSamples(output);
            }
            inputFrames += static_cast<uint64_t>(frames);
            outputFrames += static_cast<uint64_t>(output.size() / static_cast<size_t>(config.channels));
            pending.erase(pending.begin(), pending.begin() + static_cast<std::ptrdiff_t>(alignedBytes));
        }
        if (!pending.empty())
            throw std::runtime_error("connect_src_input_frame_truncated");
        auto tail = processor.flush();
        if (dsf)
        {
            if (!tail.empty())
            {
                const int transportFrames = static_cast<int>(tail.size() / static_cast<size_t>(config.channels));
                dsf->append(sdm.processNativeDsd(tail.data(), transportFrames), transportFrames);
            }
            const auto sdmTail = sdm.flushNativeDsd();
            if (!sdmTail.empty())
            {
                const int transportFrames = static_cast<int>(
                    sdmTail.size() / static_cast<size_t>(config.channels * 2));
                dsf->append(sdmTail, transportFrames);
            }
            dsf->finish(config.nativeSampleRate);
        }
        else
        {
            writeSamples(tail);
        }
        outputFrames += static_cast<uint64_t>(tail.size() / static_cast<size_t>(config.channels));
        std::cout.flush();

        Json status = {
            {"backend", processor.backend() == NativeFirProcessor::Backend::Cuda ? "cuda" : "cpu"},
            {"inputFrames", inputFrames},
            {"outputFrames", outputFrames},
            {"outputFactor", processor.outputFactor()},
            {"tailOutputFrames", processor.tailOutputFrames()},
            {"fallbackReason", processor.fallbackReason()},
            {"mode", config.mode},
            {"nativeSampleRate", dsf ? config.nativeSampleRate : 0},
            {"dsdSamples", dsf ? dsf->sampleCount() : 0},
            {"startupIdleMilliseconds", dsf ? connectSdmStartupIdleMilliseconds : 0},
            {"sdmOrder", dsf ? sdm.modulatorOrder() : 0},
            {"stabilityRecoveries", dsf ? sdm.stabilityRecoveryCount() : 0},
        };
        std::cerr << "ECHO_CONNECT_SRC_STATUS " << status.dump() << '\n';
        return 0;
    }
    catch (const std::exception& exception)
    {
        std::cerr << "echo-connect-src-renderer: " << exception.what() << '\n';
        return 2;
    }
}
