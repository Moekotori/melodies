#pragma once

#include <algorithm>
#include <cctype>
#include <string>
#include <string_view>
#include <vector>

struct DeviceDescriptor
{
    int index = -1;
    std::string typeName;
    std::string name;
    int sampleRate = 0;
    int sharedSampleRate = 0;
    bool isDefault = false;
    bool isAsio = false;
    int asioOutputChannels = 0;
    std::string asioOutputChannelNames;
    std::string stableId;
    std::string connectionType = "unknown";
    std::string formFactor = "unknown";
    std::vector<int> nativeDsdSampleRates;
};

enum class DeviceListMode
{
    Shared,
    Exclusive,
    Asio,
    KernelStreaming,
};

inline std::string asciiLower(std::string_view value)
{
    std::string lowered(value);
    std::transform(lowered.begin(), lowered.end(), lowered.begin(), [] (unsigned char ch) {
        return static_cast<char>(std::tolower(ch));
    });
    return lowered;
}

inline bool containsIgnoreCase(std::string_view text, std::string_view needle)
{
    if (needle.empty())
        return true;
    return asciiLower(text).find(asciiLower(needle)) != std::string::npos;
}

inline bool isAsioType(std::string_view typeName)
{
    return containsIgnoreCase(typeName, "asio");
}

inline bool isExclusiveType(std::string_view typeName)
{
    return containsIgnoreCase(typeName, "exclusive");
}

inline bool isKernelStreamingType(std::string_view typeName)
{
    return containsIgnoreCase(typeName, "kernel streaming")
        || containsIgnoreCase(typeName, "wdm-ks");
}

inline bool isAlsaType(std::string_view typeName)
{
    return containsIgnoreCase(typeName, "alsa");
}

inline bool isPipeWireType(std::string_view typeName)
{
    return containsIgnoreCase(typeName, "pipewire");
}

inline bool isJackType(std::string_view typeName)
{
    return containsIgnoreCase(typeName, "jack");
}

inline bool isPreferredSharedType(std::string_view typeName)
{
#ifndef _WIN32
    return ! isExclusiveType(typeName) && (isPipeWireType(typeName) || isAlsaType(typeName));
#else
    return ! isExclusiveType(typeName)
        && (containsIgnoreCase(typeName, "windows audio")
            || containsIgnoreCase(typeName, "wasapi"));
#endif
}

inline bool isDirectSoundType(std::string_view typeName)
{
    return containsIgnoreCase(typeName, "directsound");
}

inline int sharedTypePriority(std::string_view typeName)
{
#ifndef _WIN32
    if (isPipeWireType(typeName))
        return 0;

    if (isAlsaType(typeName))
        return 1;

    if (containsIgnoreCase(typeName, "shared"))
        return 2;

    if (isJackType(typeName))
        return 3;

    return 4;
#else
    if (containsIgnoreCase(typeName, "shared"))
        return 0;

    if (containsIgnoreCase(typeName, "windows audio") || containsIgnoreCase(typeName, "wasapi"))
        return 1;

    if (containsIgnoreCase(typeName, "directsound"))
        return 2;

    return 3;
#endif
}

inline bool shouldIncludeType(std::string_view typeName, DeviceListMode mode)
{
    const bool asioType = isAsioType(typeName);
    const bool kernelStreamingType = isKernelStreamingType(typeName);
    const bool exclusiveType = isExclusiveType(typeName);

    if (mode == DeviceListMode::Asio)
        return asioType;

    if (mode == DeviceListMode::KernelStreaming)
        return kernelStreamingType;

    if (asioType || kernelStreamingType)
        return false;

    if (mode == DeviceListMode::Exclusive)
        return exclusiveType;

    return ! exclusiveType;
}

inline bool shouldIncludeSharedBackendType(std::string_view typeName, std::string_view sharedBackend)
{
    if (sharedBackend == "alsa")
        return isAlsaType(typeName);

    if (sharedBackend == "pipewire")
        return isPipeWireType(typeName);

    if (sharedBackend == "windows")
#ifdef _WIN32
        return isPreferredSharedType(typeName);
#else
        return false;
#endif

    if (sharedBackend == "directsound")
        return isDirectSoundType(typeName);

    return ! isDirectSoundType(typeName);
}
