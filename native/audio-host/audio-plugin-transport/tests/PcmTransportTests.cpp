#include "PcmBlockExchange.h"
#include "SharedPcmMapping.h"
#include "SharedPcmAtomics.h"

#include <array>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <iostream>
#include <limits>
#include <stdexcept>
#include <string>
#include <thread>

#if defined(_WIN32)
#include <windows.h>
#else
#include <signal.h>
#include <sys/wait.h>
#include <unistd.h>
#endif

using namespace echo::audio_plugin;
using Clock = std::chrono::steady_clock;

namespace {
void require(bool condition, const char* message) {
    if (!condition) throw std::runtime_error(message);
}
template <class Condition> bool waitUntil(Condition condition) {
    const auto deadline = Clock::now() + std::chrono::seconds(3);
    do {
        if (condition()) return true;
        std::this_thread::sleep_for(std::chrono::milliseconds(1));
    } while (Clock::now() < deadline);
    return false;
}

void testCorrelationAndFiniteOutput() {
    PcmRegion region {};
    require(initializeRegion(region, 48000, 2), "initialize valid stereo format");
    HostBlockExchange host(region);
    AdapterBlockExchange adapter(region);
    const std::array<float, 4> dry { .2f, -.2f, .4f, -.4f };
    std::array<float, 4> output { 9, 9, 9, 9 };
    const auto ticket = host.submit(dry.data(), 2, 7, 1200, true);
    require(ticket.has_value(), "submit a partial block");
    require(host.receive(*ticket, output.data()) == ReceiveResult::Pending, "no result before completion");
    const auto lease = adapter.acquire();
    require(lease && lease->playing && lease->projectTimeSamples == 1200
        && lease->sampleRate == 48000 && lease->channels == 2, "native block context preserved");
    for (std::size_t i = 0; i < dry.size(); ++i) lease->wet[i] = lease->dry[i] * .5f;
    require(adapter.complete(*lease), "publish finished output");
    auto wrong = *ticket;
    ++wrong.generation;
    require(host.receive(wrong, output.data()) == ReceiveResult::Stale, "reject wrong session generation");
    wrong = *ticket;
    ++wrong.sequence;
    require(host.receive(wrong, output.data()) == ReceiveResult::Stale, "reject wrong block sequence");
    wrong = *ticket;
    ++wrong.frames;
    require(host.receive(wrong, output.data()) == ReceiveResult::Stale, "reject wrong actual frame count");
    require(output[0] == 9, "stale result leaves destination untouched");
    require(host.receive(*ticket, output.data()) == ReceiveResult::Ready, "exact matching output accepted");
    for (std::size_t i = 0; i < dry.size(); ++i) require(output[i] == dry[i] * .5f, "gain roundtrip");

    const auto invalid = host.submit(dry.data(), 2, 8, 0, false);
    const auto invalidLease = adapter.acquire();
    require(invalid && invalidLease, "next session can submit");
    for (std::size_t i = 0; i < dry.size(); ++i) invalidLease->wet[i] = 0;
    invalidLease->wet[3] = std::numeric_limits<float>::quiet_NaN();
    require(adapter.complete(*invalidLease), "worker releases malformed output");
    output.fill(9);
    require(host.receive(*invalid, output.data()) == ReceiveResult::InvalidSamples, "reject nonfinite wet samples");
    for (const auto value : output) require(value == 9, "invalid block does not partially overwrite fallback");
}

void testCancellationKeepsProcessingOwned() {
    PcmRegion region {};
    initializeRegion(region, 44100, 2);
    HostBlockExchange host(region);
    AdapterBlockExchange adapter(region);
    const std::array<float, 2> dry { .3f, -.3f };
    std::array<BlockTicket, slotCount> tickets {};
    std::array<AdapterBlockLease, slotCount> leases {};
    for (std::uint32_t i = 0; i < slotCount; ++i) {
        const auto ticket = host.submit(dry.data(), 1, 3, i, true);
        const auto lease = adapter.acquire();
        require(ticket && lease, "claim all slots");
        tickets[i] = *ticket;
        leases[i] = *lease;
        host.cancel(*ticket);
    }
    require(!host.submit(dry.data(), 1, 4, 0, true), "cancel must not steal a processing slot");
    require(!adapter.complete(leases[0]), "cancelled worker result is discarded");
    const auto fresh = host.submit(dry.data(), 1, 4, 0, true);
    require(fresh && fresh->slot == tickets[0].slot && fresh->sequence != tickets[0].sequence,
        "worker release permits safe reuse with a new identity");
    require(!adapter.complete(leases[0]), "duplicate completion cannot publish into reused slot");
    host.cancel(tickets[0]);
    const auto current = adapter.acquire();
    require(current && current->ticket.sequence == fresh->sequence, "old cancellation cannot cancel a reused slot");
    current->wet[0] = 0;
    current->wet[1] = 0;
    require(adapter.complete(*current), "new generation completes");
    std::array<float, 2> output { 9, 9 };
    require(host.receive(*fresh, output.data()) == ReceiveResult::Ready && output[0] == 0 && output[1] == 0,
        "legitimate all-zero wet output is accepted");
    for (std::uint32_t i = 1; i < slotCount; ++i) adapter.complete(leases[i]);
}

void testCapacityAndInvalidFormats() {
    PcmRegion region {};
    require(!initializeRegion(region, 48000, 9), "reject channel count outside bounded storage");
    require(!initializeRegion(region, 0, 2), "reject invalid sample rate");
    initializeRegion(region, 48000, maximumChannels);
    HostBlockExchange host(region);
    AdapterBlockExchange adapter(region);
    std::array<float, maximumSamples> dry {};
    require(!host.submit(dry.data(), maximumFrames + 1, 1, 0, true), "reject oversized frame count");
    require(!host.submit(dry.data(), 0, 1, 0, true), "reject empty frame count");
    require(!host.submit(dry.data(), 1, 0, 0, true), "reject uninitialized generation");
    std::array<BlockTicket, slotCount> tickets {};
    for (auto& ticket : tickets) {
        const auto submitted = host.submit(dry.data(), maximumFrames, 1, 0, true);
        require(submitted.has_value(), "fill bounded capacity");
        ticket = *submitted;
    }
    require(!host.submit(dry.data(), maximumFrames, 1, 0, true), "full transport returns immediately");
    for (const auto& ticket : tickets) host.cancel(ticket);
    require(!adapter.acquire(), "cancelled dry blocks never reach worker");
    require(host.submit(dry.data(), maximumFrames, 2, 0, true).has_value(), "cancelled queued slots reclaimed");
}

int runWorker(const std::string& name) {
    std::string error;
    auto mapping = SharedPcmMapping::open(name, error);
    if (!mapping) return 2;
    AdapterBlockExchange adapter(mapping->region());
    for (int block = 0; block < 32; ++block) {
        std::optional<AdapterBlockLease> lease;
        if (!waitUntil([&] { lease = adapter.acquire(); return lease.has_value(); })) return 3;
        if (lease->ticket.generation != 42 || lease->projectTimeSamples != block * 64
            || lease->ticket.frames != 64 || !lease->playing) return 4;
        for (std::uint32_t sample = 0; sample < lease->ticket.frames * lease->channels; ++sample)
            lease->wet[sample] = lease->dry[sample] * .5f;
        if (!adapter.complete(*lease)) return 5;
    }
    return 0;
}

class WorkerProcess final {
public:
    explicit WorkerProcess(const std::string& name) {
#if defined(_WIN32)
        std::array<wchar_t, 32768> executable {};
        const auto length = GetModuleFileNameW(nullptr, executable.data(), static_cast<DWORD>(executable.size()));
        require(length > 0 && length < executable.size(), "resolve worker executable");
        std::wstring command = L"\"" + std::wstring(executable.data(), length) + L"\" --worker "
            + std::wstring(name.begin(), name.end());
        STARTUPINFOW startup {};
        startup.cb = sizeof(startup);
        PROCESS_INFORMATION process {};
        require(CreateProcessW(nullptr, command.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW,
            nullptr, nullptr, &startup, &process) != FALSE, "spawn native worker");
        handle_ = process.hProcess;
        CloseHandle(process.hThread);
#else
        pid_ = fork();
        require(pid_ >= 0, "spawn native worker");
        if (pid_ == 0) _exit(runWorker(name));
#endif
    }
    ~WorkerProcess() {
#if defined(_WIN32)
        if (handle_) { TerminateProcess(handle_, 9); WaitForSingleObject(handle_, 1000); CloseHandle(handle_); }
#else
        if (pid_ > 0) { kill(pid_, SIGTERM); waitpid(pid_, nullptr, 0); }
#endif
    }
    bool waitForSuccess() {
#if defined(_WIN32)
        if (WaitForSingleObject(handle_, 3000) != WAIT_OBJECT_0) return false;
        DWORD code = 0;
        const bool success = GetExitCodeProcess(handle_, &code) != FALSE && code == 0;
        CloseHandle(handle_);
        handle_ = nullptr;
        return success;
#else
        int status = 0;
        if (!waitUntil([&] { return waitpid(pid_, &status, WNOHANG) == pid_; })) return false;
        pid_ = -1;
        return WIFEXITED(status) && WEXITSTATUS(status) == 0;
#endif
    }
private:
#if defined(_WIN32)
    HANDLE handle_ = nullptr;
#else
    pid_t pid_ = -1;
#endif
};

std::string mappingName(const char* suffix) {
#if defined(_WIN32)
    const auto pid = GetCurrentProcessId();
#else
    const auto pid = getpid();
#endif
    std::array<char, 9> pidText {};
    std::snprintf(pidText.data(), pidText.size(), "%08x", static_cast<unsigned int>(pid));
    return "echo-ap-test-" + std::string(pidText.data()) + "-" + suffix;
}

void testMappingsAndProcessRoundtrip() {
    std::string error;
    require(!SharedPcmMapping::create("../invalid", 48000, 2, error), "reject nonlocal name");
    const auto name = mappingName("gain");
    auto mapping = SharedPcmMapping::create(name, 48000, 2, error);
    require(mapping != nullptr, error.c_str());
    require(!SharedPcmMapping::create(name, 48000, 2, error), "existing mappings are never overwritten");
    HostBlockExchange host(mapping->region());
    WorkerProcess worker(name);
    std::array<float, 128> dry {}, wet {};
    for (int block = 0; block < 32; ++block) {
        dry.fill(.2f + block * .01f);
        const auto ticket = host.submit(dry.data(), 64, 42, block * 64, true);
        require(ticket.has_value(), "native host submits block");
        ReceiveResult received = ReceiveResult::Pending;
        require(waitUntil([&] {
            received = host.receive(*ticket, wet.data());
            return received != ReceiveResult::Pending;
        }), "bounded probe wait for worker result");
        require(received == ReceiveResult::Ready, "worker output accepted");
        for (std::size_t sample = 0; sample < wet.size(); ++sample)
            require(wet[sample] == dry[sample] * .5f, "cross-process samples retain exact identity");
    }
    require(worker.waitForSuccess(), "native worker exits successfully");
    mapping.reset();
    require(!SharedPcmMapping::open(name, error), "owner close releases mapping name");

    auto malformed = SharedPcmMapping::create(mappingName("badabi"), 48000, 2, error);
    require(malformed != nullptr, "create ABI rejection fixture");
    malformed->region().header.version = 999;
    require(!SharedPcmMapping::open(mappingName("badabi"), error), "reject incompatible mapped ABI");
}
}

int main(int argc, char** argv) {
    if (argc == 3 && std::string(argv[1]) == "--worker") return runWorker(argv[2]);
    try {
        testCorrelationAndFiniteOutput();
        testCancellationKeepsProcessingOwned();
        testCapacityAndInvalidFormats();
        testMappingsAndProcessRoundtrip();
        std::cout << "PASS native PCM identity, cancellation, capacity, finite output and two-process 32-block roundtrip\n";
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "FAIL " << error.what() << '\n';
        return 1;
    }
}
