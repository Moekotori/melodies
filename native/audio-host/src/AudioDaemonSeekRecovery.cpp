#include "AudioDaemon.h"

#include <algorithm>
#include <cmath>

bool AudioDaemon::recoverFailedSeek(double previousPosition, uint64_t previousOperationId,
                                    bool allowRecovery, nlohmann::json& result)
{
    // Replacement can already have reset DSP/FIFO state. Restoring only the
    // operation id cannot revive a joined decoder: reopen at the native clock
    // position once, under the existing operation lock and caller identity.
    source_.requestStop();
    source_.setPaused(true);
    operationId_.store(previousOperationId, std::memory_order_release);
    nlohmann::json recoveryResult;
    const bool recovered = allowRecovery
        && std::isfinite(previousPosition) && previousPosition >= 0.0
        && seekLocked(previousPosition, recoveryResult, false, true);
    if (!recovered)
    {
        // A persistent processing failure is terminal for resume, but retains
        // the caller's identity so an explicit seek/open can retry later.
        source_.requestStop();
        source_.setPaused(true);
        paused_ = true;
        failedSeekOperationId_ = previousOperationId;
        autoAdvanceSuppressedOperationId_.store(previousOperationId, std::memory_order_release);
        const auto clock = source_.playbackClockFrames
            ? source_.playbackClockFrames() : std::pair<uint64_t, uint64_t>{};
        listeningStartSeconds_.store(std::max(0.0, previousPosition), std::memory_order_release);
        positionFrameOffset_.store(clock.second, std::memory_order_release);
        positionFrameScale_.store(1.0, std::memory_order_release);
        positionUsesDeviceClock_.store(true, std::memory_order_release);
        positionTempoRampFrames_.store(0, std::memory_order_release);
    }
    result["operationId"] = previousOperationId;
    result["positionSeconds"] = std::max(0.0, previousPosition);
    result["error"] = recovered ? "seek_buffer_replace_failed_playback_restored"
                                : "seek_buffer_replace_failed_playback_stopped";
    return false;
}
