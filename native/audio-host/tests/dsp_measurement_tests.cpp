#include "DspMeasurement.h"
#include "EqProcessor.h"
#include <cmath>
#include <iostream>
#include <stdexcept>
#include <thread>

static void require(bool value, const char* message) { if (!value) throw std::runtime_error(message); }
int main() try {
    echo::EqProcessor eq;
    eq.prepare(48000, 256, 2);
    eq.setEnabled(true); eq.setPreampDb(-6);
    echo::DoubleAudioBuffer buffer(2, 256);
    for (int block = 0; block < 480; ++block) {
        if (block == 240) {
            require(!eq.loudnessMeasurement().ready && eq.loudnessMeasurement().windowMs == 0,
                "unrequested EQ comparison performed loudness analysis");
            eq.requestLoudnessMeasurement();
        }
        for (int frame = 0; frame < 256; ++frame)
            for (int channel = 0; channel < 2; ++channel)
                buffer.setSample(channel, frame, 0.1 * std::sin(2 * 3.141592653589793 * 1000 * (block*256 + frame) / 48000));
        eq.processBlock(buffer, 0, 256);
    }
    const auto measured = eq.loudnessMeasurement();
    require(measured.ready && measured.windowMs >= 1000, "programme window did not become ready");
    require(std::abs(measured.gainDb + 6) < 0.02, "measured EQ transfer gain does not match real output");
    eq.setBandGainDb(17, 3);
    require(!eq.loudnessMeasurement().ready, "editing reused a previous curve's measurement");
    eq.reset();
    require(!eq.loudnessMeasurement().ready, "session reset reused loudness");
    echo::DspLoudnessMeter meter; meter.prepare(48000);
    echo::DspMeasurement silence;
    for (int i = 0; i < 240; ++i) { buffer.clear(); meter.begin(buffer, 0, 256, 1); silence = meter.end(buffer, 0, 256); }
    require(!silence.ready, "silence authorized loudness compensation");
    echo::DspMeasurementStore store;
    auto value = measured; value.revision = store.revision(); value.realtimeRatio = 0.25;
    store.publish(value);
    require(store.read().realtimeRatio == 0.25, "runtime cost publication failed");
    store.invalidate(); require(!store.read().ready && store.read().realtimeRatio == 0, "configuration invalidation retained runtime measurements");
    require(!store.loudnessRequested(), "comparison meter started without demand");
    store.requestLoudness();
    require(store.loudnessRequested(), "comparison request did not wake meter");
    const auto requestedRevision = store.revision();
    store.requestLoudness();
    require(store.revision() == requestedRevision, "renewing demand discarded a live measurement window");
    std::this_thread::sleep_for(std::chrono::milliseconds(2100));
    store.read();
    require(!store.loudnessRequested(), "status reads kept comparison analysis alive");
    store.requestLoudness();
    require(store.revision() != requestedRevision, "new demand reused an expired programme window");
    std::cout << "Native programme measurement, silence and revision safety passed\n";
} catch (const std::exception& error) { std::cerr << error.what() << '\n'; return 1; }
