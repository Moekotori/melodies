#include "VirtualStageProcessor.h"

#include <atomic>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <limits>
#include <new>

namespace {
std::atomic<size_t> allocations { 0 };
std::atomic<size_t> allocatedBytes { 0 };
void require(bool condition, const char* message)
{
    if (!condition) { std::fprintf(stderr, "FAIL: %s\n", message); std::exit(1); }
}

void fill(echo::DoubleAudioBuffer& buffer, double left, double right)
{
    for (int i = 0; i < buffer.getNumSamples(); ++i)
    {
        buffer.setSample(0, i, left);
        buffer.setSample(1, i, right);
    }
}

void settle(echo::VirtualStageProcessor& processor)
{
    echo::DoubleAudioBuffer silence(2, 4800);
    processor.processBlock(silence, 0, silence.getNumSamples());
}

void testCenterAndSpatialArrivals()
{
    echo::VirtualStageProcessor processor;
    processor.prepare(48000, 4096, 2);
    echo::VirtualStageState state;
    state.enabled = true;
    state.reflection = 0;
    processor.setState(state);
    echo::DoubleAudioBuffer center(2, 4096);
    fill(center, .3, .3);
    processor.processBlock(center, 0, 4096);
    for (int i = 0; i < 4096; ++i)
        require(center.getSample(0, i) == center.getSample(1, i),
            "direct speaker paths must keep correlated content centered");
    require(std::abs(center.getSample(0, 4095) - .3) < 1.0e-9,
        "compensated direct paths must retain steady mono/DC level");

    // Single-channel impulses must reach the opposite ear after a small delay.
    // A larger model angle must delay that arrival further, not just change gain.
    int arrival[2] {};
    for (int angle = 0; angle < 2; ++angle)
    {
        processor.reset();
        state.azimuthDegrees = angle == 0 ? 15 : 75;
        processor.setState(state);
        settle(processor);
        echo::DoubleAudioBuffer impulse(2, 128);
        impulse.setSample(0, 0, .5);
        processor.processBlock(impulse, 0, 128);
        for (int i = 0; i < 128; ++i)
            if (std::abs(impulse.getSample(1, i)) > 1.0e-5) { arrival[angle] = i; break; }
        require(impulse.getSample(0, 0) > .3, "near-ear direct attack is not delayed");
        require(arrival[angle] > 0, "opposite-ear cue must have a delayed arrival");
    }
    require(arrival[1] > arrival[0], "angle controls interaural arrival delay");

    processor.reset();
    state.reflection = 1;
    processor.setState(state);
    settle(processor);
    echo::DoubleAudioBuffer impulse(2, 4096);
    impulse.setSample(0, 0, .5);
    impulse.setSample(1, 0, .5);
    processor.processBlock(impulse, 0, 4096);
    double roomEnergy = 0, earDifference = 0;
    for (int i = 240; i < 1600; ++i)
    {
        const double l = impulse.getSample(0, i), r = impulse.getSample(1, i);
        roomEnergy += l*l + r*r;
        earDifference += (l-r)*(l-r);
    }
    require(roomEnergy > 1.0e-4, "mono content must also receive audible short room cues");
    require(earDifference > roomEnergy * .1, "room arrivals must differ between ears");
    require(impulse.getSample(0, 0) == impulse.getSample(1, 0), "center direct attack must stay centered");
    std::printf("PASS: center, angle arrivals and stereo room cues\n");
}

void testDirectSpeakerPaths()
{
    echo::VirtualStageProcessor processor;
    processor.prepare(48000, 4800, 2);
    echo::VirtualStageState state;
    state.enabled = true;
    state.strength = 1;
    state.reflection = 0;
    processor.setState(state);
    settle(processor);
    echo::DoubleAudioBuffer impulse(2, 128);
    impulse.setSample(0, 0, .5);
    processor.processBlock(impulse, 0, 128);
    for (int i = 2; i < 100; ++i)
        require(std::abs(impulse.getSample(0, i)) <= std::abs(impulse.getSample(0, i - 1)) + 1.0e-12,
            "near-ear compensation must not introduce a delayed negative echo at the far-ear arrival");

    // The old side-only path left identical channels exactly dry with room off.
    // Check the actual processor now renders symmetric direct spatial cues.
    processor.reset();
    state.strength = .45f;
    processor.setState(state);
    settle(processor);
    echo::DoubleAudioBuffer tone(2, 4800);
    constexpr double pi = 3.14159265358979323846;
    for (int i = 0; i < 4800; ++i)
        tone.setSample(0, i, .3 * std::sin(2 * pi * 1000 * i / 48000.0));
    for (int i = 0; i < 4800; ++i) tone.setSample(1, i, tone.getSample(0, i));
    processor.processBlock(tone, 0, 4800);
    double difference = 0;
    for (int i = 2400; i < 4800; ++i)
    {
        require(tone.getSample(0, i) == tone.getSample(1, i), "direct mono cues must remain symmetric");
        const double error = tone.getSample(0, i) - .3 * std::sin(2 * pi * 1000 * i / 48000.0);
        difference += error * error;
    }
    require(std::sqrt(difference / 2400) > .01,
        "default strength must process correlated content even without room reflections");

    double farEnergy[2] {};
    for (int angle = 0; angle < 2; ++angle)
    {
        processor.reset();
        state.strength = 1;
        state.azimuthDegrees = angle == 0 ? 15 : 75;
        processor.setState(state);
        settle(processor);
        fill(tone, 0, 0);
        for (int i = 0; i < 4800; ++i)
            tone.setSample(0, i, .3 * std::sin(2 * pi * 6000 * i / 48000.0));
        processor.processBlock(tone, 0, 4800);
        for (int i = 2400; i < 4800; ++i)
            farEnergy[angle] += tone.getSample(1, i) * tone.getSample(1, i);
    }
    require(farEnergy[1] < farEnergy[0] * .5,
        "wider angles must increase far-ear high-frequency shadow, not only delay the same filter");
    std::printf("PASS: compensated near ear, mono direct cues and angle-dependent head shadow\n");
}

void testTransitionsAndBypass()
{
    echo::VirtualStageProcessor processor;
    processor.prepare(48000, 4096, 2);
    echo::VirtualStageState state;
    state.enabled = true;
    state.reflection = 0;
    processor.setState(state);
    echo::DoubleAudioBuffer buffer(2, 4096);
    fill(buffer, .4, -.4);
    processor.processBlock(buffer, 0, 4096);
    const double before = buffer.getSample(0, 4095);
    processor.setEnabled(false);
    require(processor.isProcessingActive(), "disabling must keep the chain active until its ramp ends");
    fill(buffer, .4, -.4);
    processor.processBlock(buffer, 0, 4096);
    require(std::abs(buffer.getSample(0, 0) - before) < .002, "power-off must not jump to dry audio");
    require(buffer.getSample(0, 4095) == .4, "power-off must settle to exact dry audio");
    require(!processor.isProcessingActive() && processor.tailFramesRemaining() == 0,
        "bypass must release history and processing activity");

    processor.setState(state);
    fill(buffer, .4, -.4);
    processor.processBlock(buffer, 0, 4096);
    state.strength = 0;
    processor.setState(state);
    fill(buffer, .4, -.4);
    processor.processBlock(buffer, 0, 4096);
    require(buffer.getSample(0, 4095) == .4, "zero strength must settle to exact dry audio");
    state.strength = 1;
    state.speakersBypassed = true;
    processor.setState(state);
    fill(buffer, .4, -.4);
    processor.processBlock(buffer, 0, 4096);
    require(buffer.getSample(0, 0) == .4 && buffer.getSample(1, 4095) == -.4,
        "speaker route must bypass exactly");
    std::printf("PASS: smooth power/strength transitions and speaker bypass\n");
}

void testTailAndHardReset()
{
    echo::VirtualStageProcessor processor;
    processor.prepare(48000, 4096, 2);
    echo::VirtualStageState state;
    state.enabled = true;
    processor.setState(state);
    settle(processor);
    echo::DoubleAudioBuffer buffer(2, 4096);
    buffer.setSample(0, 4095, .5);
    buffer.setSample(1, 4095, .5);
    processor.processBlock(buffer, 0, 4096);
    const auto tail = processor.tailFramesRemaining();
    require(tail > 1300 && tail < 4800, "last-sample input must retain a bounded full room tail");
    fill(buffer, 0, 0);
    processor.processBlock(buffer, 0, 4096);
    require(std::abs(buffer.getSample(0, 350)) > 1.0e-4, "EOF silence must render the early reflection");
    require(processor.tailFramesRemaining() == 0, "room tail must drain in finite silence");
    processor.reset();
    fill(buffer, 0, 0);
    processor.processBlock(buffer, 0, 4096);
    for (int i = 0; i < 4096; ++i)
        require(buffer.getSample(0, i) == 0 && buffer.getSample(1, i) == 0,
            "hard replace/seek reset must not leak old sound");
    std::printf("PASS: final-sample tail, EOF drain and hard-reset silence\n");
}

void testRampEdgesAndBlockContinuity()
{
    echo::VirtualStageProcessor processor;
    processor.prepare(48000, 4096, 2);
    echo::VirtualStageState state;
    state.enabled = true;
    state.strength = 1;
    state.reflection = 0;
    processor.setState(state);
    echo::DoubleAudioBuffer buffer(2, 4096);
    fill(buffer, .4, -.4);
    processor.processBlock(buffer, 0, 4096);
    require(std::abs(buffer.getSample(0, 0) - .4) < 1.0e-6,
        "enable ramp must ease in without an abrupt first-sample gain step");
    const double before = buffer.getSample(0, 4095);
    processor.setEnabled(false);
    fill(buffer, .4, -.4);
    processor.processBlock(buffer, 0, 64);
    require(std::abs(buffer.getSample(0, 0) - before) < 1.0e-6,
        "disable ramp must start with a gentle edge at the current audible gain");
    const double reversing = buffer.getSample(0, 63);
    processor.setEnabled(true);
    fill(buffer, .4, -.4);
    processor.processBlock(buffer, 0, 4096);
    require(std::abs(buffer.getSample(0, 0) - reversing) < 1.0e-6,
        "reversing an unfinished ramp must continue from its current audible mix");

    echo::VirtualStageProcessor whole, chunks;
    whole.prepare(48000, 4096, 2);
    chunks.prepare(48000, 64, 2);
    state.reflection = 1;
    state.azimuthDegrees = 43;
    whole.setState(state);
    chunks.setState(state);
    echo::DoubleAudioBuffer reference(2, 4096), segmented(2, 4096);
    for (int i = 0; i < 4096; ++i)
    {
        reference.setSample(0, i, .3 * std::sin(i * .61));
        reference.setSample(1, i, .2 * std::cos(i * .37));
        segmented.setSample(0, i, reference.getSample(0, i));
        segmented.setSample(1, i, reference.getSample(1, i));
    }
    whole.processBlock(reference, 0, 4096);
    for (int offset = 0; offset < 4096; offset += 64) chunks.processBlock(segmented, offset, 64);
    for (int i = 0; i < 4096; ++i)
        for (int ear = 0; ear < 2; ++ear)
            require(reference.getSample(ear, i) == segmented.getSample(ear, i),
                "fractional delay, mix ramp and room history must be continuous across callback boundaries");
    std::printf("PASS: gentle ramp edges, reversal and callback-boundary continuity\n");
}

void testBoundsAndRealtimeAllocations()
{
    for (double sampleRate : { 8000., 48000., 384000. })
    {
        echo::VirtualStageProcessor processor;
        const auto bytesBefore = allocatedBytes.load();
        processor.prepare(sampleRate, 256, 2);
        require(allocatedBytes.load() - bytesBefore <= 256 * 1024,
            "delay storage must stay below 256 KiB at all supported sample rates");
        echo::VirtualStageState state;
        state.enabled = true;
        state.strength = 1;
        state.azimuthDegrees = 75;
        processor.setState(state);
        echo::DoubleAudioBuffer buffer(4, 256);
        const auto count = allocations.load();
        double peak = 0;
        for (int block = 0; block < 32; ++block)
        {
            if (block == 8) { state.azimuthDegrees = 15; state.reflection = 0; processor.setState(state); }
            if (block == 16) { state.strength = 0; processor.setState(state); }
            for (int i = 0; i < 256; ++i)
            {
                const double phase = (block * 256 + i) * .071;
                buffer.setSample(0, i, .5 * std::sin(phase));
                buffer.setSample(1, i, .5 * std::cos(phase * 1.7));
                buffer.setSample(2, i, .125);
            }
            processor.processBlock(buffer, 0, 256);
            for (int i = 0; i < 256; ++i)
            {
                for (int ch = 0; ch < 2; ++ch)
                {
                    const double value = buffer.getSample(ch, i);
                    require(std::isfinite(value), "parameter changes must produce finite PCM");
                    peak = std::max(peak, std::abs(value));
                }
                require(buffer.getSample(2, i) == .125, "unrelated output channels must be untouched");
            }
        }
        state.strength = std::numeric_limits<float>::quiet_NaN();
        state.azimuthDegrees = std::numeric_limits<float>::infinity();
        processor.setState(state);
        require(std::isfinite(processor.getState().strength) && std::isfinite(processor.getState().azimuthDegrees),
            "invalid parameters must fall back to finite values");
        processor.reset();
        require(allocations.load() == count, "processing, parameter changes and reset must never allocate");
        require(peak < .85, "representative full-strength output must have bounded gain");
    }
    std::printf("PASS: sample-rate bounds, finite PCM, other channels and zero realtime allocations\n");
}
} // namespace

void* operator new(std::size_t bytes)
{
    allocations.fetch_add(1);
    allocatedBytes.fetch_add(bytes);
    if (void* pointer = std::malloc(bytes == 0 ? 1 : bytes)) return pointer;
    throw std::bad_alloc();
}
void* operator new[](std::size_t bytes) { return ::operator new(bytes); }
void operator delete(void* pointer) noexcept { std::free(pointer); }
void operator delete[](void* pointer) noexcept { std::free(pointer); }
void operator delete(void* pointer, std::size_t) noexcept { std::free(pointer); }
void operator delete[](void* pointer, std::size_t) noexcept { std::free(pointer); }

int main()
{
    testCenterAndSpatialArrivals();
    testDirectSpeakerPaths();
    testTransitionsAndBypass();
    testTailAndHardReset();
    testRampEdgesAndBlockContinuity();
    testBoundsAndRealtimeAllocations();
}
