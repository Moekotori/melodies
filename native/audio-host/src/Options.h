#pragma once

#include <string>
#include <cstdint>

struct Options
{
    bool list = false;
    bool asio = false;
    bool kernelStreaming = false;
    bool exclusive = false;
    bool dopOutput = false;
    bool asioNativeDsdOutput = false;
    bool mqaPcmPassthroughRequested = false;
    int sampleRate = 44100;
    int channels = 2;
    int deviceIndex = -1;
    int bufferSize = 0;
    bool deviceBufferManaged = false;
    int asioOutputChannelStart = 0;
    int fifoCapacityMs = 0;
    int startupPrebufferMs = 0;
    int startupPrebufferTimeoutMs = 0;
    bool startupPrebufferMsSpecified = false;
    bool startupPrebufferTimeoutMsSpecified = false;
    bool decodePcm = false;
    bool cddaToc = false;
    bool cddaEject = false;
    bool cddaRead = false;
    bool cddaCapabilities = false;
    bool cddaStatus = false;
    bool asioSdmFamilyProbe = false;
    bool buildCapabilities = false;
    int cddaTrack = 0;
    int cddaLastTrack = 0;
    int cddaExpectedStartLba = -1;
    int cddaExpectedEndLba = -1;
    double decodeStartSeconds = 0.0;
    int eqControlPort = 0;
    int rpcStdinFd = -1;
    int rpcStdoutFd = -1;
    int pcmInputFd = -1;
    uint64_t pcmWireEpoch = 0;
    int listenTogetherOutputFd = -1;
    int listenTogetherInputFd = -1;
    bool noStdin = false;
    bool deviceOpenDeferred = false;
    bool channelInitializationDeferred = false;
    bool proDspAllowed = false;
    double volume = 1.0;
    std::string deviceName;
    std::string deviceId;
    std::string decodeFile;
    std::string cddaDrive;
    std::string sharedBackend = "auto";
};
