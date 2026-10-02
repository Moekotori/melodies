#pragma once

#include <atomic>
#include <cerrno>
#include <chrono>
#include <cstdint>

// Writes one already-rendered block. Operations use a nonblocking PCM handle;
// waits are bounded, and a short write never discards the remaining samples.
// Kept independent of ALSA so failure/recovery sequences can be tested without
// an audio device. No allocation or extra audio buffer is needed here.
template <class Operations>
int writeLinuxAlsaPcmBlock(Operations& operations, const std::uint8_t* data,
    long frames, int frameBytes, const std::atomic<bool>& running)
{
    using Clock = std::chrono::steady_clock;
    auto deadline = Clock::now() + std::chrono::seconds(2);
    long offset = 0;
    while (running.load(std::memory_order_acquire))
    {
        if (Clock::now() >= deadline) return -ETIMEDOUT;
        const long written = operations.write(data + offset * frameBytes, frames - offset);
        if (written > 0)
        {
            if (written > frames - offset) return -EIO;
            offset += written;
            if (offset == frames) return 0;
            deadline = Clock::now() + std::chrono::seconds(2);
            continue;
        }
        if (written == -EINTR) continue;
        if (written == 0 || written == -EAGAIN)
        {
            const int ready = operations.wait(20);
            if (ready >= 0 || ready == -EINTR) continue;
            if (ready != -EPIPE && ready != -ESTRPIPE) return ready;
            // Retry write to obtain the PCM state and use the recovery below.
            continue;
        }
        if (written == -EPIPE || written == -ESTRPIPE)
        {
            int recovered = written == -EPIPE ? operations.prepare() : operations.resume();
            while (recovered == -EAGAIN && running.load(std::memory_order_acquire))
            {
                if (Clock::now() >= deadline) return -ETIMEDOUT;
                operations.pause(20);
                recovered = operations.resume();
            }
            if (!running.load(std::memory_order_acquire)) return -ECANCELED;
            if (recovered < 0 && written == -ESTRPIPE) recovered = operations.prepare();
            if (recovered < 0) return recovered;
            continue;
        }
        return static_cast<int>(written);
    }
    return -ECANCELED;
}
