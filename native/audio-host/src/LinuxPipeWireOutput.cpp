#if defined(__linux__)

#include "LinuxPipeWireOutput.h"
#include "LinuxOutputFailure.h"
#include <cerrno>

#if defined(ECHO_ENABLE_PIPEWIRE) && ECHO_ENABLE_PIPEWIRE

#include <pipewire/pipewire.h>
#include <pipewire/extensions/metadata.h>
#include <spa/param/audio/format-utils.h>
#include <spa/pod/builder.h>
#include <spa/utils/dict.h>
#include <spa/utils/hook.h>
#include <spa/utils/string.h>

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cstdlib>
#include <cstring>
#include <mutex>
#include <thread>

namespace
{
std::once_flag pipewireInitOnce;

void ensurePipeWireInit()
{
    std::call_once(pipewireInitOnce, [] { pw_init(nullptr, nullptr); });
}

const char* dictLookup(const spa_dict* props, const char* key)
{
    return props == nullptr ? nullptr : spa_dict_lookup(props, key);
}

std::string dictString(const spa_dict* props, const char* key)
{
    const char* value = dictLookup(props, key);
    return value == nullptr ? std::string() : std::string(value);
}

int dictInt(const spa_dict* props, const char* key, int fallback)
{
    const char* value = dictLookup(props, key);
    if (value == nullptr || value[0] == '\0')
        return fallback;
    char* end = nullptr;
    const long parsed = std::strtol(value, &end, 10);
    if (end == value || parsed <= 0 || parsed > 768000)
        return fallback;
    return static_cast<int>(parsed);
}

std::string parseDefaultSinkName(const char* json)
{
    if (json == nullptr)
        return {};
    const char* key = std::strstr(json, "\"name\"");
    if (key == nullptr)
        return {};
    const char* colon = std::strchr(key, ':');
    if (colon == nullptr)
        return {};
    const char* quote = std::strchr(colon, '"');
    if (quote == nullptr)
        return {};
    const char* end = std::strchr(quote + 1, '"');
    if (end == nullptr || end <= quote + 1)
        return {};
    return std::string(quote + 1, static_cast<size_t>(end - (quote + 1)));
}

bool nameMatches(const std::string& candidate, const std::string& requested)
{
    if (requested.empty() || candidate.empty())
        return false;
    auto lower = [](std::string value) {
        std::transform(value.begin(), value.end(), value.begin(), [](unsigned char ch) {
            return static_cast<char>(std::tolower(ch));
        });
        return value;
    };
    const auto left = lower(candidate);
    const auto right = lower(requested);
    return left == right || left.find(right) != std::string::npos || right.find(left) != std::string::npos;
}

struct EnumState
{
    pw_main_loop* loop = nullptr;
    pw_context* context = nullptr;
    pw_core* core = nullptr;
    pw_registry* registry = nullptr;
    pw_metadata* metadata = nullptr;
    spa_hook coreListener {};
    spa_hook registryListener {};
    spa_hook metadataListener {};
    int pendingSeq = 0;
    int defaultClockRate = 48000;
    std::string defaultSinkName;
    std::vector<LinuxPipeWireSinkInfo> sinks;
    bool done = false;
    std::string error;
};

void coreDone(void* data, uint32_t id, int seq)
{
    auto* state = static_cast<EnumState*>(data);
    if (id == PW_ID_CORE && seq == state->pendingSeq)
    {
        state->done = true;
        pw_main_loop_quit(state->loop);
    }
}

void coreError(void* data, uint32_t, int, int res, const char* message)
{
    auto* state = static_cast<EnumState*>(data);
    if (state->error.empty())
    {
        state->error = "PipeWire core error";
        if (res != 0)
            state->error += " (" + std::to_string(res) + ")";
        if (message != nullptr && message[0] != '\0')
            state->error += ": " + std::string(message);
    }
    state->done = true;
    pw_main_loop_quit(state->loop);
}

void coreInfo(void* data, const pw_core_info* info)
{
    auto* state = static_cast<EnumState*>(data);
    if (info == nullptr)
        return;
    const int rate = dictInt(info->props, "default.clock.rate", 0);
    if (rate > 0)
        state->defaultClockRate = rate;
}

const pw_core_events coreEvents = {
    .version = PW_VERSION_CORE_EVENTS,
    .info = coreInfo,
    .done = coreDone,
    .error = coreError,
};

int metadataProperty(void* data, uint32_t, const char* key, const char*, const char* value)
{
    auto* state = static_cast<EnumState*>(data);
    if (key != nullptr && std::strcmp(key, "default.audio.sink") == 0)
        state->defaultSinkName = parseDefaultSinkName(value);
    return 0;
}

const pw_metadata_events metadataEvents = {
    .version = PW_VERSION_METADATA_EVENTS,
    .property = metadataProperty,
};

void registryGlobal(void* data, uint32_t id, uint32_t, const char* type, uint32_t, const spa_dict* props)
{
    auto* state = static_cast<EnumState*>(data);
    if (type == nullptr)
        return;
    if (std::strcmp(type, PW_TYPE_INTERFACE_Node) == 0)
    {
        const char* mediaClass = dictLookup(props, PW_KEY_MEDIA_CLASS);
        if (mediaClass == nullptr || std::strcmp(mediaClass, "Audio/Sink") != 0)
            return;
        LinuxPipeWireSinkInfo sink;
        sink.nodeId = id;
        sink.name = dictString(props, PW_KEY_NODE_NAME);
        sink.description = dictString(props, PW_KEY_NODE_DESCRIPTION);
        if (sink.description.empty())
            sink.description = dictString(props, PW_KEY_NODE_NICK);
        if (sink.description.empty())
            sink.description = sink.name;
        sink.serial = dictString(props, PW_KEY_OBJECT_SERIAL);
        sink.sampleRate = dictInt(props, "clock.rate", 0);
        sink.priority = dictInt(props, PW_KEY_PRIORITY_SESSION, 0);
        state->sinks.push_back(std::move(sink));
        return;
    }
    if (std::strcmp(type, PW_TYPE_INTERFACE_Metadata) == 0)
    {
        if (state->metadata != nullptr)
            return;
        const char* metadataName = dictLookup(props, PW_KEY_METADATA_NAME);
        if (metadataName == nullptr || std::strcmp(metadataName, "default") != 0)
            return;
        auto* metadata = static_cast<pw_metadata*>(
            pw_registry_bind(state->registry, id, type, PW_VERSION_METADATA, 0));
        if (metadata != nullptr)
        {
            state->metadata = metadata;
            pw_metadata_add_listener(metadata, &state->metadataListener, &metadataEvents, state);
        }
    }
}

const pw_registry_events registryEvents = {
    .version = PW_VERSION_REGISTRY_EVENTS,
    .global = registryGlobal,
};

void destroyEnumState(EnumState& state)
{
    if (state.metadata != nullptr)
    {
        spa_hook_remove(&state.metadataListener);
        pw_proxy_destroy(reinterpret_cast<pw_proxy*>(state.metadata));
        state.metadata = nullptr;
    }
    if (state.registry != nullptr)
    {
        spa_hook_remove(&state.registryListener);
        pw_proxy_destroy(reinterpret_cast<pw_proxy*>(state.registry));
        state.registry = nullptr;
    }
    if (state.core != nullptr)
    {
        spa_hook_remove(&state.coreListener);
        pw_core_disconnect(state.core);
        state.core = nullptr;
    }
    if (state.context != nullptr)
    {
        pw_context_destroy(state.context);
        state.context = nullptr;
    }
    if (state.loop != nullptr)
    {
        pw_main_loop_destroy(state.loop);
        state.loop = nullptr;
    }
}

bool runPipeWireEnumeration(EnumState& state, std::string& error)
{
    ensurePipeWireInit();
    state.loop = pw_main_loop_new(nullptr);
    if (state.loop == nullptr)
    {
        error = "PipeWire main loop create failed";
        return false;
    }
    state.context = pw_context_new(pw_main_loop_get_loop(state.loop), nullptr, 0);
    if (state.context == nullptr)
    {
        error = "PipeWire context create failed";
        return false;
    }
    state.core = pw_context_connect(state.context, nullptr, 0);
    if (state.core == nullptr)
    {
        error = "PipeWire core connect failed";
        return false;
    }
    pw_core_add_listener(state.core, &state.coreListener, &coreEvents, &state);
    state.registry = static_cast<pw_registry*>(pw_core_get_registry(state.core, PW_VERSION_REGISTRY, 0));
    if (state.registry == nullptr)
    {
        error = "PipeWire registry unavailable";
        return false;
    }
    pw_registry_add_listener(state.registry, &state.registryListener, &registryEvents, &state);
    state.pendingSeq = pw_core_sync(state.core, PW_ID_CORE, 0);

    auto* loop = pw_main_loop_get_loop(state.loop);
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(2);
    while (!state.done && std::chrono::steady_clock::now() < deadline)
    {
        const int iterated = pw_loop_iterate(loop, 50);
        if (iterated < 0)
            break;
    }
    if (!state.error.empty())
    {
        error = state.error;
        return false;
    }
    if (!state.done)
    {
        error = "PipeWire registry sync timed out";
        return false;
    }
    return true;
}
}

bool linuxPipeWireRuntimeAvailable()
{
    std::string error;
    EnumState state;
    const bool ok = runPipeWireEnumeration(state, error);
    destroyEnumState(state);
    return ok;
}

int linuxPipeWireDefaultClockRate()
{
    std::string error;
    EnumState state;
    if (!runPipeWireEnumeration(state, error))
    {
        destroyEnumState(state);
        return 48000;
    }
    const int rate = state.defaultClockRate > 0 ? state.defaultClockRate : 48000;
    destroyEnumState(state);
    return rate;
}

std::vector<LinuxPipeWireSinkInfo> enumerateLinuxPipeWireSinks(std::string* error)
{
    EnumState state;
    std::string localError;
    if (!runPipeWireEnumeration(state, localError))
    {
        if (error != nullptr)
            *error = localError;
        destroyEnumState(state);
        return {};
    }
    for (auto& sink : state.sinks)
    {
        if (sink.sampleRate <= 0)
            sink.sampleRate = state.defaultClockRate > 0 ? state.defaultClockRate : 48000;
        sink.isDefault = !state.defaultSinkName.empty() && sink.name == state.defaultSinkName;
    }
    if (std::none_of(state.sinks.begin(), state.sinks.end(), [](const auto& sink) { return sink.isDefault; })
        && !state.sinks.empty())
    {
        auto best = std::max_element(state.sinks.begin(), state.sinks.end(),
            [](const auto& left, const auto& right) { return left.priority < right.priority; });
        best->isDefault = true;
    }
    destroyEnumState(state);
    return state.sinks;
}

struct LinuxPipeWireCallbackState
{
    pw_stream* stream = nullptr;
    PcmRingAudioSource* source = nullptr;
    int channels = 0;
    std::atomic<int> negotiatedRate { 0 };
    std::atomic<bool> streaming { false };
    LinuxOutputFailure failure;
};

namespace
{
void onProcess(void* data)
{
    auto* state = static_cast<LinuxPipeWireCallbackState*>(data);
    if (state == nullptr || state->source == nullptr || state->stream == nullptr || state->failure.failed())
        return;
    pw_buffer* buffer = pw_stream_dequeue_buffer(state->stream);
    if (buffer == nullptr || buffer->buffer == nullptr || buffer->buffer->n_datas == 0)
        return;
    spa_data& data0 = buffer->buffer->datas[0];
    if (data0.data == nullptr)
    {
        pw_stream_queue_buffer(state->stream, buffer);
        return;
    }
    const int channels = std::max(1, state->channels);
    uint32_t frames = data0.maxsize / static_cast<uint32_t>(sizeof(float) * static_cast<uint32_t>(channels));
    if (buffer->requested > 0)
        frames = std::min(frames, static_cast<uint32_t>(buffer->requested));
    if (frames == 0)
    {
        pw_stream_queue_buffer(state->stream, buffer);
        return;
    }
    auto* dst = static_cast<float*>(data0.data);
    pcm_detail::configureAudioCallbackThread();
    state->source->renderInterleaved(dst, frames, static_cast<uint32_t>(channels));
    data0.chunk->offset = 0;
    data0.chunk->stride = static_cast<int32_t>(sizeof(float) * channels);
    data0.chunk->size = frames * sizeof(float) * static_cast<uint32_t>(channels);
    pw_stream_queue_buffer(state->stream, buffer);
}

void onStateChanged(void* data, pw_stream_state previous, pw_stream_state state, const char*)
{
    auto* streamState = static_cast<LinuxPipeWireCallbackState*>(data);
    if (streamState == nullptr)
        return;
    if (state == PW_STREAM_STATE_STREAMING || state == PW_STREAM_STATE_PAUSED)
        streamState->streaming.store(true, std::memory_order_release);
    if (state == PW_STREAM_STATE_ERROR)
    {
        streamState->failure.record(EIO);
    }
    else if (state == PW_STREAM_STATE_UNCONNECTED && previous != PW_STREAM_STATE_UNCONNECTED)
        streamState->failure.record(ENODEV);
}

void onParamChanged(void* data, uint32_t id, const spa_pod* param)
{
    auto* streamState = static_cast<LinuxPipeWireCallbackState*>(data);
    if (streamState == nullptr || param == nullptr || id != SPA_PARAM_Format)
        return;
    spa_audio_info_raw info {};
    if (spa_format_audio_raw_parse(param, &info) == 0 && info.rate > 0)
        streamState->negotiatedRate.store(static_cast<int>(info.rate), std::memory_order_release);
}

const pw_stream_events streamEvents = {
    .version = PW_VERSION_STREAM_EVENTS,
    .state_changed = onStateChanged,
    .param_changed = onParamChanged,
    .process = onProcess,
};

std::string extractPipeWireTarget(const char* deviceId)
{
    if (deviceId == nullptr || deviceId[0] == '\0')
        return {};
    const char prefix[] = "shared:pw:";
    const std::string id(deviceId);
    if (id.rfind(prefix, 0) == 0)
        return id.substr(std::strlen(prefix));
    return {};
}

std::string resolvePipeWireTarget(const char* requestedDevice, int deviceIndex, const char* deviceId)
{
    std::string target = extractPipeWireTarget(deviceId);
    if (!target.empty())
        return target;

    std::string enumError;
    const auto sinks = enumerateLinuxPipeWireSinks(&enumError);
    auto matches = [&](const LinuxPipeWireSinkInfo& sink, const char* value) {
        if (value == nullptr || value[0] == '\0')
            return false;
        return nameMatches(sink.name, value)
            || nameMatches(sink.description, value)
            || nameMatches(sink.serial, value);
    };
    if (deviceId != nullptr)
    {
        for (const auto& sink : sinks)
        {
            if (matches(sink, deviceId))
                return sink.name;
        }
    }
    if (requestedDevice != nullptr && requestedDevice[0] != '\0')
    {
        for (const auto& sink : sinks)
        {
            if (matches(sink, requestedDevice))
                return sink.name;
        }
        if (deviceIndex >= 0 && deviceIndex < static_cast<int>(sinks.size()))
            return sinks[static_cast<size_t>(deviceIndex)].name;
        return requestedDevice;
    }
    if (deviceIndex >= 0 && deviceIndex < static_cast<int>(sinks.size()))
        return sinks[static_cast<size_t>(deviceIndex)].name;
    return {};
}
}

#else

struct LinuxPipeWireCallbackState
{
};

bool linuxPipeWireRuntimeAvailable()
{
    return false;
}

int linuxPipeWireDefaultClockRate()
{
    return 48000;
}

std::vector<LinuxPipeWireSinkInfo> enumerateLinuxPipeWireSinks(std::string* error)
{
    if (error != nullptr)
        *error = "PipeWire support was not built into echo-audio-host";
    return {};
}

#endif

LinuxPipeWireSharedOutput::LinuxPipeWireSharedOutput() = default;

LinuxPipeWireSharedOutput::~LinuxPipeWireSharedOutput()
{
    close();
}

#if defined(ECHO_ENABLE_PIPEWIRE) && ECHO_ENABLE_PIPEWIRE

bool LinuxPipeWireSharedOutput::open(
    PcmRingAudioSource& source,
    const char* requestedDevice,
    int deviceIndex,
    const char* deviceId,
    int sampleRate,
    int requestedChannels,
    int bufferFrames,
    std::string& error)
{
    close();
    ensurePipeWireInit();
    channels = std::max(1, std::min(8, requestedChannels));
    configuredRate = std::max(8000, sampleRate);
    const std::string target = resolvePipeWireTarget(requestedDevice, deviceIndex, deviceId);

    loop = pw_thread_loop_new("echo-pipewire-shared", nullptr);
    if (loop == nullptr)
    {
        error = "PipeWire thread loop create failed";
        return false;
    }
    if (pw_thread_loop_start(loop) < 0)
    {
        error = "PipeWire thread loop start failed";
        close();
        return false;
    }

    pw_thread_loop_lock(loop);
    auto* props = pw_properties_new(
        PW_KEY_MEDIA_TYPE, "Audio",
        PW_KEY_MEDIA_CATEGORY, "Playback",
        PW_KEY_MEDIA_ROLE, "Music",
        PW_KEY_APP_NAME, "ECHO",
        PW_KEY_NODE_NAME, "echo.playback",
        PW_KEY_NODE_DESCRIPTION, "ECHO",
        nullptr);
    if (!target.empty())
        pw_properties_set(props, PW_KEY_TARGET_OBJECT, target.c_str());
    if (bufferFrames > 0)
        pw_properties_setf(props, PW_KEY_NODE_LATENCY, "%d/%d", bufferFrames, configuredRate);

    callbackState = std::make_unique<LinuxPipeWireCallbackState>();
    callbackState->source = &source;
    callbackState->channels = channels;
    callbackState->negotiatedRate.store(configuredRate, std::memory_order_release);

    stream = pw_stream_new_simple(
        pw_thread_loop_get_loop(loop),
        "ECHO",
        props,
        &streamEvents,
        callbackState.get());
    if (stream != nullptr)
        callbackState->stream = stream;
    if (stream == nullptr)
    {
        pw_thread_loop_unlock(loop);
        error = "PipeWire stream create failed";
        close();
        return false;
    }

    spa_audio_info_raw info {};
    info.format = SPA_AUDIO_FORMAT_F32;
    info.rate = static_cast<uint32_t>(configuredRate);
    info.channels = static_cast<uint32_t>(channels);
    if (channels == 1)
        info.position[0] = SPA_AUDIO_CHANNEL_MONO;
    else
    {
        info.position[0] = SPA_AUDIO_CHANNEL_FL;
        info.position[1] = SPA_AUDIO_CHANNEL_FR;
    }
    uint8_t buffer[1024];
    spa_pod_builder builder = SPA_POD_BUILDER_INIT(buffer, sizeof(buffer));
    const spa_pod* params[] = {
        spa_format_audio_raw_build(&builder, SPA_PARAM_EnumFormat, &info),
    };
    const int connectResult = pw_stream_connect(
        stream,
        PW_DIRECTION_OUTPUT,
        PW_ID_ANY,
        static_cast<pw_stream_flags>(
            PW_STREAM_FLAG_AUTOCONNECT | PW_STREAM_FLAG_MAP_BUFFERS | PW_STREAM_FLAG_RT_PROCESS),
        params,
        1);
    pw_thread_loop_unlock(loop);
    if (connectResult < 0)
    {
        error = "PipeWire stream connect failed";
        close();
        return false;
    }

    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(2);
    while (!callbackState->streaming.load(std::memory_order_acquire)
        && !callbackState->failure.failed()
        && std::chrono::steady_clock::now() < deadline)
        std::this_thread::sleep_for(std::chrono::milliseconds(10));
    if (callbackState->failure.failed())
    {
        error = "PipeWire stream failed or disconnected";
        close();
        return false;
    }
    if (!callbackState->streaming.load(std::memory_order_acquire))
    {
        error = "PipeWire stream did not enter streaming state";
        close();
        return false;
    }

    ringSource = &source;
    running = true;
    const int actualRate = callbackState->negotiatedRate.load(std::memory_order_acquire);
    ready.sampleRate = actualRate > 0 ? actualRate : configuredRate;
    ready.hardwareSampleRate = ready.sampleRate;
    ready.channels = channels;
    ready.bufferFrameCount = std::max(1, bufferFrames);
    ready.format = "f32";
    ready.deviceName = requestedDevice != nullptr && requestedDevice[0] != '\0' ? requestedDevice : target;
    ready.nodeName = target;
    source.prepareForNativeRender(std::max(64, bufferFrames), static_cast<double>(ready.sampleRate));
    return true;
}

int LinuxPipeWireSharedOutput::takeFailure() noexcept
{
    return callbackState ? callbackState->failure.take() : 0;
}

void LinuxPipeWireSharedOutput::close() noexcept
{
    running = false;
    ringSource = nullptr;
    if (loop != nullptr)
    {
        pw_thread_loop_lock(loop);
        if (stream != nullptr)
        {
            pw_stream_disconnect(stream);
            pw_stream_destroy(stream);
            stream = nullptr;
        }
        if (callbackState)
            callbackState->stream = nullptr;
        pw_thread_loop_unlock(loop);
        pw_thread_loop_stop(loop);
        pw_thread_loop_destroy(loop);
        loop = nullptr;
    }
    callbackState.reset();
    ready = {};
}

#else

bool LinuxPipeWireSharedOutput::open(
    PcmRingAudioSource&,
    const char*,
    int,
    const char*,
    int,
    int,
    int,
    std::string& error)
{
    error = "PipeWire support was not built into echo-audio-host";
    return false;
}

int LinuxPipeWireSharedOutput::takeFailure() noexcept
{
    return 0;
}

void LinuxPipeWireSharedOutput::close() noexcept
{
    callbackState.reset();
}

#endif

#endif
