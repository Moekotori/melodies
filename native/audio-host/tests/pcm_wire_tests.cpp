#include "PcmWireProtocol.h"
#include "RawPcmInputReader.h"
#include "DecodePcmCommand.h"
#include "Options.h"
#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <mutex>
#include <sstream>
#include <stdexcept>
#include <thread>
#include <vector>
#ifdef _WIN32
#include <fcntl.h>
#include <io.h>
#else
#include <unistd.h>
#endif

using namespace echo::pcm_wire;
namespace {
void require(bool ok, const char* message) { if (!ok) throw std::runtime_error(message); }
struct Fixture {
    std::filesystem::path directory, file;
    Fixture() {
        directory = std::filesystem::temp_directory_path() / ("echo-pcm-wire-" + std::to_string(
            std::chrono::steady_clock::now().time_since_epoch().count()));
        require(std::filesystem::create_directory(directory), "fixture directory exists");
        file = directory / "stereo.wav";
        std::ofstream output(file, std::ios::binary);
        const auto word = [&](uint32_t value, int size) {
            for (int i = 0; i < size; ++i) output.put(static_cast<char>((value >> (i * 8)) & 255));
        };
        constexpr int frames = 24000, dataBytes = frames * 4;
        output.write("RIFF", 4); word(36 + dataBytes, 4); output.write("WAVEfmt ", 8);
        word(16, 4); word(1, 2); word(2, 2); word(48000, 4);
        word(192000, 4); word(4, 2); word(16, 2);
        output.write("data", 4); word(dataBytes, 4);
        for (int frame = 0; frame < frames; ++frame)
            for (int channel = 0; channel < 2; ++channel)
                word(static_cast<uint16_t>((frame * 31 + channel * 97) % 40000 - 20000), 2);
        require(output.good(), "fixture write failed");
    }
    ~Fixture() { std::error_code error; std::filesystem::remove_all(directory, error); }
};
template<class F> void reject(F action) {
    bool rejected = false;
    try { action(); } catch (const std::runtime_error&) { rejected = true; }
    require(rejected, "malformed/stale PCM stream was accepted");
}
std::span<const std::byte> bytes(const std::string& text) {
    return {reinterpret_cast<const std::byte*>(text.data()), text.size()};
}
std::string decode(const char* path, uint64_t epoch) {
    Options options;
    options.decodeFile = path; options.decodePcm = true;
    options.sampleRate = 48000; options.pcmWireEpoch = epoch;
    std::ostringstream output(std::ios::binary);
    auto* original = std::cout.rdbuf(output.rdbuf());
    try { runLibavDecodePcm(options); }
    catch (...) { std::cout.rdbuf(original); throw; }
    std::cout.rdbuf(original);
    return output.str();
}
void checkReader(const std::string& wire, const std::string& raw) {
    std::string received;
    int began = 0, ended = 0;
    Reader reader({42, 48000, 2}, [&](const float* pcm, int frames) {
        require(began == 1 && !ended, "PCM escaped native session boundaries");
        received.append(reinterpret_cast<const char*>(pcm), frames * 2 * sizeof(float));
        return true;
    }, [&] { ++began; }, [&] { ++ended; });
    auto remaining = bytes(wire);
    // Fragment every header and channel frame, including zero-length End.
    size_t step = 1;
    while (!remaining.empty()) {
        const auto n = std::min(remaining.size(), step);
        reader.feed(remaining.first(n)); remaining = remaining.subspan(n);
        step = step % 1031 + 1;
    }
    reader.finish();
    require(began == 1 && ended == 1 && received == raw, "real decoder PCM changed in transit");
    reject([&] { reader.feed(bytes(wire)); });
}
void checkRejection(const std::string& wire) {
    auto bad = [&](std::string packet) {
        int calls = 0;
        Reader reader({42, 48000, 2}, [&](const float*, int) { ++calls; return true; }, [] {}, [] {});
        reject([&] { reader.feed(bytes(packet)); reader.finish(); });
        return calls;
    };
    for (size_t offset : {size_t(0), size_t(4), size_t(8), size_t(16), size_t(24),
        size_t(32), size_t(36), size_t(38), size_t(56)}) {
        auto wrong = wire; wrong[offset] ^= 1;
        require(bad(wrong) == 0, "invalid Begin reached output");
    }
    auto oversized = wire; // first Data payload length, before allocating/copying payload
    std::memset(oversized.data() + headerBytes + 44, 0xff, 4);
    require(bad(oversized) == 0, "oversized frame reached output");
    auto reordered = wire; reordered[headerBytes + 16] ^= 1;
    require(bad(reordered) == 0, "out-of-order frame reached output");
    auto nonfinite = wire;
    const unsigned char nan[] = {0, 0, 0xc0, 0x7f};
    std::memcpy(nonfinite.data() + 2 * headerBytes, nan, 4);
    require(bad(nonfinite) == 0, "nonfinite PCM reached output");
    for (size_t cut : {size_t(0), size_t(17), headerBytes + 13, wire.size() - 1, wire.size() - headerBytes})
        bad(wire.substr(0, cut));
    Reader stopped({42, 48000, 2}, [](const float*, int) { return false; }, [] {}, [] {});
    reject([&] { stopped.feed(bytes(wire)); });
    reject([&] { stopped.feed(bytes(wire)); }); // faults are terminal
    for (const char* epoch : {"0", "-1", "1junk", "18446744073709551616", ""})
        reject([&] { parseEpoch(epoch); });
    require(parseEpoch("18446744073709551615") == UINT64_MAX, "epoch was truncated");
}

#ifndef _WIN32
void checkPipeBackpressure(const std::string& wire, const std::string& raw) {
    int fds[2]; require(pipe(fds) == 0, "pipe failed");
    std::mutex gate;
    std::condition_variable signal;
    bool release = false, entered = false, done = false;
    std::string result, failure;
    RawPcmInputReader reader(fds[0], 2, [&](const float* pcm, int frames) {
        std::unique_lock lock(gate);
        entered = true; signal.notify_all();
        if (!signal.wait_for(lock, std::chrono::seconds(3), [&] { return release; })) return false;
        result.append(reinterpret_cast<const char*>(pcm), frames * 2 * sizeof(float));
        return true;
    }, [&](const std::string& message) {
        std::lock_guard lock(gate); failure = message; done = true; signal.notify_all();
    });
    reader.enableFraming({42, 48000, 2}, [] {}, [&] {
        std::lock_guard lock(gate); done = true; signal.notify_all();
    });
    reader.start();
    std::atomic<bool> senderDone = false;
    std::thread sender([&] {
        size_t sent = 0;
        while (sent < wire.size()) {
            const auto n = write(fds[1], wire.data() + sent, wire.size() - sent);
            if (n <= 0) break;
            sent += n;
        }
        close(fds[1]); senderDone.store(true);
    });
    bool ready;
    {
        std::unique_lock lock(gate);
        ready = signal.wait_for(lock, std::chrono::seconds(3), [&] { return entered || done; });
        // The native worker stays in push; it cannot deliver End while the sink
        // is blocked. No PCM queue is created by the framing layer.
        ready = ready && entered && !done;
        release = true; signal.notify_all();
        ready = signal.wait_for(lock, std::chrono::seconds(3), [&] { return done; }) && ready;
    }
    reader.stop(); close(fds[0]); sender.join();
    require(ready && senderDone && failure.empty() && result == raw,
        "native pipe backpressure/End failed");
}
#endif
}
int main() {
    try {
        Fixture fixture;
        const auto raw = decode(fixture.file.string().c_str(), 0), wire = decode(fixture.file.string().c_str(), 42);
        require(raw.size() > maximumPayloadBytes * 2, "fixture must span several bounded packets");
        checkReader(wire, raw); checkRejection(wire);
#ifndef _WIN32
        checkPipeBackpressure(wire, raw);
#endif
        std::cout << "PASS native decode, framed PCM, strict epochs/sequence/limits, truncation, pipe backpressure\n";
        return 0;
    } catch (const std::exception& error) {
        std::cerr << error.what() << '\n'; return 1;
    }
}
