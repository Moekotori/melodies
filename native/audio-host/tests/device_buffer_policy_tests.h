#pragma once

inline void testDeviceManagedBufferPolicy()
{
    const auto initial = parseOptions({ "echo-audio-host", "--defer-device-open" });
    Options following;
    std::string error;
    require(configureRuntimeOutputOptions({ {"outputMode", "shared"}, {"bufferSize", 0}, {"latencyProfile", "stable"} },
        initial, following, error), "device-managed configuration accepted");
    require(following.deviceBufferManaged && getDeviceBufferSize(following) == 0,
        "device-managed zero must survive latency defaults");
    requireVectorEquals(buildBufferSizeAttempts(following), { 0 },
        "device-managed opening must not inject fixed-size attempts");
    requireVectorEquals(buildPreferredBufferSizeAttempts(0, 2048), { 0 },
        "cached fixed buffers must not override device-managed opening");
    Options invalid;
    require(!configureRuntimeOutputOptions({ {"bufferSize", -1} }, following, invalid, error),
        "invalid negative buffer must not turn into device management");

    Options fixed;
    require(configureRuntimeOutputOptions({ {"outputMode", "shared"}, {"bufferSize", 2048} }, following, fixed, error),
        "fixed buffer configuration accepted after device-managed mode");
    require(!fixed.deviceBufferManaged && getDeviceBufferSize(fixed) == 2048,
        "explicit profile request must clear device management");
    require(!hasSameRuntimeOutputOptions(following, fixed), "policy change must reopen the device");

#ifdef _WIN32
    Options asio;
    require(configureRuntimeOutputOptions({ {"outputMode", "asio"}, {"bufferSize", 8192} }, fixed, asio, error),
        "ASIO configuration accepted");
    require(asio.deviceBufferManaged && getDeviceBufferSize(asio) == 0,
        "ASIO must prefer the driver even when an old fixed buffer is supplied");
#endif
    const auto commandLine = parseOptions({ "echo-audio-host", "-buffer", "0" });
    require(commandLine.deviceBufferManaged && getDeviceBufferSize(commandLine) == 0,
        "one-shot CLI preserves explicit device management");
    require(getDeviceBufferSize(initial) == 256, "unspecified one-shot buffers retain the previous default");
}
