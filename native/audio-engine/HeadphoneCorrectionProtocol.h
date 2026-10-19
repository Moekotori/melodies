#pragma once

#include "HeadphoneCorrectionParameters.h"
#include "EqTypes.h"
#include "third_party/nlohmann_json.hpp"
#include <string>

namespace echo
{
inline bool validHeadphoneCorrectionProfile(const nlohmann::json& profile)
{
    if (!profile.is_object() || !profile.contains("bands") || !profile["bands"].is_array()
        || profile["bands"].size() != eqBandCount) return false;
    const auto number = [](const nlohmann::json& value, const char* key, double minimum, double maximum) {
        return value.contains(key) && value[key].is_number()
            && correctionNumberInRange(value[key].get<double>(), minimum, maximum);
    };
    if (!number(profile, "preampDb", correctionMinPreampDb, correctionMaxPreampDb)) return false;
    if ((profile.contains("strengthPercent") && profile["strengthPercent"] != 100)
        || (profile.contains("tiltDb") && profile["tiltDb"] != 0)) return false;
    for (const auto& band : profile["bands"])
    {
        if (!band.is_object()
            || !number(band, "frequencyHz", correctionMinFrequencyHz, correctionMaxFrequencyHz)
            || !number(band, "gainDb", correctionMinGainDb, correctionMaxGainDb)
            || !number(band, "q", correctionMinQ, correctionMaxQ)
            || !band.contains("filterType") || !band["filterType"].is_string()) return false;
        const std::string type = band["filterType"].get<std::string>();
        if (type != "peaking" && type != "lowShelf" && type != "highShelf"
            && type != "lowPass" && type != "highPass" && type != "notch") return false;
        if ((band.contains("enabled") && !band["enabled"].is_boolean())
            || (band.contains("scope") && band["scope"] != "stereo")
            || (band.contains("dynamicMode") && band["dynamicMode"] != "off")) return false;
    }
    return true;
}
} // namespace echo
