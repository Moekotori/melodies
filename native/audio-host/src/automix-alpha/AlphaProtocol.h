#pragma once
#include "AlphaParameters.h"
#include "third_party/nlohmann_json.hpp"
#include <string>

namespace echo::automix_alpha {
inline bool parseParameters(const nlohmann::json& plan, Parameters& result) {
    if (!plan.contains("alphaDsp")) return true;
    const auto& value = plan["alphaDsp"];
    if (!value.is_object() || !value.contains("style") || !value["style"].is_string()) return false;
    const auto name = value["style"].get<std::string>();
    if (name == "plain_blend") result.style = Style::PlainBlend;
    else if (name == "bass_swap") result.style = Style::BassSwap;
    else if (name == "tail_ride") result.style = Style::TailRide;
    else if (name == "learned_eq") result.style = Style::LearnedEq;
    else if (name == "show_mode") result.style = Style::ShowMode;
    else if (name == "beat_cut") result.style = Style::BeatCut;
    else return false;
    if (value.contains("eqControls")) {
        const auto& controls = value["eqControls"];
        if (!controls.is_array() || controls.size() != result.eqControls.size()) return false;
        for (size_t i = 0; i < result.eqControls.size(); ++i) {
            if (!controls[i].is_number()) return false;
            result.eqControls[i] = controls[i].get<float>();
        }
    } else if (result.style == Style::LearnedEq) return false;
    result.enabled = true;
    // Alpha reconstructs the unshaped outgoing signal from the host envelope.
    // Reject combinations for which that reconstruction would be ambiguous.
    return result.valid() && plan.value("handoffProfile", "balanced") == "balanced"
        && !plan.value("echoThrow", false) && !plan.contains("handoffSchedule")
        && !plan.value("workshopSchedule", false)
        && !plan.contains("workshopWindowId");
}
}
