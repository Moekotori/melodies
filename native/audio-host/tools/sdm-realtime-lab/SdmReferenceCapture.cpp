#include "RoutePlan.h"
#include "NativeFirProcessor.h"
#include "NativeSdmProcessor.h"
#include <algorithm>
#include <array>
#include <cmath>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <stdexcept>

namespace {
void word(std::ostream& file, uint64_t value, int bytes)
{
    for (int index = 0; index < bytes; ++index) file.put(static_cast<char>(value >> (index * 8)));
}
}
int main(int argc, char** argv)
{
    try
    {
        if (argc != 5) throw std::runtime_error("usage: echo-sdm-reference-capture <plan> <route-id> <canonical-mono-f32-wav> <new-dsf>");
        const auto routes = echo::sdmRealtimeLab::readRoutes(argv[1]);
        const auto selected = std::find_if(routes.begin(), routes.end(), [&](const auto& route) { return route.id == argv[2]; });
        if (selected == routes.end()) throw std::runtime_error("capture_route_missing");
        if (std::filesystem::exists(argv[4])) throw std::runtime_error("capture_refuses_existing_dsf");
        std::ifstream file(argv[3], std::ios::binary);
        std::array<uint8_t, 44> header {}; file.read(reinterpret_cast<char*>(header.data()), header.size());
        const auto u32 = [&](int offset) { uint32_t v = 0; for (int i = 0; i < 4; ++i) v |= static_cast<uint32_t>(header[offset + i]) << (i * 8); return v; };
        if (!file || std::string(reinterpret_cast<const char*>(header.data()), 4) != "RIFF"
            || std::string(reinterpret_cast<const char*>(header.data() + 36), 4) != "data"
            || header[20] != 3 || header[22] != 1 || header[34] != 32
            || u32(24) != static_cast<uint32_t>(selected->sourceRate) || u32(40) > 4 * u32(24) || u32(40) % 4 != 0)
            throw std::runtime_error("capture_requires_canonical_mono_float32_wav_up_to_one_second");
        NativeFirProcessor fir; NativeSdmProcessor sdm;
        fir.setDoubleProcessing(true); sdm.setDoubleProcessing(true);
        std::string error;
        const int factor = selected->carrierRate / selected->sourceRate;
        if (!fir.configure(1, selected->stages, false, 4096, error)
            || !sdm.configure(1, echo::SdmQualityProfile::ReferenceEc, selected->carrierRate, false, 4096 * factor,
                error, echo::SdmDecisionMode::Greedy, false, echo::SdmPreModulatorProfile::UltimateReference))
            throw std::runtime_error(error);
        std::vector<uint8_t> bytes;
        bytes.reserve(static_cast<size_t>(u32(40) / 4) * factor * 2 + static_cast<size_t>(fir.tailOutputFrames()) * 2 + 4096);
        const auto append = [&](const std::vector<double>& carrier) {
            if (!carrier.empty()) {
                const auto block = sdm.processNativeDsd(carrier.data(), static_cast<int>(carrier.size()));
                bytes.insert(bytes.end(), block.begin(), block.end());
            }
        };
        std::vector<float> input(4096);
        int remaining = static_cast<int>(u32(40) / 4);
        while (remaining > 0) {
            const int frames = std::min(4096, remaining);
            file.read(reinterpret_cast<char*>(input.data()), static_cast<std::streamsize>(frames * sizeof(float)));
            if (!file) throw std::runtime_error("capture_source_truncated");
            append(fir.processDouble(input.data(), frames)); remaining -= frames;
        }
        append(fir.flushDouble());
        const auto tail = sdm.flushNativeDsd(); bytes.insert(bytes.end(), tail.begin(), tail.end());
        const uint64_t padded = (bytes.size() + 4095) / 4096 * 4096;
        std::ofstream dsf(argv[4], std::ios::binary);
        dsf.write("DSD ", 4); word(dsf, 28, 8); word(dsf, 92 + padded, 8); word(dsf, 0, 8);
        dsf.write("fmt ", 4); word(dsf, 52, 8);
        for (uint32_t value : {1u, 0u, 1u, 1u, static_cast<uint32_t>(selected->carrierRate * 16), 1u}) word(dsf, value, 4);
        word(dsf, bytes.size() * 8, 8); word(dsf, 4096, 4); word(dsf, 0, 4);
        dsf.write("data", 4); word(dsf, 12 + padded, 8);
        dsf.write(reinterpret_cast<const char*>(bytes.data()), static_cast<std::streamsize>(bytes.size()));
        for (uint64_t index = bytes.size(); index < padded; ++index) dsf.put(static_cast<char>(0x69));
        if (!dsf) throw std::runtime_error("capture_write_failed");
        const auto counts = sdm.clampCounts();
        std::cout << std::setprecision(12) << "{\"route\":\"" << selected->id << "\",\"nativeRate\":" << selected->carrierRate * 16
            << ",\"channels\":1,\"headroomDb\":" << -20 * std::log10(sdm.profileHeadroomGain())
            << ",\"alignmentDelayBits\":" << fir.nominalLatencyOutputFrames() * 16 + sdm.preModulatorDelayBits()
            << ",\"recoveries\":" << sdm.stabilityRecoveryCount() << ",\"inputClamps\":" << counts.input
            << ",\"loopClamps\":" << counts.loopInput << ",\"errorClamps\":" << counts.quantizationError
            << ",\"sourceFrames\":" << u32(40) / 4 << ",\"validBitCount\":" << bytes.size() * 8 << "}\n";
        return 0;
    }
    catch (const std::exception& error) { std::cerr << error.what() << '\n'; return 1; }
}
