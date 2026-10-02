#pragma once

#include "../../audio-engine/buffer.h"
#include "../../audio-engine/third_party/nlohmann_json.hpp"

#include <algorithm>
#include <atomic>
#include <cstddef>
#include <cstdint>
#include <string>
#include <string_view>
#include <thread>

namespace echo_audio_host {

using Json = nlohmann::json;
using FloatPlanarBuffer = echo::FloatAudioBuffer;

struct FifoBlock
{
    int start1 = 0;
    int size1 = 0;
    int start2 = 0;
    int size2 = 0;
};

class NativeFifo final
{
public:
    struct ReadLease
    {
        uint64_t epoch = 0;
        bool valid = false;
    };

    explicit NativeFifo(int capacityToUse)
        : capacity(std::max(0, capacityToUse))
    {
    }

    void prepareToWrite(int requested, int& start1, int& size1, int& start2, int& size2) const
    {
        const auto block = prepareToWrite(requested);
        start1 = block.start1;
        size1 = block.size1;
        start2 = block.start2;
        size2 = block.size2;
    }

    FifoBlock prepareToWrite(int requested) const
    {
        FifoBlock block;
        const uint64_t write = writeSequence.load(std::memory_order_relaxed);
        const uint64_t read = readSequence.load(std::memory_order_acquire);
        const int ready = static_cast<int>(std::min<uint64_t>(
            static_cast<uint64_t>(capacity),
            write >= read ? write - read : 0));
        const int writable = std::min(std::max(0, requested), capacity - ready);
        if (writable <= 0 || capacity <= 0)
            return block;

        block.start1 = static_cast<int>(write % static_cast<uint64_t>(capacity));
        block.size1 = std::min(writable, capacity - block.start1);
        block.start2 = 0;
        block.size2 = writable - block.size1;
        return block;
    }

    void finishedWrite(int written)
    {
        if (written > 0)
            writeSequence.fetch_add(static_cast<uint64_t>(written), std::memory_order_release);
    }

    void prepareToRead(int requested, int& start1, int& size1, int& start2, int& size2) const
    {
        const auto block = prepareToRead(requested);
        start1 = block.start1;
        size1 = block.size1;
        start2 = block.start2;
        size2 = block.size2;
    }

    FifoBlock prepareToRead(int requested) const
    {
        FifoBlock block;
        const uint64_t read = readSequence.load(std::memory_order_relaxed);
        const uint64_t write = writeSequence.load(std::memory_order_acquire);
        const int ready = static_cast<int>(std::min<uint64_t>(
            static_cast<uint64_t>(capacity),
            write >= read ? write - read : 0));
        const int readable = std::min(std::max(0, requested), ready);
        if (readable <= 0 || capacity <= 0)
            return block;

        block.start1 = static_cast<int>(read % static_cast<uint64_t>(capacity));
        block.size1 = std::min(readable, capacity - block.start1);
        block.start2 = 0;
        block.size2 = readable - block.size1;
        return block;
    }

    void finishedRead(int read)
    {
        if (read > 0)
            readSequence.fetch_add(static_cast<uint64_t>(read), std::memory_order_release);
    }

    bool tryAcquireReadLease(ReadLease& lease) const noexcept
    {
        lease = {};
        const uint64_t epoch = accessEpoch.load(std::memory_order_acquire);
        if ((epoch & 1u) != 0u)
            return false;

        activeReaders.fetch_add(1, std::memory_order_acq_rel);
        if (accessEpoch.load(std::memory_order_acquire) != epoch)
        {
            activeReaders.fetch_sub(1, std::memory_order_release);
            activeReaders.notify_all();
            return false;
        }

        lease.epoch = epoch;
        lease.valid = true;
        return true;
    }

    void releaseReadLease(ReadLease& lease, int framesRead = 0) noexcept
    {
        if (! lease.valid)
            return;

        // A reset publishes an odd epoch before waiting for readers. It cannot
        // touch either sequence until this lease is released, so finishedRead
        // is guaranteed to advance the exact FIFO snapshot that was prepared.
        if (framesRead > 0)
            finishedRead(framesRead);
        lease.valid = false;
        activeReaders.fetch_sub(1, std::memory_order_release);
        activeReaders.notify_all();
    }

    void resetWhenReadersQuiescent() noexcept
    {
        reconfigureCapacityWhenReadersQuiescent(capacity);
    }

    void reconfigureCapacityWhenReadersQuiescent(int newCapacity) noexcept
    {
        uint64_t epoch = accessEpoch.load(std::memory_order_acquire);
        for (;;)
        {
            if ((epoch & 1u) != 0u)
            {
                accessEpoch.wait(epoch, std::memory_order_acquire);
                epoch = accessEpoch.load(std::memory_order_acquire);
                continue;
            }
            if (accessEpoch.compare_exchange_weak(
                    epoch,
                    epoch + 1,
                    std::memory_order_acq_rel,
                    std::memory_order_acquire))
                break;
        }

        uint32_t readers = activeReaders.load(std::memory_order_acquire);
        while (readers != 0)
        {
            // Control/session threads may sleep here; the audio callback only
            // publishes an atomic notification when its bounded read ends.
            activeReaders.wait(readers, std::memory_order_acquire);
            readers = activeReaders.load(std::memory_order_acquire);
        }

        capacity = std::max(0, newCapacity);
        resetSequences();
        accessEpoch.store(epoch + 2, std::memory_order_release);
        accessEpoch.notify_all();
    }

    uint64_t currentEpoch() const noexcept
    {
        return accessEpoch.load(std::memory_order_acquire);
    }

    int getNumReady() const
    {
        const uint64_t read = readSequence.load(std::memory_order_acquire);
        const uint64_t write = writeSequence.load(std::memory_order_acquire);
        return static_cast<int>(std::min<uint64_t>(
            static_cast<uint64_t>(capacity),
            write >= read ? write - read : 0));
    }
    int getFreeSpace() const { return capacity - getNumReady(); }
    int getTotalSize() const { return capacity; }

private:
    void resetSequences() noexcept
    {
        readSequence.store(0, std::memory_order_release);
        writeSequence.store(0, std::memory_order_release);
    }

    int capacity = 0;
    std::atomic<uint64_t> readSequence { 0 };
    std::atomic<uint64_t> writeSequence { 0 };
    mutable std::atomic<uint32_t> activeReaders { 0 };
    std::atomic<uint64_t> accessEpoch { 0 };
};

class RenderEpoch final
{
public:
    class RenderLease final
    {
    public:
        RenderLease() = default;
        RenderLease(const RenderLease&) = delete;
        RenderLease& operator=(const RenderLease&) = delete;
        RenderLease(RenderLease&& other) noexcept
            : owner(other.owner), epoch(other.epoch)
        {
            other.owner = nullptr;
        }
        RenderLease& operator=(RenderLease&& other) noexcept
        {
            if (this != &other)
            {
                release();
                owner = other.owner;
                epoch = other.epoch;
                other.owner = nullptr;
            }
            return *this;
        }
        ~RenderLease() { release(); }

        explicit operator bool() const noexcept { return owner != nullptr; }
        uint64_t acquiredEpoch() const noexcept { return epoch; }

    private:
        friend class RenderEpoch;
        RenderLease(const RenderEpoch* ownerToUse, uint64_t epochToUse) noexcept
            : owner(ownerToUse), epoch(epochToUse)
        {
        }
        void release() noexcept
        {
            if (owner == nullptr)
                return;
            owner->release();
            owner = nullptr;
        }

        const RenderEpoch* owner = nullptr;
        uint64_t epoch = 0;
    };

    class QuiescenceGuard final
    {
    public:
        explicit QuiescenceGuard(RenderEpoch& ownerToUse) noexcept
            : owner(&ownerToUse), closedEpoch(ownerToUse.closeAndWait())
        {
        }
        QuiescenceGuard(const QuiescenceGuard&) = delete;
        QuiescenceGuard& operator=(const QuiescenceGuard&) = delete;
        ~QuiescenceGuard()
        {
            if (owner != nullptr)
                owner->reopen(closedEpoch);
        }

    private:
        RenderEpoch* owner = nullptr;
        uint64_t closedEpoch = 0;
    };

    RenderLease tryAcquire() const noexcept
    {
        const uint64_t epoch = accessEpoch.load(std::memory_order_acquire);
        if ((epoch & 1u) != 0u)
            return {};

        activeCallbacks.fetch_add(1, std::memory_order_acq_rel);
        if (accessEpoch.load(std::memory_order_acquire) != epoch)
        {
            activeCallbacks.fetch_sub(1, std::memory_order_release);
            activeCallbacks.notify_all();
            return {};
        }
        return RenderLease(this, epoch);
    }

private:
    void release() const noexcept
    {
        activeCallbacks.fetch_sub(1, std::memory_order_release);
        activeCallbacks.notify_all();
    }

    uint64_t closeAndWait() noexcept
    {
        uint64_t epoch = accessEpoch.load(std::memory_order_acquire);
        for (;;)
        {
            if ((epoch & 1u) != 0u)
            {
                accessEpoch.wait(epoch, std::memory_order_acquire);
                epoch = accessEpoch.load(std::memory_order_acquire);
                continue;
            }
            if (accessEpoch.compare_exchange_weak(
                    epoch,
                    epoch + 1,
                    std::memory_order_acq_rel,
                    std::memory_order_acquire))
                break;
        }

        uint32_t callbacks = activeCallbacks.load(std::memory_order_acquire);
        while (callbacks != 0)
        {
            activeCallbacks.wait(callbacks, std::memory_order_acquire);
            callbacks = activeCallbacks.load(std::memory_order_acquire);
        }
        return epoch + 1;
    }

    void reopen(uint64_t closedEpoch) noexcept
    {
        accessEpoch.store(closedEpoch + 1, std::memory_order_release);
        accessEpoch.notify_all();
    }

    mutable std::atomic<uint32_t> activeCallbacks { 0 };
    std::atomic<uint64_t> accessEpoch { 0 };
};

struct FloatInterleavedRenderTarget
{
    float* samples = nullptr;
    uint32_t frames = 0;
    uint32_t channels = 0;
};

struct U32InterleavedRenderTarget
{
    uint32_t* samples = nullptr;
    uint32_t frames = 0;
    uint32_t channels = 0;
};

struct U8InterleavedRenderTarget
{
    uint8_t* samples = nullptr;
    uint32_t frames = 0;
    uint32_t channels = 0;
};

template <typename Target>
class SourceRenderer
{
public:
    virtual ~SourceRenderer() = default;
    virtual uint32_t render(Target target) = 0;
};

inline Json parseJson(std::string_view text)
{
    return Json::parse(text.begin(), text.end());
}

inline std::string jsonStringValue(const Json& object, const char* key, std::string_view fallback = {})
{
    if (! object.is_object())
        return std::string(fallback);

    const auto iterator = object.find(key);
    return iterator != object.end() && iterator->is_string() ? iterator->get<std::string>() : std::string(fallback);
}

inline double jsonDoubleValue(const Json& object, const char* key, double fallback)
{
    if (! object.is_object())
        return fallback;

    const auto iterator = object.find(key);
    return iterator != object.end() && iterator->is_number() ? iterator->get<double>() : fallback;
}

}
