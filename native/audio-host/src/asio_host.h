#pragma once

#ifdef _WIN32

#include <stddef.h>
#include <stdint.h>

typedef unsigned int (*asio_render_callback)(
    void* userData,
    float* output,
    unsigned int frameCount,
    unsigned int channels);

typedef unsigned int (*asio_dop_render_callback)(
    void* userData,
    uint32_t* output,
    unsigned int frameCount,
    unsigned int channels);

typedef unsigned int (*asio_native_dsd_render_callback)(
    void* userData,
    uint8_t* output,
    unsigned int byteFrameCount,
    unsigned int channels);

typedef struct asio_device_info {
    char name[512];
    int isDefault;
    uint32_t outputChannels;
    char outputChannelNames[1024];
} asio_device_info;

typedef struct asio_ready_info {
    uint32_t sampleRate;
    uint32_t channels;
    uint32_t bufferFrameCount;
    uint32_t requestedBufferFrameCount;
    uint32_t inputChannels;
    uint32_t outputChannels;
    uint32_t minBufferFrames;
    uint32_t maxBufferFrames;
    uint32_t preferredBufferFrames;
    int32_t granularity;
    uint32_t outputChannelStart;
    char format[64];
    char deviceName[512];
} asio_ready_info;

typedef struct asio_runtime asio_runtime;

enum asio_runtime_event_flag {
    ASIO_RUNTIME_EVENT_NONE = 0,
    ASIO_RUNTIME_EVENT_RESET_REQUEST = 1u << 0,
    ASIO_RUNTIME_EVENT_RESYNC_REQUEST = 1u << 1,
    ASIO_RUNTIME_EVENT_LATENCIES_CHANGED = 1u << 2,
    ASIO_RUNTIME_EVENT_SAMPLE_RATE_CHANGED = 1u << 3,
    ASIO_RUNTIME_EVENT_RENDER_FAILED = 1u << 4,
};

typedef struct asio_runtime_events {
    uint32_t flags;
    uint32_t sampleRate;
} asio_runtime_events;

int asio_list_devices(asio_device_info** outDevices, uint32_t* outCount);
void asio_free_devices(asio_device_info* devices);

int asio_probe_sample_rates(
    const char* targetDeviceName,
    int targetDeviceIndex,
    const uint32_t* sampleRates,
    uint32_t sampleRateCount,
    int nativeDsdMode,
    uint8_t* supported,
    char* error,
    size_t errorLen);

int asio_start(
    const char* targetDeviceName,
    int targetDeviceIndex,
    uint32_t requestedSampleRate,
    uint32_t sourceChannels,
    uint32_t requestedBufferFrames,
    uint32_t outputChannelStart,
    int exactPcmPassthrough,
    asio_render_callback callback,
    void* userData,
    asio_runtime** outRuntime,
    asio_ready_info* outInfo,
    char* error,
    size_t errorLen);

int asio_start_dop(
    const char* targetDeviceName,
    int targetDeviceIndex,
    uint32_t requestedSampleRate,
    uint32_t sourceChannels,
    uint32_t requestedBufferFrames,
    uint32_t outputChannelStart,
    asio_dop_render_callback callback,
    void* userData,
    asio_runtime** outRuntime,
    asio_ready_info* outInfo,
    char* error,
    size_t errorLen);

int asio_start_native_dsd(
    const char* targetDeviceName,
    int targetDeviceIndex,
    uint32_t requestedSampleRate,
    uint32_t sourceChannels,
    uint32_t requestedBufferFrames,
    uint32_t outputChannelStart,
    asio_native_dsd_render_callback callback,
    void* userData,
    asio_runtime** outRuntime,
    asio_ready_info* outInfo,
    char* error,
    size_t errorLen);

int asio_open_control_panel(
    const char* targetDeviceName,
    int targetDeviceIndex,
    char* error,
    size_t errorLen);

void asio_stop(asio_runtime* runtime);
int asio_render_failed(asio_runtime* runtime);
int asio_poll_runtime_events(asio_runtime* runtime, asio_runtime_events* outEvents);

#ifdef ECHO_AUDIO_ENGINE_TESTS
uint32_t asio_build_buffer_candidates_for_tests(
    long minSize,
    long maxSize,
    long preferredSize,
    long granularity,
    uint32_t requestedBufferFrames,
    uint32_t* outCandidates,
    uint32_t maxCandidates);

uint32_t asio_build_buffer_include_input_attempts_for_tests(
    long inputChannelCount,
    int dopMode,
    int nativeDsdMode,
    int* outAttempts,
    uint32_t maxAttempts);

const char* asio_error_name_for_tests(long error);

int asio_should_suppress_unsolicited_windows_for_tests(const char* driverName);

uint32_t asio_build_sample_rate_pivot_candidates_for_tests(
    double requestedSampleRate,
    uint32_t* outCandidates,
    uint32_t maxCandidates);

void asio_write_sample_for_tests(
    void* buffer,
    long sampleType,
    long frameIndex,
    float sample);
void asio_write_exact_sample_for_tests(
    void* buffer,
    long sampleType,
    long frameIndex,
    float sample);

void asio_write_dop_sample_for_tests(
    void* buffer,
    long sampleType,
    long frameIndex,
    uint32_t sample24);

void asio_write_native_dsd_samples_for_tests(
    void* buffer,
    long sampleType,
    uint32_t frameCount,
    const uint8_t* source,
    uint32_t sourceByteFrames,
    uint32_t sourceChannels,
    uint32_t sourceChannel,
    int forcePackedMsb);

int asio_prime_native_dsd_buffers_for_tests(
    void* firstBuffer,
    void* secondBuffer,
    long sampleType,
    uint32_t frameCount,
    int forcePackedMsb);

int asio_render_guard_catches_exception_for_tests(void);
int asio_wait_for_callbacks_for_tests(int activeCallbacks, uint32_t maxAttempts);
uint32_t asio_capture_message_event_for_tests(long selector);
uint32_t asio_capture_sample_rate_event_for_tests(double sampleRate, uint32_t* outSampleRate);
#endif

#endif
