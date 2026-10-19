#pragma once

#include <cstdint>

#if defined(_WIN32)
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#else
static_assert(__atomic_always_lock_free(sizeof(std::uint32_t), nullptr),
    "Shared PCM requires native lock-free 32-bit atomics");
#endif

namespace echo::audio_plugin::shared_atomic {

// Only naturally aligned uint32_t words are atomic. On Windows Interlocked
// explicitly supports variables in mappings shared between processes.
inline std::uint32_t load(const std::uint32_t& word) noexcept {
#if defined(_WIN32)
    return static_cast<std::uint32_t>(InterlockedCompareExchange(
        reinterpret_cast<volatile LONG*>(const_cast<std::uint32_t*>(&word)), 0, 0));
#else
    return __atomic_load_n(&word, __ATOMIC_ACQUIRE);
#endif
}

inline void store(std::uint32_t& word, std::uint32_t value) noexcept {
#if defined(_WIN32)
    InterlockedExchange(reinterpret_cast<volatile LONG*>(&word), static_cast<LONG>(value));
#else
    __atomic_store_n(&word, value, __ATOMIC_RELEASE);
#endif
}

inline bool compareExchange(std::uint32_t& word, std::uint32_t expected, std::uint32_t desired) noexcept {
#if defined(_WIN32)
    return static_cast<std::uint32_t>(InterlockedCompareExchange(
        reinterpret_cast<volatile LONG*>(&word), static_cast<LONG>(desired), static_cast<LONG>(expected))) == expected;
#else
    return __atomic_compare_exchange_n(&word, &expected, desired, false, __ATOMIC_ACQ_REL, __ATOMIC_ACQUIRE);
#endif
}

} // namespace echo::audio_plugin::shared_atomic
