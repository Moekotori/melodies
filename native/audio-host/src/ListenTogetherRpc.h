#pragma once
#include "ListenTogetherRealtimeAudio.h"
#include "NativePlaybackPipeline.h"
#include "RpcOutputWriter.h"
#include <functional>

bool handleListenTogetherRpc(
    const std::string& method, const echo_audio_host::Json& object,
    const echo_audio_host::Json* paramsObject, RpcOutputWriter& stdoutFd,
    PcmRingAudioSource* audioSource, NativePlaybackPipeline* playbackPipeline,
    echo::listen_together::ListenTogetherHostAudioStream* listenTogetherHost,
    echo::listen_together::ListenTogetherGuestAudioStream* listenTogetherGuest,
    std::atomic<std::uint64_t>* listenTogetherGuestGeneration,
    const std::function<bool()>& stopListenTogetherGuestSession,
    const std::function<void()>& resetPacketReader,
    const std::function<bool(std::string&)>& prepareGuestOutput,
    int outputLatencyFrames = 0);
