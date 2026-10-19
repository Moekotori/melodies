#include "SpatialDspProcessor.h"
#include <atomic>
#include <cstdio>
#include <cstdlib>
#include <new>

namespace {
std::atomic<size_t> allocations { 0 };
void require(bool condition, const char* message) {
    if (!condition) { std::fprintf(stderr, "FAIL: %s\n", message); std::exit(1); }
}
void fill(echo::DoubleAudioBuffer& buffer, double l = .4, double r = -.4) {
    for (int i = 0; i < buffer.getNumSamples(); ++i) {
        buffer.setSample(0, i, l); buffer.setSample(1, i, r);
        if (buffer.getNumChannels() > 2) buffer.setSample(2, i, .123);
    }
}
void transitions(double rate) {
    echo::SpatialDspProcessor processor;
    processor.prepare(rate, 256, 3);
    echo::DoubleAudioBuffer buffer(3, 256);
    echo::CrossfeedState state;
    double previous = .4;
    const auto before = allocations.load();
    for (int block = 0; block < 100; ++block) {
        if (block == 1) state.enabled = true;
        if (block == 25) { state.bs2b = true; state.cutoffHz = 4000; state.feedDb = 1; }
        if (block == 50) { state.amount = 1; state.cutoffHz = 100; state.feedDb = 15; }
        // Reverse in-flight transitions rather than waiting for each to finish.
        if (block >= 55 && block < 60) { state.bs2b = !state.bs2b; state.enabled = !state.enabled; }
        if (block == 60) state.enabled = false;
        processor.setCrossfeedState(state);
        fill(buffer);
        processor.processCrossfeedBlock(buffer, 0, 256);
        for (int i = 0; i < 256; ++i) {
            const double l = buffer.getSample(0, i), r = buffer.getSample(1, i);
            require(std::isfinite(l) && std::isfinite(r), "transitions produce finite PCM");
            require(std::abs(l - previous) < .01, "power, model and parameter changes must not jump");
            require(std::abs(l + r) < 1e-12, "symmetric side content stays symmetric");
            require(buffer.getSample(2, i) == .123, "extra channels stay untouched");
            previous = l;
        }
    }
    require(allocations.load() == before, "processing and target changes allocate no memory");
    require(!processor.isAnyEnabled() && processor.isTailDrained(), "disabled fade clears activity and tail");
    require(previous == .4, "completed bypass is exactly dry");
    std::printf("PASS: transitions, bounded state and no allocations at %.0f Hz\n", rate);
}

void lifetimeAndPartition() {
    echo::SpatialDspProcessor whole, split;
    whole.prepare(48000, 4096, 2); split.prepare(48000, 4096, 2);
    echo::CrossfeedState state;
    state.enabled = true; state.bs2b = true;
    echo::DoubleAudioBuffer a(2, 4096), b(2, 4096);
    for (int phase = 0; phase < 4; ++phase) {
        if (phase == 1) { state.bs2b = false; state.amount = .9; state.cutoffHz = 100; }
        if (phase == 2) state.enabled = false;
        if (phase == 3) { whole.reset(); split.reset(); state.enabled = true; state.bs2b = true; }
        whole.setCrossfeedState(state); split.setCrossfeedState(state);
        if (phase == 2) require(whole.isAnyEnabled(), "disabled module remains active until fade drains");
        fill(a); fill(b);
        whole.processCrossfeedBlock(a, 0, 4096);
        for (int start = 0; start < 4096; start += 37)
            split.processCrossfeedBlock(b, start, std::min(37, 4096 - start));
        for (int ear = 0; ear < 2; ++ear)
            for (int i = 0; i < 4096; ++i)
                require(std::abs(a.getSample(ear, i) - b.getSample(ear, i)) < 1e-12,
                    "ramps and filter histories are independent of callback partition");
    }
    fill(a, 0, 0);
    a.setSample(0, 4095, .5);
    whole.processCrossfeedBlock(a, 0, 4096);
    require(!whole.isTailDrained(), "last-sample filter history participates in EOF drain");
    fill(a, 0, 0);
    whole.processCrossfeedBlock(a, 0, 4096);
    require(whole.isTailDrained(), "filter tail drains in silence");
    whole.reset(); fill(a, 0, 0); whole.processCrossfeedBlock(a, 0, 4096);
    for (int i = 0; i < 4096; ++i)
        require(a.getSample(0, i) == 0 && a.getSample(1, i) == 0, "seek/reset leaves no stale signal");
    state.enabled = true;
    whole.setCrossfeedState(state);
    echo::VirtualStageState stage; stage.enabled = true;
    whole.setVirtualStageState(stage);
    require(!whole.isCrossfeedEnabled(), "virtual stage still disables crossfeed target");
    std::puts("PASS: callback partitions, fade activity, EOF tail, hard reset and mutual exclusion");
}
} // namespace

void* operator new(std::size_t bytes) {
    allocations.fetch_add(1);
    if (void* ptr = std::malloc(bytes ? bytes : 1)) return ptr;
    throw std::bad_alloc();
}
void* operator new[](std::size_t bytes) { return ::operator new(bytes); }
void operator delete(void* ptr) noexcept { std::free(ptr); }
void operator delete[](void* ptr) noexcept { std::free(ptr); }
void operator delete(void* ptr, std::size_t) noexcept { std::free(ptr); }
void operator delete[](void* ptr, std::size_t) noexcept { std::free(ptr); }

int main() {
    // Unmodified libbs2b 3.1.0 Bauer impulse reference, already recorded in
    // dsp_filter_rack_tests.cpp; coefficient smoothing must not change steady DSP.
    echo::Bs2bCrossfeed reference;
    reference.configure(48000, 700, 4.5);
    double l = .5, r = 0;
    reference.process(l, r);
    require(std::abs(l - .39469092309225123) < 1e-13
        && std::abs(r - .016342575456538253) < 1e-13, "BS2B retains upstream response");
    for (double rate : { 8000., 48000., 384000. }) transitions(rate);
    lifetimeAndPartition();
}
