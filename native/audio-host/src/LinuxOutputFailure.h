#pragma once

#include <atomic>

// One fixed-size failure per output lifetime. Producers may run on audio
// threads; the host control loop is the single consumer. Reset after joining.
class LinuxOutputFailure final
{
public:
    void record(int positiveError) noexcept
    {
        int expected = 0;
        code.compare_exchange_strong(expected, positiveError, std::memory_order_release);
    }

    bool failed() const noexcept { return code.load(std::memory_order_acquire) != 0; }

    int take() noexcept
    {
        const int value = code.load(std::memory_order_acquire);
        if (reported || value == 0) return 0;
        reported = true;
        return value;
    }

    void reset() noexcept
    {
        code.store(0, std::memory_order_relaxed);
        reported = false;
    }

private:
    std::atomic<int> code { 0 };
    bool reported = false;
};
