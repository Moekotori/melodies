#pragma once

#include <cmath>
#include <cstdint>
#include <string>
#include <memory>
#include <vector>
#include "../../audio-engine/third_party/nlohmann_json.hpp"

// Prepared on the RPC thread. The audio callback only reads interleaved f32.
struct WorkshopTransitionWindow
{
    static bool transport(const nlohmann::json& request, nlohmann::json& result,
        std::shared_ptr<WorkshopTransitionWindow>& staged, int rate, int channelCount)
    {
        const std::string operation = request.value("operation", "");
        const std::string id = request.value("windowId", "");
        if (id.empty() || id.size() > 80 || channelCount < 1 || channelCount > 2
            || rate < 8000 || rate > 48000) return false;
        if (operation == "window.begin")
        {
            const auto count = request.value("frames", int64_t(-1));
            if (count < 0 || count > int64_t(rate) * 16) return false;
            auto window = std::make_shared<WorkshopTransitionWindow>();
            window->id = id;
            window->frames = static_cast<uint64_t>(count);
            window->channels = channelCount;
            window->samples.resize(static_cast<size_t>(count * channelCount));
            staged = std::move(window);
        }
        else if (operation == "window.append")
        {
            if (!staged || staged->id != id || !request.contains("samples")
                || !staged->append(request.value("frameOffset", UINT64_MAX), request["samples"])) return false;
        }
        else if (operation == "window.cancel")
        {
            if (staged && staged->id == id) staged.reset();
        }
        else return false;
        result = {{"acknowledged", true}, {"windowId", id}};
        return true;
    }
    std::string id;
    int channels = 0;
    uint64_t frames = 0;
    uint64_t writtenFrames = 0;
    std::vector<float> samples;

    bool append(uint64_t offset, const nlohmann::json& input)
    {
        if (offset != writtenFrames || !input.is_array() || input.empty()
            || input.size() > static_cast<size_t>(4096 * channels)
            || input.size() % channels != 0
            || writtenFrames + input.size() / channels > frames)
            return false;
        for (const auto& sample : input)
            if (!sample.is_number() || !std::isfinite(sample.get<double>())
                || std::abs(sample.get<double>()) > 8.0)
                return false;
        const size_t start = static_cast<size_t>(writtenFrames * channels);
        for (size_t i = 0; i < input.size(); ++i)
            samples[start + i] = input[i].get<float>();
        writtenFrames += input.size() / channels;
        return true;
    }
};
