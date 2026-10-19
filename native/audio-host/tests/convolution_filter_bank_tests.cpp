#include "ConvolutionProcessor.h"
#include "DspSafetyLimiter.h"
#include <filesystem>
#include <bit>
#include <fstream>
#include <iostream>
#include <numeric>
#include <stdexcept>

namespace {
void require(bool value, const char* reason) { if (!value) throw std::runtime_error(reason); }
void wav(const std::filesystem::path& path, int rate, int channels, int frames = 128, bool includeTail = false)
{
    std::ofstream out(path, std::ios::binary);
    const auto u16 = [&](uint16_t v) { out.put(static_cast<char>(v)); out.put(static_cast<char>(v >> 8)); };
    const auto u32 = [&](uint32_t v) { u16(static_cast<uint16_t>(v)); u16(static_cast<uint16_t>(v >> 16)); };
    const int bytes = frames * channels * 2;
    out.write("RIFF", 4); u32(36 + bytes); out.write("WAVEfmt ", 8); u32(16);
    u16(1); u16(channels); u32(rate); u32(rate * channels * 2); u16(channels * 2); u16(16);
    out.write("data", 4); u32(bytes);
    for (int sample = 0; sample < frames; ++sample)
        for (int channel = 0; channel < channels; ++channel)
            u16(sample == 0 || (includeTail && sample == frames - 1) ? static_cast<uint16_t>(4096 * (channel + 1)) : 0);
}
nlohmann::json entry(const char* name, int rate, int channels, int frames = 128)
{
    return {{"path", name}, {"name", name}, {"sampleRate", rate}, {"channels", channels}, {"frames", frames}};
}
void manifest(const std::filesystem::path& path, const nlohmann::json& entries)
{
    std::ofstream(path) << nlohmann::json{{"version", 1}, {"filters", entries}};
}
void doubleWav(const std::filesystem::path& path)
{
    std::ofstream out(path, std::ios::binary);
    const auto u16 = [&](uint16_t v) { out.put(static_cast<char>(v)); out.put(static_cast<char>(v >> 8)); };
    const auto u32 = [&](uint32_t v) { u16(static_cast<uint16_t>(v)); u16(static_cast<uint16_t>(v >> 16)); };
    out.write("RIFF", 4); u32(36 + 512 * 2 * 8); out.write("WAVEfmt ", 8); u32(16);
    u16(3); u16(2); u32(48000); u32(48000 * 2 * 8); u16(16); u16(64);
    out.write("data", 4); u32(512 * 2 * 8);
    for (int sample = 0; sample < 512; ++sample) for (int channel = 0; channel < 2; ++channel) {
        const double value = sample == 0 || sample == 257 ? channel == 0 ? 1.0 + 1e-12 : -1.0 : 0;
        const auto bits = std::bit_cast<uint64_t>(value); u32(static_cast<uint32_t>(bits)); u32(static_cast<uint32_t>(bits >> 32));
    }
}
void verifyOutput(echo::ConvolutionProcessor& processor, int channels, bool mono)
{
    processor.setEnabled(true);
    echo::FloatAudioBuffer buffer(channels, 256);
    for (int channel = 0; channel < channels; ++channel) buffer.getWritePointer(channel)[0] = 0.25f;
    processor.processBlock(buffer, 0, 256);
    for (int channel = 0; channel < channels; ++channel)
        require(std::abs(buffer.getReadPointer(channel)[0] - 0.03125f * (mono ? 1 : channel + 1)) < 1.0e-5f,
            "real decoded IR must reach the corresponding output channel");
}
}

int main(int argc, char** argv)
{
    try
    {
        require(argc == 2, "an owned fixture directory is required");
        const auto root = std::filesystem::absolute(argv[1]);
        std::filesystem::create_directories(root);
        wav(root / "stereo44.wav", 44100, 2); wav(root / "stereo48.wav", 48000, 2);
        wav(root / "surround48.wav", 48000, 6); wav(root / "mono48.wav", 48000, 1);
        manifest(root / "bank.json", nlohmann::json::array({entry("stereo44.wav", 44100, 2),
            entry("stereo48.wav", 48000, 2), entry("surround48.wav", 48000, 6), entry("mono48.wav", 48000, 1)}));
        echo::ConvolutionProcessor processor;
        processor.prepare(44100, 256, 2);
        require(processor.loadImpulseResponse((root / "bank.json").string(), "bank", "Test bank"), "bank import");
        require(processor.getState().selectedFilter.at("name") == "stereo44.wav", "exact 44.1 kHz selection");
        require(processor.getState().latencySamples == 0, "native-rate IR has no added resampling delay");
        require(processor.getState().filterBank.at("filters").at(0).at("frames") == 128, "source filter descriptors exposed");
        processor.prepare(48000, 256, 2);
        require(processor.getState().selectedFilter.at("name") == "stereo48.wav", "rate switch selects another asset");
        verifyOutput(processor, 2, false);
        processor.prepare(48000, 256, 6);
        require(processor.getState().channelMode == "multichannel", "six-channel IR must remain multichannel");
        require(processor.getState().selectedFilter.at("name") == "surround48.wav", "exact channel layout preferred over mono");
        verifyOutput(processor, 6, false);
        processor.prepare(96000, 256, 6);
        require(processor.getState().selectedFilter.at("resampled") == true && processor.getState().tapCount == 512
            && processor.getState().latencySamples == 128,
            "fallback IR resampling uses the processing rate");
        processor.prepare(48000, 256, 8);
        require(processor.getState().selectedFilter.at("sourceChannels") == 1, "mono fallback");
        verifyOutput(processor, 8, true);
        processor.prepare(48000, 256, 16);
        require(processor.getState().selectedFilter.at("processingChannels") == 16, "mono graph follows layout-only growth");
        verifyOutput(processor, 16, true);
        processor.prepare(48000, 256, 2);
        require(processor.getState().selectedFilter.at("processingChannels") == 2, "layout-only changes reprepare a mono graph");

        manifest(root / "surround.json", nlohmann::json::array({entry("surround48.wav", 48000, 6)}));
        processor.setEnabled(false);
        require(processor.loadImpulseResponse((root / "surround.json").string(), "six", "Six channels"), "disabled unmatched bank can be imported");
        require(processor.getState().selectedFilter.is_null(), "no fake selection before a compatible route");
        processor.setEnabled(true);
        require(processor.getState().error == "convolution_no_matching_channels", "unmatched layout is reported");
        processor.prepare(48000, 256, 6);
        require(processor.getState().error.empty(), "compatible route recovers");

        const auto currentId = processor.getState().irId;
        manifest(root / "invalid.json", nlohmann::json::array({entry("../outside.wav", 48000, 1)}));
        require(!processor.loadImpulseResponse((root / "invalid.json").string(), "invalid", "Invalid"), "path traversal rejected");
        require(processor.getState().irId == currentId, "failed import preserves the old graph");
        require(processor.getState().status == "error", "an old graph does not hide the failed command");

        std::vector<std::vector<float>> gainImpulse(2, std::vector<float>(128, 0));
        gainImpulse[0][0] = 0.5f; gainImpulse[1][0] = -0.25f;
        for (const double target : { 24000.0, 96000.0, 44100.0 }) {
            const auto result = echo::resampleImpulseResponse(gainImpulse, 48000, target, 2);
            require(!result.taps.empty() && result.latencySamples > 0, "band-limited IR resampling succeeds");
            require(std::abs(std::accumulate(result.taps[0].begin(), result.taps[0].end(), 0.0) - 0.5) < 0.0002,
                "resampling preserves FIR DC gain at the file boundary");
            require(std::abs(std::accumulate(result.taps[1].begin(), result.taps[1].end(), 0.0) + 0.25) < 0.0002,
                "resampling preserves channel separation and sign");
        }
        require(echo::resampleImpulseResponse(gainImpulse, 48000, 48000, 2).taps == gainImpulse,
            "native-rate coefficients stay exactly unchanged");
        const auto tone = [](double frequency) {
            std::vector<std::vector<float>> signal(1, std::vector<float>(4096));
            for (size_t sample = 0; sample < signal[0].size(); ++sample) {
                const double window = 0.5 - 0.5 * std::cos(2 * 3.141592653589793 * sample / 4095);
                signal[0][sample] = static_cast<float>(window * std::sin(2 * 3.141592653589793 * frequency * sample / 96000));
            }
            return echo::resampleImpulseResponse(signal, 96000, 48000, 1);
        };
        const auto passband = tone(1000), stopband = tone(40000);
        require(!passband.taps.empty() && !stopband.taps.empty(), "frequency probes resample successfully");
        const auto energy = [](const auto& samples) { double sum = 0; for (const float sample : samples) sum += sample * sample; return sum; };
        require(energy(stopband.taps[0]) < energy(passband.taps[0]) * 1.0e-6, "downsampling rejects frequencies above output Nyquist");

        wav(root / "too-long96.wav", 96000, 2, 65536); wav(root / "short384.wav", 384000, 2);
        manifest(root / "budget.json", nlohmann::json::array({entry("too-long96.wav", 96000, 2, 65536), entry("short384.wav", 384000, 2)}));
        processor.setEnabled(false); processor.prepare(192000, 256, 2);
        require(processor.loadImpulseResponse((root / "budget.json").string(), "budget", "Budget-aware"), "budget-aware bank import");
        require(processor.getState().selectedFilter.at("name") == "short384.wav", "skip an over-budget IR before choosing the nearest usable filter");
        manifest(root / "over-budget.json", nlohmann::json::array({entry("too-long96.wav", 96000, 2, 65536)}));
        require(processor.loadImpulseResponse((root / "over-budget.json").string(), "over-budget", "Over budget"), "disabled over-budget bank can be retained");
        processor.setEnabled(true);
        require(processor.getState().error == "convolution_filter_budget_exceeded", "distinguish budget failure from incompatible channels");
        // Existing direct IR paths may contain more source taps at a higher rate.
        // Their source coefficient cap is independent of the prepared graph tap cap.
        processor.setEnabled(false);
        processor.clearImpulseResponse();
        processor.prepare(48000, 256, 2);
        wav(root / "legacy-long96.wav", 96000, 1, 100000);
        require(processor.loadImpulseResponse((root / "legacy-long96.wav").string(), "legacy", "Legacy high-rate IR"),
            "retain a legacy high-rate source IR when its resampled graph fits");
        require(processor.getState().tapCount == 50064 && processor.getState().latencySamples == 32,
            "legacy source frame budget does not replace the output graph budget");
        // REW's default filter length must survive import AND reach the last output sample.
        processor.setEnabled(false); processor.clearImpulseResponse();
        wav(root / "rew128k.wav", 48000, 2, 131072, true);
        manifest(root / "rew.json", nlohmann::json::array({entry("rew128k.wav", 48000, 2, 131072)}));
        require(processor.loadImpulseResponse((root / "rew.json").string(), "rew", "REW stereo"), "REW-length bank import");
        require(processor.getState().tapCount == 131072, "REW filter is not truncated");
        verifyOutput(processor, 2, false);
        echo::FloatAudioBuffer tail(2, 256);
        for (int offset = 256; offset < 131072; offset += 256) {
            tail.clear();
            processor.processBlock(tail, 0, 256);
        }
        for (int channel = 0; channel < 2; ++channel)
            require(std::abs(tail.getSample(channel, 255) - 0.03125f * (channel + 1)) < 1.0e-5f,
                "last REW coefficient reaches its output channel");
        require(processor.isTailDrained(), "long filter tail drains after its final sample");
        require(!echo::planImpulseResampling(131072, 48000, 48000, 3).valid,
            "mono broadcast cannot exceed the aggregate processing budget");
        require(!echo::planImpulseResampling(131073, 48000, 48000, 1).valid,
            "per-channel tap limit remains enforced");
        processor.setEnabled(false); processor.clearImpulseResponse();
        doubleWav(root / "double-routing.wav");
        auto routingEntry = entry("double-routing.wav", 48000, 2, 512);
        routingEntry["coefficientChannels"] = 2;
        routingEntry["routes"] = nlohmann::json::array({ {{"input", 0}, {"output", 1}}, {{"input", 0}, {"output", 1}} });
        manifest(root / "double-routing.json", nlohmann::json::array({routingEntry}));
        require(processor.loadImpulseResponse((root / "double-routing.json").string(), "matrix", "Double routing"), "routed double WAV import");
        require(processor.getState().selectedFilter.at("processingPrecisionBits") == 64, "precision evidence comes from the host");
        processor.setEnabled(true);
        echo::FloatAudioBuffer routed(2, 512); routed.setSample(0, 0, 1.0f);
        processor.processBlock(routed, 0, 512);
        require(std::abs(routed.getSample(1, 0) - 1e-12) < 1e-15, "double coefficient cancellation survives direct convolution");
        require(std::abs(routed.getSample(1, 257) - 1e-12) < 1e-15, "double coefficient cancellation survives FFT convolution");
        require(routed.getSample(0, 0) == 0.0f && routed.getSample(0, 257) == 0.0f, "unrouted output is silent; routes sum on the specified output");
        processor.prepare(96000, 256, 2);
        require(processor.getState().selectedFilter.at("routingPaths") == 2, "routing survives IR resampling");
        auto badRouting = routingEntry;
        badRouting["routes"][0]["output"] = 2;
        manifest(root / "bad-routing.json", nlohmann::json::array({badRouting}));
        require(!processor.loadImpulseResponse((root / "bad-routing.json").string(), "bad", "Bad"), "out-of-range routing is rejected");
        require(processor.getState().irId == "matrix", "invalid routing preserves the previous graph");
        std::vector<std::vector<double>> matrix(4, std::vector<double>(512, 0));
        const double gains[] = { 0.5, 0.25, -0.125, 0.75 };
        for (int route = 0; route < 4; ++route) matrix[route][0] = matrix[route][257] = gains[route];
        echo::PartitionedConvolutionEngine crossMatrix(matrix, 2, { {0, 0}, {1, 0}, {0, 1}, {1, 1} });
        echo::FloatAudioBuffer twoInputs(2, 512);
        twoInputs.setSample(0, 0, 0.25f); twoInputs.setSample(1, 0, 0.5f);
        crossMatrix.processBlock(twoInputs, 0, 512);
        for (int sample : { 0, 257 }) {
            require(std::abs(twoInputs.getSample(0, sample) - 0.25f) < 1e-6f, "direct and FFT routes use both input histories for the left output");
            require(std::abs(twoInputs.getSample(1, sample) - 0.34375f) < 1e-6f, "direct and FFT routes preserve weighted polarity for the right output");
        }
        for (const auto layout : { std::pair{2, 4}, std::pair{6, 2} }) {
            std::vector<std::vector<double>> impulse(1, std::vector<double>(512, 0));
            impulse[0][0] = impulse[0][257] = 1.0 + 1e-12;
            echo::PartitionedConvolutionEngine conversion(impulse, layout.first,
                {{layout.first - 1, layout.second - 1}}, layout.second);
            echo::DoubleAudioBuffer signal(std::max(layout.first, layout.second), 512);
            signal.setActiveChannels(layout.first);
            signal.setSample(layout.first - 1, 0, 0.25 + 1e-12);
            conversion.processBlock(signal, 0, 512);
            require(signal.getNumChannels() == layout.second, "converted active output view");
            for (int sample : {0, 257}) {
                require(std::abs(signal.getSample(layout.second - 1, sample) - (0.25 + 1e-12) * (1.0 + 1e-12)) < 1e-15,
                    "upmix/downmix preserves double precision across direct and FFT partitions");
                for (int channel = 0; channel < layout.second - 1; ++channel)
                    require(signal.getSample(channel, sample) == 0, "unrouted converted outputs remain silent");
            }
        }
        std::vector<std::vector<float>> excessive(6, std::vector<float>(65536, 0));
        echo::PartitionedConvolutionEngine tooLarge(excessive, 6);
        require(!tooLarge.isPrepared(), "total coefficients bounded");
        std::cout << "Convolution bank: rate matching, resampling, 6-channel decode/output, mono fallback, layout changes, failure preservation and bounds passed\n";
        return 0;
    }
    catch (const std::exception& error) { std::cerr << error.what() << '\n'; return 1; }
}
