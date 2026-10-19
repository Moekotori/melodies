#pragma once

namespace echo_audio_host
{
inline constexpr int sdmRealtimeAdmissionSourceFrames = 4096;
inline constexpr int maximumNativeEchoSrcUpsampleFactor = 16;
// DSD512 expands a 44.1/48 kHz source through five x2 stages.
inline constexpr int maximumNativeSdmUpsampleFactor = 32;
}
