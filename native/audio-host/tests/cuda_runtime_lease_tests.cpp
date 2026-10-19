#include "CudaRuntimeLeaseRegistry.h"
#include <atomic>
#include <array>
#include <chrono>
#include <future>
#include <iostream>
#include <stdexcept>
#include <thread>
#include <type_traits>

namespace {
void require(bool condition, const char* message) { if (!condition) throw std::runtime_error(message); }
// Context API model only: the production ownership policy above is under test.
// No FIR, modulator or other replacement DSP is implemented in this fixture.
struct ContextApi {
    inline static std::atomic<unsigned> selections {0}, resets {0};
    inline static std::atomic<bool> failNext {false};
    inline static std::promise<void>* entered = nullptr;
    inline static std::shared_future<void>* release = nullptr;
    static bool setDevice(int device, const char* prefix, std::string& error) {
        ++selections;
        if (failNext.exchange(false)) { error = std::string(prefix) + "injected_set_device_failure"; return false; }
        if (device == 1 && entered) { entered->set_value(); release->wait(); }
        return true;
    }
    static void resetDevice(int) noexcept { ++resets; }
    static void clear() { selections = 0; resets = 0; failNext = false; }
};
using Registry = CudaRuntimeLeaseRegistry<ContextApi>;
static_assert(!std::is_copy_constructible_v<Registry::Lease> && std::is_nothrow_move_constructible_v<Registry::Lease>);

void ownershipAndFailure()
{
    ContextApi::clear(); Registry registry; std::string error;
    registry.retainHold();
    auto lease = registry.acquire(0, "test_", error);
    require(bool(lease), "device acquisition must own runtime");
    auto moved = std::move(lease);
    require(!lease && moved.activate("test_", error), "move must transfer one owner");
    registry.resetIfIdle(); moved.reset();
    require(ContextApi::resets == 0, "reconfiguration hold must prevent idle reset");
    const auto epoch = registry.epoch(); registry.releaseHold();
    require(ContextApi::resets == 1 && registry.epoch() != epoch, "last owner must reset and invalidate caches");
    require(!moved.activate("test_", error) && error == "test_runtime_lease_missing", "released lease must reject activation");
    ContextApi::failNext = true;
    auto failed = registry.acquire(0, "test_", error);
    require(!failed && error == "test_injected_set_device_failure", "initialization failure must preserve the real error");
    const auto afterFailure = registry.epoch();
    auto recovered = registry.acquire(0, "test_", error); require(bool(recovered), "new acquisition must work after failure cleanup");
    ContextApi::failNext = true;
    auto activation = std::async(std::launch::async, [&] {
        std::string activationError;
        const bool ok = recovered.activate("test_", activationError);
        return std::pair{ok, activationError};
    });
    const auto resetCount = ContextApi::resets.load();
    const auto [ok, activationError] = activation.get();
    require(!ok && activationError == "test_injected_set_device_failure" && recovered
        && ContextApi::resets == resetCount, "activation failure must propagate without resetting another owner's resources");
    recovered.reset();
    require(registry.epoch() != afterFailure, "failed acquisition must not leak an owner");
    std::cout << "[cuda-runtime] PASS lease ownership, reconfiguration hold and failed initialization\n";
}
void crossThreadEpoch()
{
    ContextApi::clear(); Registry registry;
    std::promise<Registry::Lease> published;
    auto first = published.get_future();
    std::promise<void> continuePromise; auto continued = continuePromise.get_future();
    auto worker = std::async(std::launch::async, [&] {
        std::string error;
        auto lease = registry.acquire(0, "test_", error);
        require(bool(lease), "worker must acquire original device");
        published.set_value(std::move(lease)); continued.wait();
        auto next = registry.acquire(0, "test_", error);
        require(bool(next), "persistent worker must reacquire after another thread resets");
    });
    auto firstOwner = first.get(); const auto oldEpoch = registry.epoch(); firstOwner.reset();
    require(registry.epoch() != oldEpoch, "main thread release must invalidate worker's cached device");
    continuePromise.set_value(); worker.get();
    require(ContextApi::selections == 2 && ContextApi::resets == 2,
        "same worker/device after reset must call the API again and remain tracked");
    std::cout << "[cuda-runtime] PASS persistent worker cache invalidation across reset\n";
}
void initializationDoesNotBlockLiveActivation()
{
    ContextApi::clear(); Registry registry; std::string error;
    auto live = registry.acquire(0, "test_", error); require(bool(live), "live device must be acquired");
    std::promise<void> entered, releasePromise;
    auto enteredFuture = entered.get_future(); auto release = releasePromise.get_future().share();
    ContextApi::entered = &entered; ContextApi::release = &release;
    auto initializing = std::async(std::launch::async, [&] {
        std::string localError; return registry.acquire(1, "test_", localError);
    });
    enteredFuture.wait();
    auto activating = std::async(std::launch::async, [&] {
        std::string localError; return live.activate("test_", localError);
    });
    const bool unblocked = activating.wait_for(std::chrono::milliseconds(100)) == std::future_status::ready;
    auto resetting = std::async(std::launch::async, [&] { registry.resetIfIdle(); });
    const bool resetUnblocked = resetting.wait_for(std::chrono::milliseconds(100)) == std::future_status::ready;
    const bool retained = ContextApi::resets == 0;
    releasePromise.set_value();
    require(activating.get(), "live device activation must succeed");
    resetting.get();
    auto candidate = initializing.get();
    ContextApi::entered = nullptr; ContextApi::release = nullptr;
    require(unblocked && resetUnblocked && retained && bool(candidate), "slow initialization must not hold activation mutex or allow idle reset");
    candidate.reset(); require(ContextApi::resets == 0, "candidate release must retain live context");
    live.reset(); require(ContextApi::resets == 2, "last owner must release both selected devices");
    std::cout << "[cuda-runtime] PASS retained initialization outside live activation path\n";
}
void concurrentOwners()
{
    ContextApi::clear(); Registry registry;
    std::array<std::future<void>, 4> workers;
    for (int lane = 0; lane < 4; ++lane) workers[lane] = std::async(std::launch::async, [&, lane] {
        for (int i = 0; i < 64; ++i) {
            std::string error; auto lease = registry.acquire(lane % 2, "test_", error);
            require(bool(lease) && lease.activate("test_", error), "concurrent owner must activate");
        }
    });
    for (auto& worker : workers) worker.get();
    const auto epoch = registry.epoch();
    std::string error; auto last = registry.acquire(0, "test_", error); last.reset();
    require(registry.epoch() != epoch, "concurrent acquisition/release must leave no retained owner");
    std::cout << "[cuda-runtime] PASS four concurrent owners and final release\n";
}
}
int main()
{
    try { ownershipAndFailure(); crossThreadEpoch(); initializationDoesNotBlockLiveActivation(); concurrentOwners(); }
    catch (const std::exception& error) { std::cerr << "[cuda-runtime] FAIL " << error.what() << '\n'; return 1; }
    return 0;
}
