#pragma once
#include <cstdint>

#include "../../audio-engine/buffer.h"

namespace echo::listen_together {

class RealtimeProgramPcmSink
{
public:
    virtual ~RealtimeProgramPcmSink() = default;

    virtual void onProgramClock(std::uint64_t, std::uint64_t, int, bool, double = 1.0) noexcept {}
    virtual void onProgramPcm(
        const echo::FloatAudioBuffer& audio,
        int startFrame,
        int frameCount,
        int sampleRate) noexcept = 0;
};

} // namespace echo::listen_together
