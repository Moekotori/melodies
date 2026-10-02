#include "LinuxAlsaDevicePolicy.h"

#include <alsa/asoundlib.h>
#include <charconv>
#include <string>

bool isLinuxAlsaPlaybackDevice(std::string_view id)
{
    if (!isLinuxAlsaHardwareDevice(id)) return false;
    id.remove_prefix(id.front() == ':' ? 1 : 3);
    const auto comma = id.find(',');
    if (comma == std::string_view::npos) return false;
    int card = -1;
    int device = -1;
    const auto parsedCard = std::from_chars(id.data(), id.data() + comma, card);
    const auto parsedDevice = std::from_chars(id.data() + comma + 1, id.data() + id.size(), device);
    if (parsedCard.ec != std::errc() || parsedCard.ptr != id.data() + comma || card < 0
        || parsedDevice.ec != std::errc() || parsedDevice.ptr != id.data() + id.size() || device < 0)
        return false;

    snd_ctl_t* control = nullptr;
    const auto controlName = "hw:" + std::to_string(card);
    if (snd_ctl_open(&control, controlName.c_str(), SND_CTL_NONBLOCK) < 0) return false;
    snd_pcm_info_t* info = nullptr;
    snd_pcm_info_alloca(&info);
    snd_pcm_info_set_device(info, static_cast<unsigned>(device));
    snd_pcm_info_set_subdevice(info, 0);
    snd_pcm_info_set_stream(info, SND_PCM_STREAM_PLAYBACK);
    const bool available = snd_ctl_pcm_info(control, info) >= 0;
    snd_ctl_close(control);
    return available;
}
