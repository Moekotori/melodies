#define ECHO_AUDIO_ENGINE_TESTS 1

#include "../../audio-engine/LevelMeterProcessor.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <mutex>
#include <stdexcept>
#include <string>
#include <thread>
#include <vector>

namespace
{
using namespace std::chrono_literals;

constexpr double pi = 3.14159265358979323846;

void require(bool condition, const char* message)
{
    if (! condition)
    {
        std::fprintf(stderr, "FAIL: %s\n", message);
        std::exit(1);
    }
}

template <typename Predicate>
bool waitUntil(Predicate predicate, std::chrono::milliseconds timeout = 1000ms)
{
    const auto deadline = std::chrono::steady_clock::now() + timeout;
    while (! predicate())
    {
        if (std::chrono::steady_clock::now() >= deadline)
            return false;
        std::this_thread::sleep_for(1ms);
    }
    return true;
}

echo::FloatAudioBuffer makeSine(int channels, int frames, double sampleRate, float amplitude, float frequencyHz)
{
    echo::FloatAudioBuffer buffer(channels, frames);
    for (int sample = 0; sample < frames; ++sample)
    {
        const float value = amplitude * static_cast<float>(std::sin(
            2.0 * pi * static_cast<double>(frequencyHz) * static_cast<double>(sample) / sampleRate));
        for (int channel = 0; channel < channels; ++channel)
            buffer.setSample(channel, sample, value);
    }
    return buffer;
}

echo::FloatAudioBuffer cloneBuffer(const echo::FloatAudioBuffer& source)
{
    echo::FloatAudioBuffer copy(source.getNumChannels(), source.getNumSamples());
    for (int channel = 0; channel < source.getNumChannels(); ++channel)
    {
        for (int sample = 0; sample < source.getNumSamples(); ++sample)
            copy.setSample(channel, sample, source.getSample(channel, sample));
    }
    return copy;
}

bool buffersExactlyEqual(const echo::FloatAudioBuffer& left, const echo::FloatAudioBuffer& right)
{
    if (left.getNumChannels() != right.getNumChannels() || left.getNumSamples() != right.getNumSamples())
        return false;
    for (int channel = 0; channel < left.getNumChannels(); ++channel)
    {
        for (int sample = 0; sample < left.getNumSamples(); ++sample)
        {
            if (left.getSample(channel, sample) != right.getSample(channel, sample))
                return false;
        }
    }
    return true;
}

void feedBlocks(echo::LevelMeterProcessor& meter, echo::FloatAudioBuffer& buffer, int blocks)
{
    for (int block = 0; block < blocks; ++block)
        meter.processBlock(buffer, 0, buffer.getNumSamples());
}

// Declare each gate before its meter so the worker is joined before this
// callback target is destroyed.
struct SnapshotGate
{
    std::mutex mutex;
    echo::LevelMeterSnapshot latest;
    std::atomic<int> count { 0 };
    std::atomic<std::thread::id> threadId {};

    echo::LevelMeterProcessor::Callback bind()
    {
        return [this](const echo::LevelMeterSnapshot& snapshot)
        {
            std::lock_guard<std::mutex> lock(mutex);
            latest = snapshot;
            threadId.store(std::this_thread::get_id(), std::memory_order_release);
            count.fetch_add(1, std::memory_order_acq_rel);
        };
    }

    echo::LevelMeterSnapshot copy()
    {
        std::lock_guard<std::mutex> lock(mutex);
        return latest;
    }
};

void testProcessBlockDoesNotMutateAudio()
{
    SnapshotGate gate;
    echo::LevelMeterProcessor meter;
    meter.setIntervalMs(1);
    meter.setVisualSpectrumEnabled(true);
    meter.setCallback(gate.bind());
    meter.prepare(48000.0, 128, 2);

    auto buffer = makeSine(2, 128, 48000.0, 0.4f, 1000.0f);
    const auto dry = cloneBuffer(buffer);
    feedBlocks(meter, buffer, 40);
    require(buffersExactlyEqual(buffer, dry), "processBlock must leave every audio sample unchanged");
    require(waitUntil([&] { return gate.count.load() > 0; }), "meter must still publish telemetry");
}

void testCallbackRunsOffAudioThread()
{
    SnapshotGate gate;
    echo::LevelMeterProcessor meter;
    meter.setIntervalMs(1);
    meter.setCallback(gate.bind());
    meter.prepare(48000.0, 64, 2);

    auto buffer = makeSine(2, 64, 48000.0, 0.5f, 440.0f);
    const auto audioThread = std::this_thread::get_id();
    meter.processBlock(buffer, 0, buffer.getNumSamples());
    require(waitUntil([&] { return gate.count.load() > 0; }), "callback must run for a report-sized block");
    require(gate.threadId.load() != audioThread, "callback thread must differ from processBlock thread");
}

void testSlowWorkerDoesNotBlockProcessBlock()
{
    SnapshotGate gate;
    echo::LevelMeterProcessor meter;
    meter.setIntervalMs(1);
    meter.setVisualSpectrumEnabled(true);
    meter.setCallback(gate.bind());
    meter.prepare(48000.0, 64, 2);
    meter.setWorkerStallMsForTests(40);

    auto buffer = makeSine(2, 64, 48000.0, 0.5f, 1000.0f);
    const auto started = std::chrono::steady_clock::now();
    meter.processBlock(buffer, 0, buffer.getNumSamples());
    const auto elapsed = std::chrono::steady_clock::now() - started;
    require(elapsed < 5ms, "processBlock must return without waiting for a slow worker");
}

void testBacklogDropsTelemetryNotAudio()
{
    SnapshotGate gate;
    echo::LevelMeterProcessor meter;
    meter.setIntervalMs(1);
    meter.setVisualSpectrumEnabled(true);
    meter.setCallback(gate.bind());
    meter.prepare(192000.0, 64, 2);
    meter.setWorkerStallMsForTests(80);

    auto buffer = makeSine(2, 64, 192000.0, 0.4f, 1000.0f);
    const auto dry = cloneBuffer(buffer);
    for (int block = 0; block < 200; ++block)
    {
        const auto started = std::chrono::steady_clock::now();
        meter.processBlock(buffer, 0, buffer.getNumSamples());
        require(std::chrono::steady_clock::now() - started < 5ms,
            "backlogged telemetry must not stall the audio callback");
        require(buffersExactlyEqual(buffer, dry), "backlogged telemetry must not mutate audio");
        require(meter.getMailboxOccupancyForTests() <= 3, "mailbox occupancy must stay bounded");
    }
    require(meter.getDroppedSnapshotCountForTests() > 0, "a slow worker must drop telemetry instead of blocking");
    meter.setWorkerStallMsForTests(0);
}

void testMailboxNeverGrowsUnbounded()
{
    SnapshotGate gate;
    echo::LevelMeterProcessor meter;
    meter.setIntervalMs(1);
    meter.setCallback(gate.bind());
    meter.prepare(48000.0, 32, 2);
    meter.setWorkerStallMsForTests(100);

    auto buffer = makeSine(2, 32, 48000.0, 0.3f, 800.0f);
    size_t peakOccupancy = 0;
    for (int block = 0; block < 300; ++block)
    {
        meter.processBlock(buffer, 0, buffer.getNumSamples());
        peakOccupancy = std::max(peakOccupancy, meter.getMailboxOccupancyForTests());
        require(peakOccupancy <= 3, "mailbox must stay within the three preallocated slots");
    }
    require(meter.getDroppedSnapshotCountForTests() > 0, "overflowed mailbox must drop snapshots");
    meter.setWorkerStallMsForTests(0);
}

void testDisabledSpectrumSkipsGoertzel()
{
    SnapshotGate gate;
    echo::LevelMeterProcessor meter;
    meter.setIntervalMs(1);
    meter.setVisualSpectrumEnabled(false);
    meter.setCallback(gate.bind());
    meter.prepare(48000.0, 256, 2);

    auto buffer = makeSine(2, 256, 48000.0, 0.5f, 1000.0f);
    feedBlocks(meter, buffer, 30);
    require(waitUntil([&] { return gate.count.load() > 0; }), "peak/RMS must still publish with spectrum off");
    require(meter.getSpectrumComputationCountForTests() == 0,
        "disabled visual spectrum must not run Goertzel");
    require(! gate.copy().visualSpectrumReady, "disabled visual spectrum must not report ready");
}

void testSpectrumReadyOnlyAfterFullWindow()
{
    SnapshotGate gate;
    echo::LevelMeterProcessor meter;
    meter.setIntervalMs(1);
    meter.setVisualSpectrumEnabled(true);
    meter.setCallback(gate.bind());
    meter.prepare(48000.0, 64, 2);

    auto buffer = makeSine(2, 64, 48000.0, 0.45f, 1000.0f);
    meter.processBlock(buffer, 0, buffer.getNumSamples());
    require(waitUntil([&] { return gate.count.load() > 0; }), "first interval must publish");
    require(! gate.copy().visualSpectrumReady, "spectrum must stay unready before a full window accumulates");

    feedBlocks(meter, buffer, 40);
    require(waitUntil([&] { return gate.copy().visualSpectrumReady; }),
        "spectrum must become ready only after 2048 window samples");
    const auto snapshot = gate.copy();
    require(snapshot.visualSpectrum.size() == 128,
        "native spectrum must provide 128 real frequency probes");
    require(*std::max_element(snapshot.visualSpectrum.begin(), snapshot.visualSpectrum.end()) > 0.1f,
        "primed spectrum must react to audible energy");
}

void testDenseSpectrumLocatesRealFrequency()
{
    SnapshotGate gate;
    echo::LevelMeterProcessor meter;
    meter.setIntervalMs(100);
    meter.setVisualSpectrumEnabled(true);
    meter.setCallback(gate.bind());
    meter.prepare(48000.0, 4800, 2);

    constexpr size_t targetBucket = 104;
    const float frequency = static_cast<float>(40.0 * std::pow(18000.0 / 40.0,
        static_cast<double>(targetBucket) / 127.0));
    auto buffer = makeSine(2, 4800, 48000.0, 0.45f, frequency);
    meter.processBlock(buffer, 0, buffer.getNumSamples());
    require(waitUntil([&] { return gate.copy().visualSpectrumReady; }),
        "dense native probes must publish a real tone");
    const auto snapshot = gate.copy();
    const auto peak = std::max_element(snapshot.visualSpectrum.begin(), snapshot.visualSpectrum.end());
    require(static_cast<size_t>(peak - snapshot.visualSpectrum.begin()) == targetBucket,
        "a real high-frequency tone must peak at its logarithmic probe");
    require(*peak > snapshot.visualSpectrum[targetBucket - 1] + 0.05f,
        "neighboring native probes must carry independently measured detail");
}

void testReenableDoesNotReuseOldWindow()
{
    SnapshotGate gate;
    echo::LevelMeterProcessor meter;
    meter.setIntervalMs(1);
    meter.setVisualSpectrumEnabled(true);
    meter.setCallback(gate.bind());
    meter.prepare(48000.0, 128, 2);

    auto buffer = makeSine(2, 128, 48000.0, 0.5f, 1000.0f);
    feedBlocks(meter, buffer, 20);
    require(waitUntil([&] { return gate.copy().visualSpectrumReady; }), "spectrum must prime before disable");

    const auto countBeforeDisable = gate.count.load();
    meter.setVisualSpectrumEnabled(false);
    feedBlocks(meter, buffer, 4);
    require(waitUntil([&] {
        return gate.count.load() > countBeforeDisable && ! gate.copy().visualSpectrumReady;
    }), "disabled spectrum must stop reporting ready");

    const auto countBefore = gate.count.load();
    meter.setVisualSpectrumEnabled(true);
    meter.processBlock(buffer, 0, buffer.getNumSamples());
    require(waitUntil([&] { return gate.count.load() > countBefore; }), "re-enable must publish a fresh snapshot");
    require(! gate.copy().visualSpectrumReady, "re-enable must accumulate a new window instead of reusing the old one");
}

void testResetDoesNotLeakPreviousTrack()
{
    SnapshotGate gate;
    echo::LevelMeterProcessor meter;
    meter.setIntervalMs(1);
    meter.setVisualSpectrumEnabled(true);
    meter.setCallback(gate.bind());
    meter.prepare(48000.0, 128, 2);

    auto loud = makeSine(2, 128, 48000.0, 0.9f, 1000.0f);
    feedBlocks(meter, loud, 20);
    require(waitUntil([&] {
        const auto snapshot = gate.copy();
        return snapshot.visualSpectrumReady && ! snapshot.peakDb.empty() && snapshot.peakDb.front() > -6.0f;
    }), "loud track must publish peak and spectrum");

    meter.reset();
    echo::FloatAudioBuffer silence(2, 128);
    const auto countBefore = gate.count.load();
    feedBlocks(meter, silence, 8);
    require(waitUntil([&] { return gate.count.load() > countBefore; }), "reset must still allow later telemetry");
    const auto snapshot = gate.copy();
    require(! snapshot.peakDb.empty() && snapshot.peakDb.front() <= -59.0f,
        "reset must not leak the previous track peak");
    require(! snapshot.rmsDb.empty() && snapshot.rmsDb.front() <= -59.0f,
        "reset must not leak the previous track RMS");
    require(! snapshot.visualSpectrumReady, "reset must not leak the previous track spectrum");
}

void testPrepareUpdatesSpectrumCoefficients()
{
    SnapshotGate gate;
    echo::LevelMeterProcessor meter;
    meter.setIntervalMs(1);
    meter.setVisualSpectrumEnabled(true);
    meter.setCallback(gate.bind());
    meter.prepare(44100.0, 128, 2);
    require(std::abs(meter.getSpectrumSampleRateForTests() - 44100.0) < 0.5,
        "44.1 kHz must keep a 1:1 spectrum rate");

    meter.prepare(96000.0, 128, 2);
    require(std::abs(meter.getSpectrumSampleRateForTests() - 48000.0) < 0.5,
        "96 kHz must rebuild decimated 48 kHz spectrum coefficients");

    auto buffer = makeSine(2, 128, 96000.0, 0.4f, 1000.0f);
    feedBlocks(meter, buffer, 40);
    require(waitUntil([&] { return gate.copy().visualSpectrumReady; }),
        "spectrum must still prime after a sample-rate rebuild");
}

void testRapidControlSurfaceDoesNotCrash()
{
    SnapshotGate gate;
    echo::LevelMeterProcessor meter;
    meter.prepare(48000.0, 64, 2);
    auto buffer = makeSine(2, 64, 48000.0, 0.3f, 660.0f);
    std::atomic<bool> stop { false };
    std::thread audio([&] {
        while (! stop.load(std::memory_order_acquire))
            meter.processBlock(buffer, 0, buffer.getNumSamples());
    });

    for (int step = 0; step < 200; ++step)
    {
        meter.setCallback(gate.bind());
        meter.setIntervalMs(step % 2 == 0 ? 1 : 5);
        meter.setVisualSpectrumEnabled(step % 3 == 0);
        if (step % 7 == 0)
            meter.reset();
        if (step % 11 == 0)
            meter.setCallback({});
        if (step % 13 == 0)
            meter.prepare(48000.0, 64, 2);
    }

    stop.store(true, std::memory_order_release);
    audio.join();
    meter.setCallback({});
}

void testDestructorStopsWorkerWithoutCallbackAfterDestroy()
{
    auto calls = std::make_shared<std::atomic<int>>(0);
    {
        echo::LevelMeterProcessor meter;
        meter.setIntervalMs(1);
        meter.setVisualSpectrumEnabled(true);
        meter.setCallback([calls](const echo::LevelMeterSnapshot&) {
            calls->fetch_add(1, std::memory_order_relaxed);
        });
        meter.prepare(48000.0, 64, 2);
        auto buffer = makeSine(2, 64, 48000.0, 0.4f, 1000.0f);
        feedBlocks(meter, buffer, 8);
        require(waitUntil([&] { return calls->load() > 0; }), "worker must deliver at least one callback before destroy");
    }
    const int afterDestroy = calls->load();
    std::this_thread::sleep_for(50ms);
    require(calls->load() == afterDestroy, "destructor must not invoke callback after destroy");
}

void testCallbackExceptionKeepsWorkerAndAudioSafe()
{
    std::atomic<bool> thrown { false };
    std::atomic<int> successes { 0 };
    echo::LevelMeterProcessor meter;
    meter.setIntervalMs(1);
    meter.setCallback([&](const echo::LevelMeterSnapshot&) {
        if (! thrown.exchange(true, std::memory_order_acq_rel))
            throw std::runtime_error("telemetry observer failed");
        successes.fetch_add(1, std::memory_order_relaxed);
    });
    meter.prepare(48000.0, 64, 2);

    auto buffer = makeSine(2, 64, 48000.0, 0.5f, 1000.0f);
    const auto dry = cloneBuffer(buffer);
    feedBlocks(meter, buffer, 8);
    require(buffersExactlyEqual(buffer, dry), "a throwing callback must not mutate audio");
    require(waitUntil([&] { return thrown.load(std::memory_order_acquire); }),
        "worker must invoke the throwing observer");
    feedBlocks(meter, buffer, 8);
    require(waitUntil([&] { return successes.load() > 0; }), "worker must survive callback exceptions");
}

void testProcessBlockDeadlineExcludesFullSpectrum()
{
    SnapshotGate gate;
    echo::LevelMeterProcessor meter;
    meter.setIntervalMs(1);
    meter.setVisualSpectrumEnabled(true);
    meter.setCallback(gate.bind());
    meter.prepare(192000.0, 64, 2);

    auto buffer = makeSine(2, 64, 192000.0, 0.4f, 1000.0f);
    feedBlocks(meter, buffer, 160);
    require(waitUntil([&] { return gate.copy().visualSpectrumReady; }),
        "192 kHz warmup must fill the decimated spectrum window");

    meter.setWorkerStallMsForTests(50);
    std::this_thread::sleep_for(10ms);
    const auto computeBefore = meter.getSpectrumComputationCountForTests();
    std::chrono::nanoseconds worst { 0 };
    for (int block = 0; block < 8; ++block)
    {
        const auto started = std::chrono::steady_clock::now();
        meter.processBlock(buffer, 0, buffer.getNumSamples());
        worst = std::max(worst, std::chrono::steady_clock::now() - started);
    }
    const auto computeAfter = meter.getSpectrumComputationCountForTests();
    require(worst < 2ms, "processBlock must stay inside a 192 kHz / 64-frame callback budget");
    require(computeAfter == computeBefore, "full 128x2048 Goertzel must not run inside processBlock");
    require(meter.getMailboxOccupancyForTests() > 0 || meter.getDroppedSnapshotCountForTests() > 0,
        "the realtime path must publish or drop telemetry without computing spectrum");

    meter.setWorkerStallMsForTests(0);
    require(waitUntil([&] { return meter.getSpectrumComputationCountForTests() > computeBefore; }),
        "spectrum work must resume on the worker after processBlock returns");
}

void testConcurrentResetCallbackAndMailbox()
{
    SnapshotGate gate;
    echo::LevelMeterProcessor meter;
    meter.prepare(48000.0, 64, 2);
    auto buffer = makeSine(2, 64, 48000.0, 0.25f, 520.0f);
    std::atomic<bool> stop { false };
    std::thread audio([&] {
        while (! stop.load(std::memory_order_acquire))
            meter.processBlock(buffer, 0, buffer.getNumSamples());
    });
    std::thread control([&] {
        for (int step = 0; step < 150 && ! stop.load(std::memory_order_acquire); ++step)
        {
            meter.setCallback(gate.bind());
            meter.setVisualSpectrumEnabled(step % 2 == 0);
            meter.setIntervalMs(1);
            meter.reset();
            if (step % 5 == 0)
                meter.setCallback({});
        }
    });
    std::this_thread::sleep_for(150ms);
    stop.store(true, std::memory_order_release);
    audio.join();
    control.join();
    meter.setCallback({});
}
} // namespace

int main()
{
    testProcessBlockDoesNotMutateAudio();
    testCallbackRunsOffAudioThread();
    testSlowWorkerDoesNotBlockProcessBlock();
    testBacklogDropsTelemetryNotAudio();
    testMailboxNeverGrowsUnbounded();
    testDisabledSpectrumSkipsGoertzel();
    testSpectrumReadyOnlyAfterFullWindow();
    testDenseSpectrumLocatesRealFrequency();
    testReenableDoesNotReuseOldWindow();
    testResetDoesNotLeakPreviousTrack();
    testPrepareUpdatesSpectrumCoefficients();
    testRapidControlSurfaceDoesNotCrash();
    testDestructorStopsWorkerWithoutCallbackAfterDestroy();
    testCallbackExceptionKeepsWorkerAndAudioSafe();
    testProcessBlockDeadlineExcludesFullSpectrum();
    testConcurrentResetCallbackAndMailbox();
    std::puts("level_meter_processor_tests: all tests passed");
    return 0;
}
