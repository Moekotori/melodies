#if defined(__linux__)

#include "LinuxAlsaNativeDsdOutput.h"
#include "LinuxAlsaPcmWrite.h"
#include "LinuxAlsaDevicePolicy.h"

#include <alsa/asoundlib.h>

#include <algorithm>
#include <cstdio>
#include <cstring>
#include <thread>

namespace
{
struct AlsaPcmWriteOperations
{
    snd_pcm_t* pcm;
    long write(const std::uint8_t* data, long frames) { return snd_pcm_writei(pcm, data, frames); }
    int wait(int milliseconds) { return snd_pcm_wait(pcm, milliseconds); }
    int prepare() { return snd_pcm_prepare(pcm); }
    int resume() { return snd_pcm_resume(pcm); }
    void pause(int milliseconds) { std::this_thread::sleep_for(std::chrono::milliseconds(milliseconds)); }
};

constexpr int kDsdBitsPerAlsaFrame = 32;
constexpr int kBytesPerAlsaSample = 4;
constexpr unsigned kDsdU32CandidateRates[] {
    88'200, 96'000, 176'400, 192'000, 352'800, 384'000, 705'600, 768'000,
};

std::string hwPcmFromAlsaId(const char* alsaId)
{
    if (alsaId == nullptr || alsaId[0] == '\0')
        return {};
    std::string id(alsaId);
    if (id.rfind("exclusive:", 0) == 0)
        id = id.substr(10);
    if (!id.empty() && id.front() == ':')
        return "hw" + id;
    if (id.rfind("hw:", 0) == 0)
        return id;
    return {};
}

std::string asciiLower(std::string value)
{
    std::transform(value.begin(), value.end(), value.begin(), [](unsigned char ch) {
        return static_cast<char>(std::tolower(ch));
    });
    return value;
}

bool nameMatches(const std::string& candidate, const std::string& requested)
{
    if (requested.empty())
        return false;
    const auto left = asciiLower(candidate);
    const auto right = asciiLower(requested);
    return left == right || left.find(right) != std::string::npos || right.find(left) != std::string::npos;
}

std::string resolveHwPcm(const char* requestedDevice, int deviceIndex, const char* deviceId)
{
    const auto fromId = hwPcmFromAlsaId(deviceId);
    if (!fromId.empty())
        return fromId;

    int card = -1;
    int matchedIndex = 0;
    while (snd_card_next(&card) == 0 && card >= 0)
    {
        char cardName[64] {};
        std::snprintf(cardName, sizeof(cardName), "hw:%d", card);
        snd_ctl_t* ctl = nullptr;
        if (snd_ctl_open(&ctl, cardName, 0) < 0)
            continue;
        snd_ctl_card_info_t* cardInfo = nullptr;
        snd_ctl_card_info_alloca(&cardInfo);
        if (snd_ctl_card_info(ctl, cardInfo) < 0)
        {
            snd_ctl_close(ctl);
            continue;
        }
        const char* cardId = snd_ctl_card_info_get_id(cardInfo);
        const char* cardLongName = snd_ctl_card_info_get_name(cardInfo);
        int device = -1;
        while (snd_ctl_pcm_next_device(ctl, &device) == 0 && device >= 0)
        {
            snd_pcm_info_t* pcmInfo = nullptr;
            snd_pcm_info_alloca(&pcmInfo);
            snd_pcm_info_set_device(pcmInfo, static_cast<unsigned>(device));
            snd_pcm_info_set_subdevice(pcmInfo, 0);
            snd_pcm_info_set_stream(pcmInfo, SND_PCM_STREAM_PLAYBACK);
            if (snd_ctl_pcm_info(ctl, pcmInfo) < 0)
                continue;
            const char* pcmName = snd_pcm_info_get_name(pcmInfo);
            std::string combined;
            if (cardId != nullptr && pcmName != nullptr)
                combined = std::string(cardId) + ", " + pcmName;
            else if (cardLongName != nullptr && pcmName != nullptr)
                combined = std::string(cardLongName) + ", " + pcmName;
            const bool matches = (requestedDevice != nullptr && requestedDevice[0] != '\0')
                && (nameMatches(combined, requestedDevice)
                    || nameMatches(cardId != nullptr ? cardId : "", requestedDevice)
                    || nameMatches(cardLongName != nullptr ? cardLongName : "", requestedDevice)
                    || nameMatches(pcmName != nullptr ? pcmName : "", requestedDevice));
            if (matches || (requestedDevice == nullptr && deviceIndex == matchedIndex))
            {
                snd_ctl_close(ctl);
                return "hw:" + std::to_string(card) + "," + std::to_string(device);
            }
            ++matchedIndex;
        }
        snd_ctl_close(ctl);
    }
    if (deviceIndex >= 0)
        return "hw:" + std::to_string(deviceIndex) + ",0";
    return "hw:0,0";
}
}

void packAlsaDsdU32Be(
    const std::uint8_t* interleavedByteFrames,
    int channels,
    int byteFrameCount,
    std::uint8_t* packedU32Be)
{
    if (interleavedByteFrames == nullptr || packedU32Be == nullptr || channels <= 0 || byteFrameCount <= 0)
        return;
    const int alsaFrames = byteFrameCount / kBytesPerAlsaSample;
    for (int frame = 0; frame < alsaFrames; ++frame)
    {
        for (int channel = 0; channel < channels; ++channel)
        {
            const std::uint8_t* src =
                interleavedByteFrames + (static_cast<size_t>(frame) * kBytesPerAlsaSample * channels) + channel;
            std::uint8_t* dst = packedU32Be + (static_cast<size_t>(frame) * channels + channel) * kBytesPerAlsaSample;
            dst[0] = src[0 * channels];
            dst[1] = src[1 * channels];
            dst[2] = src[2 * channels];
            dst[3] = src[3 * channels];
        }
    }
}

std::vector<int> probeLinuxAlsaNativeDsdBitRates(const char* alsaId)
{
    std::vector<int> rates;
    const auto pcmName = hwPcmFromAlsaId(alsaId);
    if (pcmName.empty())
        return rates;
    snd_pcm_t* pcm = nullptr;
    if (snd_pcm_open(&pcm, pcmName.c_str(), SND_PCM_STREAM_PLAYBACK, SND_PCM_NONBLOCK) < 0)
        return rates;
    snd_pcm_hw_params_t* params = nullptr;
    snd_pcm_hw_params_alloca(&params);
    if (snd_pcm_hw_params_any(pcm, params) == 0
        && snd_pcm_hw_params_set_format(pcm, params, SND_PCM_FORMAT_DSD_U32_BE) == 0)
    {
        for (const unsigned candidate : kDsdU32CandidateRates)
        {
            if (snd_pcm_hw_params_test_rate(pcm, params, candidate, 0) == 0)
                rates.push_back(static_cast<int>(candidate) * kDsdBitsPerAlsaFrame);
        }
    }
    snd_pcm_close(pcm);
    return rates;
}

LinuxAlsaNativeDsdOutput::~LinuxAlsaNativeDsdOutput()
{
    close();
}

bool LinuxAlsaNativeDsdOutput::open(
    NativeDsdRingSource& source,
    const char* requestedDevice,
    int deviceIndex,
    const char* deviceId,
    int nativeBitRate,
    int requestedChannels,
    int bufferFrames,
    std::string& error)
{
    close();
    if (nativeBitRate <= 0 || nativeBitRate % kDsdBitsPerAlsaFrame != 0)
    {
        error = "ALSA exclusive native DSD sample rate must be a multiple of 32";
        return false;
    }
    const unsigned alsaRate = static_cast<unsigned>(nativeBitRate / kDsdBitsPerAlsaFrame);
    channels = std::max(1, std::min(2, requestedChannels));
    const auto pcmName = resolveHwPcm(requestedDevice, deviceIndex, deviceId);
    int err = openLinuxAlsaAfterIdleRelease([&] {
        return snd_pcm_open(&pcm, pcmName.c_str(), SND_PCM_STREAM_PLAYBACK, SND_PCM_NONBLOCK);
    }, [](int ms) {
        std::this_thread::sleep_for(std::chrono::milliseconds(ms));
    }, -EBUSY);
    if (err < 0)
    {
        error = std::string("ALSA exclusive native DSD open failed: ") + snd_strerror(err) + " (" + pcmName + ")";
        if (err == -EBUSY)
            error = "alsa_exclusive_device_busy: Device or resource busy; release this output in other audio applications and retry";
        pcm = nullptr;
        return false;
    }

    snd_pcm_hw_params_t* hw = nullptr;
    snd_pcm_hw_params_alloca(&hw);
    snd_pcm_hw_params_any(pcm, hw);
    snd_pcm_hw_params_set_access(pcm, hw, SND_PCM_ACCESS_RW_INTERLEAVED);
    err = snd_pcm_hw_params_set_format(pcm, hw, SND_PCM_FORMAT_DSD_U32_BE);
    if (err < 0)
    {
        error = std::string("ALSA exclusive native DSD requires DSD_U32_BE: ") + snd_strerror(err);
        close();
        return false;
    }
    err = snd_pcm_hw_params_set_channels(pcm, hw, static_cast<unsigned>(channels));
    if (err < 0)
    {
        error = std::string("ALSA exclusive native DSD channel count rejected: ") + snd_strerror(err);
        close();
        return false;
    }
    unsigned actualRate = alsaRate;
    err = snd_pcm_hw_params_set_rate_near(pcm, hw, &actualRate, nullptr);
    if (err < 0 || actualRate != alsaRate)
    {
        error = "ALSA exclusive native DSD rate unsupported: " + std::to_string(alsaRate);
        close();
        return false;
    }
    snd_pcm_uframes_t period = static_cast<snd_pcm_uframes_t>(std::max(64, bufferFrames > 0 ? bufferFrames : 256));
    snd_pcm_hw_params_set_period_size_near(pcm, hw, &period, nullptr);
    snd_pcm_uframes_t buffer = period * 4;
    snd_pcm_hw_params_set_buffer_size_near(pcm, hw, &buffer);
    err = snd_pcm_hw_params(pcm, hw);
    if (err < 0)
    {
        error = std::string("ALSA exclusive native DSD hw_params failed: ") + snd_strerror(err);
        close();
        return false;
    }
    snd_pcm_hw_params_current(pcm, hw);
    snd_pcm_hw_params_get_period_size(hw, &period, nullptr);
    periodFrames = static_cast<int>(std::max<snd_pcm_uframes_t>(64, period));

    snd_pcm_sw_params_t* sw = nullptr;
    snd_pcm_sw_params_alloca(&sw);
    snd_pcm_sw_params_current(pcm, sw);
    snd_pcm_sw_params_set_start_threshold(pcm, sw, static_cast<snd_pcm_uframes_t>(periodFrames));
    snd_pcm_sw_params_set_avail_min(pcm, sw, static_cast<snd_pcm_uframes_t>(periodFrames));
    snd_pcm_sw_params(pcm, sw);

    err = snd_pcm_nonblock(pcm, 1);
    if (err < 0)
    {
        error = "ALSA native DSD nonblocking setup failed";
        close();
        return false;
    }

    byteScratch.assign(static_cast<size_t>(periodFrames) * kBytesPerAlsaSample * channels, 0x69u);
    packedScratch.assign(static_cast<size_t>(periodFrames) * channels * kBytesPerAlsaSample, 0x69u);
    ringSource = &source;
    ready.sampleRate = nativeBitRate;
    ready.hardwareSampleRate = nativeBitRate;
    ready.channels = channels;
    ready.bufferFrameCount = periodFrames;
    ready.format = "dsd_u32_be";
    ready.deviceName = requestedDevice != nullptr && requestedDevice[0] != '\0' ? requestedDevice : pcmName;
    ready.pcmName = pcmName;
    running.store(true, std::memory_order_release);
    renderThread = std::thread([this] { renderLoop(); });
    return true;
}

void LinuxAlsaNativeDsdOutput::renderLoop()
{
    AlsaPcmWriteOperations operations { pcm };
    while (running.load(std::memory_order_acquire))
    {
        const int byteFrames = periodFrames * kBytesPerAlsaSample;
        std::memset(byteScratch.data(), 0x69, byteScratch.size());
        ringSource->renderInterleaved(byteScratch.data(), static_cast<uint32_t>(byteFrames), static_cast<uint32_t>(channels));
        packAlsaDsdU32Be(byteScratch.data(), channels, byteFrames, packedScratch.data());
        const int result = writeLinuxAlsaPcmBlock(operations, packedScratch.data(), periodFrames,
            channels * kBytesPerAlsaSample, running);
        if (result < 0)
        {
            if (result != -ECANCELED) failure.record(-result);
            break;
        }
    }
}

void LinuxAlsaNativeDsdOutput::close() noexcept
{
    running.store(false, std::memory_order_release);
    if (renderThread.joinable()) renderThread.join();
    if (pcm != nullptr)
    {
        snd_pcm_drop(pcm);
        snd_pcm_close(pcm);
        pcm = nullptr;
    }
    ringSource = nullptr;
    ready = {};
    failure.reset();
    std::vector<std::uint8_t>().swap(byteScratch);
    std::vector<std::uint8_t>().swap(packedScratch);
}

#endif
