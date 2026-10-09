#include "daemon_seek_end_tests.h"
#include "../src/AudioDaemon.h"
#include <chrono>
#include <cmath>
#include <filesystem>
#include <fstream>
#include <stdexcept>

namespace {
void require(bool condition, const char* message) {
    if (!condition) throw std::runtime_error(message);
}

struct SeekFixture {
    std::filesystem::path path;
    SeekFixture() {
        const auto root = std::filesystem::path(__FILE__).parent_path().parent_path().parent_path().parent_path()
            / "misc" / "native-seek-tests";
        std::filesystem::create_directories(root);
        path = root / ("seek-" + std::to_string(std::chrono::steady_clock::now().time_since_epoch().count()) + ".wav");
        std::ofstream file(path, std::ios::binary);
        const auto word = [&](uint32_t value, int bytes) {
            for (int i = 0; i < bytes; ++i) file.put(static_cast<char>((value >> (i * 8)) & 0xff));
        };
        constexpr uint32_t dataBytes = 48000 * 2 * 2 * 2;
        file.write("RIFF", 4); word(36 + dataBytes, 4); file.write("WAVEfmt ", 8);
        word(16, 4); word(1, 2); word(2, 2); word(48000, 4); word(48000 * 4, 4);
        word(4, 2); word(16, 2); file.write("data", 4); word(dataBytes, 4);
        for (uint32_t i = 0; i < dataBytes; ++i) file.put(0);
        require(file.good(), "seek WAV fixture must be written");
    }
    ~SeekFixture() { std::error_code ignored; std::filesystem::remove(path, ignored); }
};
}

void testDaemonSeekAtFileEnd() {
    SeekFixture fixture;
    std::atomic<bool> shutdown { false };
    AudioDaemon::SourceHooks hooks;
    hooks.beginSession = [] {};
    hooks.markInputEnded = [] {};
    hooks.requestStop = [] {};
    hooks.setPaused = [](bool) {};
    hooks.push = [](const float*, int) { return true; };
    hooks.pushForGeneration = [](const float*, int, uint64_t) { return true; };
    hooks.generation = [] { return uint64_t{1}; };
    hooks.decoderSampleRateFor = [](int rate) { return rate; };
    hooks.requestStopForGeneration = [](uint64_t) { return true; };
    hooks.seekPrerollFrames = [] { return 0; };
    hooks.replaceBufferedAudioWithPreroll = [](const float*, int frames, int preroll, bool) { return std::max(0, frames - preroll); };
    auto writer = std::make_shared<RpcOutputWriter>(-1, [](const char*, std::size_t bytes) { return static_cast<std::ptrdiff_t>(bytes); });
    AudioDaemon daemon(std::move(hooks), 48000, writer, shutdown);
    nlohmann::json result;
    require(daemon.openFileForTests(fixture.path.string(), 48000, result), "ordinary PCM source must open");
    require(daemon.pauseForTests(true), "source must pause before deterministic seek checks");
    for (const double target : {1.0, 2.0, 5.0}) {
        require(daemon.seekForTests(target, result), "ordinary source seek including EOF must retain a playable tail");
        require(std::abs(result.value("positionSeconds", -1.0) - daemon.listeningMediaPosition(0, 0, 48000)) < 0.001,
            "seek acknowledgement must report the host's normalized origin");
        require(daemon.listeningMediaPosition(0, 0, 48000) < 2.0, "host seek origin must remain inside source duration");
    }
    require(daemon.openSourceForTests({{"kind", "local"}, {"uri", fixture.path.string()},
        {"range", {{"startSeconds", 0.5}, {"endSeconds", 1.5}}}}, 48000, 0.0, result), "ranged source must open");
    require(daemon.seekForTests(1.0, result), "ranged EOF seek must keep existing logical-time normalization");
    require(std::abs(daemon.listeningMediaPosition(0, 0, 48000) - 0.75) < 0.001, "CUE seek origin must use logical source time");
    daemon.stopForTests(result);
}

#ifdef ECHO_DAEMON_SEEK_TEST_MAIN
#include <iostream>
int main() {
    try { testDaemonSeekAtFileEnd(); std::cout << "[PASS] daemon seek tail normalization\n"; return 0; }
    catch (const std::exception& error) { std::cerr << "[FAIL] " << error.what() << '\n'; return 1; }
}
#endif
