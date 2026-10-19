#pragma once
#include "DspMeasurement.h"
#include "third_party/nlohmann_json.hpp"
namespace echo {
// Control/telemetry thread only.
inline nlohmann::json dspMeasurementJson(const DspMeasurement& value) {
    return {{"ready", value.ready}, {"revision", value.revision}, {"inputDb", value.inputDb},
        {"outputDb", value.outputDb}, {"gainDb", value.gainDb}, {"inputPeakDb", value.inputPeakDb},
        {"outputPeakDb", value.outputPeakDb}, {"windowMs", value.windowMs}, {"realtimeRatio", value.realtimeRatio},
        {"latencyMs", value.latencyMs >= 0 ? nlohmann::json(value.latencyMs) : nlohmann::json(nullptr)}, {"ageMs", value.ageMs}};
}
}
