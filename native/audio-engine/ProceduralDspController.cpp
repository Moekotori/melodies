#include "ProceduralDsp.h"
#include <cstring>
#include <set>
#include <stdexcept>

namespace echo {
namespace {
using Json = nlohmann::json;
double number(const Json& j, const char* key, double low, double high) {
    const auto& v = j.at(key);
    if (!v.is_number()) throw std::runtime_error("procedural_invalid_number");
    const double n = v.get<double>();
    if (!std::isfinite(n) || n < low || n > high) throw std::runtime_error("procedural_number_out_of_range");
    return n;
}
int channel(const Json& j) {
    if (!j.is_number_integer()) throw std::runtime_error("procedural_invalid_channel");
    const int n = j.get<int>();
    if (n < 1 || n > 32) throw std::runtime_error("procedural_invalid_channel");
    return n - 1;
}
int indexOf(const std::string& text, std::initializer_list<const char*> values) {
    int i = 0;
    for (const auto* value : values) { if (text == value) return i; ++i; }
    throw std::runtime_error("procedural_unknown_type");
}
ProceduralConfig parse(const Json& root) {
    ProceduralConfig result;
    result.enabled = root.at("enabled").get<bool>();
    const auto& ops = root.at("operations");
    if (!ops.is_array() || ops.size() > proceduralMaxOps) throw std::runtime_error("procedural_too_many_operations");
    std::set<std::string> ids;
    double delayBudget = 0;
    for (const auto& op : ops) {
        auto& step = result.steps[result.count++];
        const auto id = op.at("id").get<std::string>();
        if (id.empty() || id.size() > 64 || !ids.insert(id).second || op.at("name").get<std::string>().size() > 320)
            throw std::runtime_error("procedural_invalid_identity");
        step.kind = static_cast<ProceduralKind>(indexOf(op.at("kind").get<std::string>(), {"eq", "gain", "delay", "invert", "mute", "mix", "iir"}));
        step.enabled = op.at("enabled").get<bool>();
        step.channels = 0;
        const auto& channels = op.at("channels");
        if (!channels.is_array() || channels.empty() || channels.size() > 32) throw std::runtime_error("procedural_invalid_channels");
        for (const auto& c : channels) {
            const uint32_t bit = uint32_t(1) << channel(c);
            if ((step.channels & bit) != 0) throw std::runtime_error("procedural_duplicate_channel");
            step.channels |= bit;
        }
        step.gain = std::pow(10.0, number(op, "gainDb", -24, 24) / 20.0);
        step.delayMs = number(op, "delayMs", 0, 2000);
        if (step.kind == ProceduralKind::Delay) delayBudget += step.delayMs * channels.size();
        const auto& bands = op.at("bands");
        if (!bands.is_array() || bands.size() > proceduralMaxBands) throw std::runtime_error("procedural_too_many_bands");
        for (const auto& b : bands) {
            auto& band = step.bands[step.bandCount++];
            band.enabled = b.at("enabled").get<bool>();
            band.type = indexOf(b.at("filterType").get<std::string>(), {"peaking", "lowshelf", "highshelf", "lowpass", "highpass", "notch", "bandpass"});
            band.hz = static_cast<float>(number(b, "frequencyHz", 10, 96000));
            band.db = static_cast<float>(number(b, "gainDb", -24, 24));
            band.q = static_cast<float>(number(b, "q", 0.1, 30));
        }
        const auto& routes = op.at("routes");
        if (!routes.is_array() || routes.size() > proceduralMaxRoutes) throw std::runtime_error("procedural_too_many_routes");
        for (const auto& r : routes) {
            auto& route = step.routes[step.routeCount++];
            route.input = channel(r.at("input")); route.output = channel(r.at("output"));
            route.gain = std::pow(10.0, number(r, "gainDb", -24, 24) / 20.0) * (r.at("invert").get<bool>() ? -1 : 1);
        }
        const auto& c = op.at("coefficients");
        step.iir = {number(c,"b0",-16,16),number(c,"b1",-16,16),number(c,"b2",-16,16),number(c,"a1",-2,2),number(c,"a2",-1,1)};
        if (std::abs(step.iir.a2) >= 1 || 1 + step.iir.a1 + step.iir.a2 <= 0.000001 || 1 - step.iir.a1 + step.iir.a2 <= 0.000001)
            throw std::runtime_error("procedural_unstable_iir");
    }
    if (delayBudget > 4000) throw std::runtime_error("procedural_delay_budget_exceeded");
    return result;
}
}
ProceduralDspController::ProceduralDspController() {
    std::string error;
    setState(Json{{"enabled",false},{"operations",Json::array()}}, error);
}
bool ProceduralDspController::setState(const Json& state, std::string& error) {
    try {
        const auto config = parse(state);
        std::array<uint32_t, words> packed {};
        std::memcpy(packed.data(), &config, sizeof(config));
        // Copy the JSON before publication, including all allocations that may fail.
        auto saved = state;
        const std::lock_guard<std::mutex> lock(writer_);
        state_.swap(saved);
        sequence_.fetch_add(1);
        for (size_t i = 0; i < words; ++i) data_[i].store(packed[i]);
        enabled_.store(config.enabled && config.count > 0);
        sequence_.fetch_add(1);
        error.clear(); return true;
    } catch (const std::exception&) {
        error = "procedural_invalid_configuration"; return false;
    }
}
nlohmann::json ProceduralDspController::getState() const {
    const std::lock_guard<std::mutex> lock(writer_); return state_;
}
bool ProceduralDspController::tryRead(ProceduralConfig& result, uint64_t& revision) const noexcept {
    const auto before = sequence_.load();
    if ((before & 1) || before == revision) return false;
    std::array<uint32_t, words> packed {};
    for (size_t i = 0; i < words; ++i) packed[i] = data_[i].load();
    if (sequence_.load() != before) return false;
    std::memcpy(&result, packed.data(), sizeof(result));
    revision = before; return true;
}
} // namespace echo
