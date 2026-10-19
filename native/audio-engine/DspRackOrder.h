#pragma once

#include "ProceduralDsp.h"
#include "DspFilterRack.h"
#include "EqProcessor.h"
#include <array>
#include <atomic>
#include <cstddef>
#include <string>
#include <vector>

namespace echo
{
enum class DspRackModuleId
{
    Equalizer = 0,
    Convolution,
    ReplayGain,
    Compressor,
    Crossfeed,
    StereoField,
    ChannelMatrix,
    ChannelBalance,
    Multiband,
    Deesser,
    Procedural,
};

class DspRackOrder final
{
public:
    static constexpr std::size_t moduleCount = 11;
    using Snapshot = std::array<DspRackModuleId, moduleCount>;

    DspRackOrder();
    ProceduralDspController procedural;
    DspFilterRack filters;
    // Fixed bounded profile, independently controlled from the personal EQ.
    // DspChain is the sole realtime owner, including PCM-before-SDM routes.
    EqProcessor headphoneCorrection { true };

    Snapshot snapshot() const noexcept;
    bool setOrder(const std::vector<DspRackModuleId>& order, std::string& error, bool publishFilters = true);
    void resetToDefault();

    static Snapshot defaultOrder() noexcept;
    static const char* moduleIdText(DspRackModuleId module) noexcept;
    static bool parseModuleId(const std::string& text, DspRackModuleId& module) noexcept;

private:
    void storeOrder(const Snapshot& order) noexcept;

    // JSON-RPC is the single writer. The audio thread reads a coherent fixed-size
    // snapshot through this seqlock without allocation, mutexes or shared_ptr traffic.
    std::atomic<unsigned int> sequence_ { 0 };
    std::array<std::atomic<int>, moduleCount> order_;
};
} // namespace echo
