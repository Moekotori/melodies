#include "BlindEqRenderer.h"
#include "../../audio-engine/DspChain.h"
#include "../../audio-engine/EqMessageProtocol.h"
#include "../../audio-engine/libav_decoder.h"
#include "../../audio-engine/third_party/nlohmann_json.hpp"
#include <array>
#include <filesystem>
#include <fstream>
#include <iostream>

namespace {
constexpr int rate = 48000;
constexpr int channels = 2;
constexpr int block = 1024;

class WaveWriter {
    std::ofstream file;
    uint32_t bytes = 0;
    void integer(uint32_t value, int count) {
        for (int i = 0; i < count; ++i) file.put(static_cast<char>((value >> (8 * i)) & 255));
    }
    void header() {
        file.seekp(0);
        file.write("RIFF", 4); integer(36 + bytes, 4); file.write("WAVEfmt ", 8);
        integer(16, 4); integer(3, 2); integer(channels, 2); integer(rate, 4);
        integer(rate * channels * 4, 4); integer(channels * 4, 2); integer(32, 2);
        file.write("data", 4); integer(bytes, 4);
    }
public:
    explicit WaveWriter(const std::string& path) : file(std::filesystem::u8path(path), std::ios::binary | std::ios::trunc) {
        file.exceptions(std::ios::badbit | std::ios::failbit);
        header();
    }
    void write(const echo::FloatAudioBuffer& buffer, int first, int count, int64_t position, int64_t total, float gain, bool fadeEdges) {
        std::array<float, block * channels> samples {};
        for (int i = 0; i < count; ++i) {
            const auto frame = position + i;
            const auto fade = fadeEdges ? std::min({1.0, frame / (rate * 0.015), (total - 1 - frame) / (rate * 0.015)}) : 1.0;
            for (int c = 0; c < channels; ++c) {
                const float value = buffer.getSample(c, first + i) * gain * static_cast<float>(std::max(0.0, fade));
                if (!std::isfinite(value)) throw std::runtime_error("invalid PCM");
                samples[i * channels + c] = value;
            }
        }
        file.write(reinterpret_cast<const char*>(samples.data()), count * channels * sizeof(float));
        bytes += count * channels * sizeof(float);
    }
    void close() { header(); file.close(); }
};

struct Variant {
    echo::EqProcessor eq;
    echo::ConvolutionProcessor room;
    echo::ChannelBalanceProcessor balance;
    echo::DspHeadroomProcessor headroom;
    echo::ReplayGainProcessor replayGain;
    echo::CompressorProcessor compressor;
    echo::SpatialDspProcessor spatial;
    echo::PlaybackRateProcessor speed;
    echo::LevelMeterProcessor meter;
    echo::DspChain chain { eq, room, balance, headroom, replayGain, compressor, spatial, speed, meter };
    echo::FloatAudioBuffer pcm { channels, block };
    void configure(const nlohmann::json& state) {
        auto preset = state;
        preset["type"] = "eq:set-preset";
        const auto response = nlohmann::json::parse(echo::EqMessageProtocol::handleJsonLine(preset.dump(), eq, balance, room, headroom));
        if (response.value("type", "") == "eq:error") throw std::runtime_error("invalid EQ");
        eq.setEnabled(state.value("enabled", false));
        headroom.setHeadroomDb(state.value("dspHeadroomDb", 0.0f));
        chain.prepare(rate, block, channels);
    }
};
}

int renderBlindEq(const std::string& configurationPath) {
    try {
        std::ifstream configFile(std::filesystem::u8path(configurationPath));
        if (std::filesystem::file_size(std::filesystem::u8path(configurationPath)) > 65536) throw std::runtime_error("config too large");
        const auto config = nlohmann::json::parse(configFile);
        const auto sourcePath = config.at("input").get<std::string>();
        if (!std::filesystem::is_regular_file(std::filesystem::u8path(sourcePath))) throw std::runtime_error("local file required");
        const double start = config.at("startSeconds").get<double>();
        const double length = config.at("lengthSeconds").get<double>();
        if (!std::isfinite(start) || start < 0 || !std::isfinite(length) || length < 5 || length > 30) throw std::runtime_error("invalid range");
        const bool finish = config.value("finish", false);
        const auto states = config.at("states");
        if (states.size() != (finish ? 1u : 2u)) throw std::runtime_error("invalid variants");
        const auto& common = states.at(0);
        echo::DspChain::setSafetyLimiterEnabled(common.value("dspSafetyLimiterEnabled", true));
        const auto ceiling = common.value("dspLimiterCeilingDb", nlohmann::json());
        echo::DspChain::setLimiterCeilingDb(ceiling.is_number() ? ceiling.get<float>() : -1.0f);
        echo::DspChain::setLimiterReleaseMs(common.value("dspLimiterReleaseMs", 80.0f));
        std::array<Variant, 2> variants;
        std::array<std::unique_ptr<WaveWriter>, 2> writers;
        for (size_t v = 0; v < states.size(); ++v) {
            if (!finish) variants[v].configure(states[v]);
            writers[v] = std::make_unique<WaveWriter>(config.at("outputs").at(v).get<std::string>());
        }
        echo::LibavPcmStreamDecoder decoder;
        decoder.open(sourcePath, rate, channels);
        if (!finish && (decoder.probe().channels < 1 || decoder.probe().channels > 2)) throw std::runtime_error("unsupported channels");
        const double preStart = finish ? start : std::max(0.0, start - 1.0);
        decoder.seek(preStart);
        const int64_t skip = std::llround((start - preStart) * rate);
        const int64_t total = std::llround(length * rate);
        int64_t decoded = 0, written = 0;
        while (written < total) {
            const auto chunk = decoder.readFrames(static_cast<int>(std::min<int64_t>(block, skip + total - decoded)));
            if (chunk.frames <= 0) break;
            const int first = static_cast<int>(std::min<int64_t>(chunk.frames, std::max<int64_t>(0, skip - decoded)));
            const int count = chunk.frames - first;
            for (size_t v = 0; v < states.size(); ++v) {
                for (int i = 0; i < chunk.frames; ++i)
                    for (int c = 0; c < channels; ++c) variants[v].pcm.setSample(c, i, chunk.samples[i * channels + c]);
                if (!finish) variants[v].chain.processBlock(variants[v].pcm, 0, chunk.frames, false);
                const float gainDb = config.value("gainDb", 0.0f);
                if (!std::isfinite(gainDb) || gainDb > 0 || gainDb < -60) throw std::runtime_error("invalid gain");
                // Only the first pass fades. The finishing pass applies one constant attenuation.
                if (count > 0) writers[v]->write(variants[v].pcm, first, count, written,
                    total, std::pow(10.0f, gainDb / 20.0f), !finish);
            }
            decoded += chunk.frames;
            written += count;
        }
        if (written != total) throw std::runtime_error("short source");
        for (auto& writer : writers) if (writer) writer->close();
        std::cout << "{\"preparedEqVersion\":1,\"frames\":" << written << "}" << std::endl;
        return 0;
    } catch (...) {
        // Do not expose local paths, tags or decoder messages through diagnostics.
        std::cerr << "blind_eq_prepare_failed" << std::endl;
        return 1;
    }
}
