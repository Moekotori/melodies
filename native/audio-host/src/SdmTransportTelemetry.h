#pragma once
#include <cstdint>

namespace echo_audio_host {
// Logical DSD bits per channel, for both DoP words (16 bits) and native bytes
// (8 bits). These are sampled ring facts, not device/driver xrun counters.
struct SdmTransportTelemetry {
    uint64_t generation = 0, readyDsdBits = 0, capacityDsdBits = 0, playedDsdBits = 0;
    uint64_t underrunCallbacks = 0, underrunDsdBits = 0;
    bool paused = false, prebuffering = false, inputEnded = false, stopped = false, hasAudio = false;
};
}
