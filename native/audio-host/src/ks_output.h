#pragma once

#ifdef _WIN32

#include <stddef.h>
#include <stdint.h>

typedef unsigned int (*ks_render_callback)(
    void* userData,
    float* output,
    unsigned int frameCount,
    unsigned int channels);

typedef struct ks_device_info {
    wchar_t id[1024];
    char name[512];
    uint32_t renderPinCount;
} ks_device_info;

typedef struct ks_ready_info {
    uint32_t sampleRate;
    uint32_t channels;
    uint32_t bufferFrameCount;
    char format[32];
    char deviceName[512];
} ks_ready_info;

typedef struct ks_runtime ks_runtime;

int ks_output_list_devices(ks_device_info** outDevices, uint32_t* outCount);
void ks_output_free_devices(ks_device_info* devices);

int ks_output_start(
    const char* targetDeviceName,
    int targetDeviceIndex,
    uint32_t sampleRate,
    uint32_t channels,
    uint32_t requestedBufferFrames,
    ks_render_callback callback,
    void* userData,
    ks_runtime** outRuntime,
    ks_ready_info* outInfo,
    char* error,
    size_t errorLen);

void ks_output_stop(ks_runtime* runtime);

#endif
