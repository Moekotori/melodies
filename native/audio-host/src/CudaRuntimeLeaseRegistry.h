#pragma once
#include <algorithm>
#include <atomic>
#include <cstdint>
#include <mutex>
#include <string>
#include <utility>
#include <vector>

// Runtime ownership policy shared by the real CUDA adapter and deterministic
// context-lifetime tests. It contains no DSP implementation or CUDA handles.
// One registry per runtime backend. API activation runs outside its mutex;
// an owner is retained before any call that can initialize the context.
template<class RuntimeApi>
class CudaRuntimeLeaseRegistry final {
public:
    class Lease final {
    public:
        Lease() = default;
        ~Lease() { reset(); }
        Lease(const Lease&) = delete;
        Lease& operator=(const Lease&) = delete;
        Lease(Lease&& other) noexcept : owner_(std::exchange(other.owner_, nullptr)), device_(other.device_) {}
        Lease& operator=(Lease&& other) noexcept {
            if (this != &other) { reset(); owner_ = std::exchange(other.owner_, nullptr); device_ = other.device_; }
            return *this;
        }
        explicit operator bool() const noexcept { return owner_ != nullptr; }
        bool activate(const char* prefix, std::string& error) const {
            if (!owner_) { error = std::string(prefix) + "runtime_lease_missing"; return false; }
            return owner_->activateOwned(device_, prefix, error);
        }
        void reset() noexcept {
            if (auto* owner = std::exchange(owner_, nullptr)) owner->releaseHold();
        }
    private:
        friend class CudaRuntimeLeaseRegistry;
        Lease(CudaRuntimeLeaseRegistry* owner, int device) : owner_(owner), device_(device) {}
        CudaRuntimeLeaseRegistry* owner_ = nullptr;
        int device_ = -1;
    };

    CudaRuntimeLeaseRegistry() : epoch_(nextEpoch_.fetch_add(1, std::memory_order_relaxed)) {}
    CudaRuntimeLeaseRegistry(const CudaRuntimeLeaseRegistry&) = delete;
    CudaRuntimeLeaseRegistry& operator=(const CudaRuntimeLeaseRegistry&) = delete;

    Lease acquire(int device, const char* prefix, std::string& error) {
        {
            std::lock_guard<std::mutex> lock(mutex_);
            if (std::find(devices_.begin(), devices_.end(), device) == devices_.end()) devices_.push_back(device);
            ++owners_;
        }
        Lease lease(this, device);
        if (!lease.activate(prefix, error)) return {};
        error.clear();
        return lease;
    }
    // Reconfiguration/probing holds keep an already initialized primary context
    // alive without selecting a device or initializing CUDA on the CPU path.
    void retainHold() {
        std::lock_guard<std::mutex> lock(mutex_); ++owners_;
    }
    void releaseHold() noexcept {
        std::lock_guard<std::mutex> lock(mutex_); --owners_; resetIfIdleLocked();
    }
    void resetIfIdle() {
        std::lock_guard<std::mutex> lock(mutex_); resetIfIdleLocked();
    }
    uint64_t epoch() const noexcept { return epoch_.load(std::memory_order_acquire); }

private:
    bool activateOwned(int device, const char* prefix, std::string& error) {
        // The lease excludes reset for this entire call. No global vector write
        // or registry mutex is needed on the producer's activation fast path.
        const auto epoch = epoch_.load(std::memory_order_acquire);
        if (cache_.registry == this && cache_.epoch == epoch && cache_.device == device) return true;
        if (!RuntimeApi::setDevice(device, prefix, error)) return false;
        cache_ = {this, epoch, device};
        return true;
    }
    void resetIfIdleLocked() noexcept {
        if (owners_ != 0 || devices_.empty()) return;
        epoch_.store(nextEpoch_.fetch_add(1, std::memory_order_relaxed), std::memory_order_release);
        for (int device : devices_) RuntimeApi::resetDevice(device);
        devices_.clear();
    }
    struct Cache { const CudaRuntimeLeaseRegistry* registry = nullptr; uint64_t epoch = 0; int device = -1; };
    inline static thread_local Cache cache_ {};
    inline static std::atomic<uint64_t> nextEpoch_ {1};
    std::atomic<uint64_t> epoch_;
    std::mutex mutex_;
    uint64_t owners_ = 0;
    std::vector<int> devices_;
};
