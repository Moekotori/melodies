#pragma once
#include "EqBand.h"
#include "buffer.h"
#include <array>
#include <atomic>
#include <memory>
#include <mutex>
#include "third_party/nlohmann_json.hpp"
#include <vector>

namespace echo {
constexpr int proceduralMaxOps = 16, proceduralMaxBands = 31, proceduralMaxChannels = 32, proceduralMaxRoutes = 64;
enum class ProceduralKind { Eq, Gain, Delay, Invert, Mute, Mix, Iir };
struct ProceduralBand { int type = 0; bool enabled = true; float hz = 1000, db = 0, q = 1; };
struct ProceduralRoute { int input = 0, output = 0; double gain = 1; };
struct ProceduralStep {
    ProceduralKind kind = ProceduralKind::Eq;
    bool enabled = true;
    uint32_t channels = 3;
    double gain = 1, delayMs = 0;
    int bandCount = 0, routeCount = 0;
    std::array<ProceduralBand, proceduralMaxBands> bands {};
    std::array<ProceduralRoute, proceduralMaxRoutes> routes {};
    BiquadCoefficients iir {};
};
struct ProceduralConfig {
    bool enabled = false;
    int count = 0;
    std::array<ProceduralStep, proceduralMaxOps> steps {};
};

// Control-plane JSON is never visited by the audio callback. Atomic words allow
// a bounded, race-free snapshot attempt; a busy writer defers adoption a block.
class ProceduralDspController {
public:
    ProceduralDspController();
    bool setState(const nlohmann::json&, std::string& error);
    nlohmann::json getState() const;
    uint64_t revision() const noexcept { return sequence_.load(std::memory_order_acquire); }
    bool tryRead(ProceduralConfig&, uint64_t& revision) const noexcept;
    bool enabled() const noexcept { return enabled_.load(); }
private:
    friend class DspPreparedHandoff;
    static constexpr size_t words = (sizeof(ProceduralConfig) + 3) / 4;
    static_assert(std::atomic<uint32_t>::is_always_lock_free);
    mutable std::mutex writer_;
    nlohmann::json state_;
    std::array<std::atomic<uint32_t>, words> data_ {};
    std::atomic<uint64_t> sequence_ { 0 };
    std::atomic<bool> enabled_ { false };
};

class ProceduralDspProcessor {
public:
    explicit ProceduralDspProcessor(ProceduralDspController&);
    ~ProceduralDspProcessor();
    void prepare(double sampleRate, int channels, double maximumChannelDelayMs = 4000);
    void reset() noexcept;
    template <typename Sample>
    void processBlock(PlanarAudioBuffer<Sample>&, int start, int count) noexcept;
    bool isActive() const noexcept;
    int64_t tailFramesRemaining() const noexcept;
private:
    struct Runtime;
    friend struct DspPreparedRuntime;
    ProceduralDspController& controller_;
    std::unique_ptr<Runtime> current_, previous_;
    ProceduralConfig pending_ {};
    uint64_t revision_ = UINT64_MAX;
    double sampleRate_ = 48000;
    int channels_ = 0, fade_ = 0, fadeLength_ = 240;
};
} // namespace echo
