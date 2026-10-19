#pragma once
#include <array>
#include <atomic>
#include <cmath>

namespace echo::automix_alpha {
enum class Style { PlainBlend, BassSwap, TailRide, LearnedEq, ShowMode, BeatCut };
struct Parameters {
    bool enabled = false;
    Style style = Style::PlainBlend;
    std::array<float, 12> eqControls { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 };
    bool valid() const noexcept {
        if (static_cast<int>(style) < 0 || static_cast<int>(style) > 5) return false;
        for (float value : eqControls) if (!std::isfinite(value) || value < 0 || value > 1) return false;
        return true;
    }
};

// Installed before the existing native plan is enabled. The render thread
// snapshots it once per deck lease; no strings, model or PCM cross this boundary.
struct AtomicParameters {
    std::atomic<bool> enabled { false };
    std::atomic<int> style { 0 };
    std::array<std::atomic<float>, 12> controls {};
    void store(const Parameters& parameters) noexcept {
        enabled.store(false, std::memory_order_release);
        style.store(static_cast<int>(parameters.style), std::memory_order_relaxed);
        for (size_t i = 0; i < controls.size(); ++i) controls[i].store(parameters.eqControls[i], std::memory_order_relaxed);
        enabled.store(parameters.enabled && parameters.valid(), std::memory_order_release);
    }
    Parameters snapshot() const noexcept {
        Parameters result;
        result.enabled = enabled.load(std::memory_order_acquire);
        result.style = static_cast<Style>(style.load(std::memory_order_relaxed));
        for (size_t i = 0; i < controls.size(); ++i) result.eqControls[i] = controls[i].load(std::memory_order_relaxed);
        return result;
    }
};
}
