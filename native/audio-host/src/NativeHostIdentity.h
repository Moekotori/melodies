#pragma once
#include "../../audio-engine/third_party/nlohmann_json.hpp"

namespace echo_audio_host {
// Static image metadata plus the calling process PID. No device enumeration,
// CUDA initialization, settings mutation or file hashing at runtime.
nlohmann::json nativeHostIdentity(bool includeLoadedExecutable = false);
}
