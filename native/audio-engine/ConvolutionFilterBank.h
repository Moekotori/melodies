#pragma once

#include "third_party/nlohmann_json.hpp"
#include "ImpulseResponseResampler.h"
#include "ConvolutionLimits.h"
#include "ConvolutionRouting.h"
#include <algorithm>
#include <cctype>
#include <cmath>
#include <filesystem>
#include <fstream>
#include <stdexcept>
#include <string>
#include <vector>

namespace echo
{
struct ConvolutionFilterEntry
{
    std::string path;
    std::string name;
    int sampleRate = 0;
    int channels = 0;
    int frames = 0;
    int coefficientChannels = 0;
    int inputChannels = 0;
    std::vector<ConvolutionRoute> routes;
};

struct ConvolutionFilterBank
{
    std::vector<ConvolutionFilterEntry> filters;

    const ConvolutionFilterEntry* select(double rate, int channels, bool requireBudget = true) const
    {
        const ConvolutionFilterEntry* best = nullptr;
        double bestDistance = 0.0;
        if (!std::isfinite(rate) || rate <= 0 || channels < 1 || channels > convolutionMaxChannels) return nullptr;
        for (const auto& entry : filters)
        {
            if (entry.routes.empty() ? entry.channels != 1 && entry.channels != channels : entry.inputChannels != channels) continue;
            if (requireBudget && !planImpulseResampling(entry.frames, entry.sampleRate, rate,
                    std::max({channels, entry.channels, entry.coefficientChannels})).valid) continue;
            const double distance = std::abs(std::log(entry.sampleRate / rate));
            if (best == nullptr || (entry.inputChannels == channels && best->inputChannels != channels)
                || (entry.inputChannels == best->inputChannels && distance < bestDistance))
            {
                best = &entry;
                bestDistance = distance;
            }
        }
        return best;
    }

    nlohmann::json summary() const
    {
        std::vector<int> rates, channels;
        auto descriptors = nlohmann::json::array();
        for (const auto& entry : filters) {
            rates.push_back(entry.sampleRate); channels.push_back(entry.channels);
            auto descriptor = nlohmann::json{{"name", entry.name}, {"sampleRate", entry.sampleRate}, {"channels", entry.channels}, {"frames", entry.frames}};
            if (!entry.routes.empty()) { descriptor["routingPaths"] = entry.routes.size(); descriptor["inputChannels"] = entry.inputChannels; }
            descriptors.push_back(std::move(descriptor));
        }
        const auto unique = [](auto& values) {
            std::sort(values.begin(), values.end());
            values.erase(std::unique(values.begin(), values.end()), values.end());
        };
        unique(rates); unique(channels);
        return { {"filterCount", filters.size()}, {"sampleRates", rates}, {"channelCounts", channels}, {"filters", descriptors} };
    }

    static ConvolutionFilterBank read(const std::string& manifest)
    {
        const auto root = std::filesystem::canonical(std::filesystem::path(manifest).parent_path());
        if (std::filesystem::file_size(manifest) > 65536) throw std::runtime_error("convolution_manifest_size_limit");
        std::ifstream stream(manifest);
        const auto json = nlohmann::json::parse(stream);
        if (json.at("version") != 1 || !json.at("filters").is_array()
            || json.at("filters").empty() || json.at("filters").size() > 64) throw std::runtime_error("convolution_manifest_invalid");
        ConvolutionFilterBank bank;
        for (const auto& value : json.at("filters"))
        {
            const auto relative = std::filesystem::path(value.at("path").get<std::string>());
            if (relative.empty() || relative.is_absolute() || relative.has_root_name()) throw std::runtime_error("convolution_manifest_path_invalid");
            for (const auto& part : relative) if (part == ".." || part == ".") throw std::runtime_error("convolution_manifest_path_invalid");
            const auto path = std::filesystem::canonical(root / relative);
            auto extension = path.extension().string();
            std::transform(extension.begin(), extension.end(), extension.begin(), [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
            if (extension != ".wav" && extension != ".flac" && extension != ".aif" && extension != ".aiff" && extension != ".m4a") throw std::runtime_error("convolution_manifest_format_invalid");
            const auto within = path.lexically_relative(root);
            if (within.empty() || *within.begin() == ".." || !std::filesystem::is_regular_file(path)
                || std::filesystem::file_size(path) > 8 * 1024 * 1024) throw std::runtime_error("convolution_manifest_path_invalid");
            ConvolutionFilterEntry entry { path.string(), value.at("name").get<std::string>(),
                value.at("sampleRate").get<int>(), value.at("channels").get<int>(), value.at("frames").get<int>() };
            entry.inputChannels = value.value("inputChannels", entry.channels);
            entry.coefficientChannels = value.value("coefficientChannels", entry.channels);
            if (value.contains("routes")) {
                const auto& routes = value.at("routes");
                if (!routes.is_array() || routes.empty() || routes.size() > convolutionMaxChannels
                    || static_cast<int>(routes.size()) != entry.coefficientChannels) throw std::runtime_error("convolution_manifest_routing_invalid");
                for (const auto& route : routes) {
                    const int input = route.at("input").get<int>(), output = route.at("output").get<int>();
                    if (input < 0 || output < 0 || input >= entry.inputChannels || output >= entry.channels)
                        throw std::runtime_error("convolution_manifest_routing_invalid");
                    entry.routes.push_back({input, output});
                }
            } else if (entry.coefficientChannels != entry.channels) throw std::runtime_error("convolution_manifest_routing_invalid");
            if (entry.name.empty() || entry.name.size() > 960 || entry.sampleRate < 8000 || entry.sampleRate > 768000
                || entry.channels < 1 || entry.channels > convolutionMaxChannels || entry.frames < 1 || entry.frames > convolutionMaxTaps
                || entry.inputChannels < 1 || entry.inputChannels > convolutionMaxChannels
                || entry.coefficientChannels < 1 || entry.coefficientChannels > convolutionMaxChannels
                || static_cast<int64_t>(entry.frames) * std::max({entry.inputChannels, entry.channels, entry.coefficientChannels}) > convolutionMaxCoefficients) throw std::runtime_error("convolution_manifest_budget_invalid");
            if (std::any_of(bank.filters.begin(), bank.filters.end(), [&](const auto& old) {
                return old.sampleRate == entry.sampleRate && old.inputChannels == entry.inputChannels;
            })) throw std::runtime_error("convolution_ambiguous_filter_layout");
            bank.filters.push_back(std::move(entry));
        }
        return bank;
    }
};
} // namespace echo
