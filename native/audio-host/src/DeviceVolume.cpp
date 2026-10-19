#include "DeviceVolume.h"
#include <algorithm>
#include <cmath>
#include <iostream>
#include <memory>
#include <stdexcept>
#include "../../audio-engine/third_party/nlohmann_json.hpp"

#ifdef _WIN32
#include <windows.h>
#include <mmdeviceapi.h>
#include <endpointvolume.h>

namespace {
template<typename T> struct ReleaseCom { void operator()(T* value) const { if (value) value->Release(); } };
template<typename T> using ComPtr = std::unique_ptr<T, ReleaseCom<T>>;

void requireResult(HRESULT result) {
    if (FAILED(result)) throw std::runtime_error("device_volume_driver_error");
}

class WindowsDeviceVolume {
public:
    explicit WindowsDeviceVolume(const std::string& endpointId) {
        requireResult(CoInitializeEx(nullptr, COINIT_MULTITHREADED));
        initialized_ = true;
        try {
            const int length = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, endpointId.data(), static_cast<int>(endpointId.size()), nullptr, 0);
            if (length <= 0) throw std::runtime_error("device_volume_invalid_endpoint");
            std::wstring id(static_cast<size_t>(length), L'\0');
            if (MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, endpointId.data(), static_cast<int>(endpointId.size()), id.data(), length) != length)
                throw std::runtime_error("device_volume_invalid_endpoint");
            IMMDeviceEnumerator* rawEnumerator = nullptr;
            requireResult(CoCreateInstance(__uuidof(MMDeviceEnumerator), nullptr, CLSCTX_INPROC_SERVER, __uuidof(IMMDeviceEnumerator), reinterpret_cast<void**>(&rawEnumerator)));
            ComPtr<IMMDeviceEnumerator> enumerator(rawEnumerator);
            IMMDevice* rawDevice = nullptr;
            requireResult(enumerator->GetDevice(id.c_str(), &rawDevice));
            ComPtr<IMMDevice> device(rawDevice);
            DWORD state = 0;
            requireResult(device->GetState(&state));
            if ((state & DEVICE_STATE_ACTIVE) == 0) throw std::runtime_error("device_volume_disconnected");
            IAudioEndpointVolume* rawVolume = nullptr;
            requireResult(device->Activate(__uuidof(IAudioEndpointVolume), CLSCTX_INPROC_SERVER, nullptr, reinterpret_cast<void**>(&rawVolume)));
            volume_.reset(rawVolume);
        } catch (...) { volume_.reset(); CoUninitialize(); initialized_ = false; throw; }
    }
    ~WindowsDeviceVolume() { volume_.reset(); if (initialized_) CoUninitialize(); }

    nlohmann::json query() const {
        DWORD support = 0;
        requireResult(volume_->QueryHardwareSupport(&support));
        float minimum = 0, maximum = 0, step = 0, current = 0;
        BOOL muted = FALSE;
        requireResult(volume_->GetVolumeRange(&minimum, &maximum, &step));
        requireResult(volume_->GetMasterVolumeLevel(&current));
        requireResult(volume_->GetMute(&muted));
        if (!std::isfinite(minimum) || !std::isfinite(maximum) || !std::isfinite(step) || !std::isfinite(current)
            || maximum <= minimum || step <= 0 || step > maximum - minimum)
            throw std::runtime_error("device_volume_invalid_range");
        const bool hardware = (support & ENDPOINT_HARDWARE_SUPPORT_VOLUME) != 0;
        return { {"supported", hardware}, {"hardwareMute", hardware && (support & ENDPOINT_HARDWARE_SUPPORT_MUTE) != 0},
            {"minimumDb", minimum}, {"maximumDb", maximum}, {"stepDb", step}, {"currentDb", current}, {"muted", muted != FALSE},
            {"reason", hardware ? "" : "software-only"} };
    }

    nlohmann::json setDb(double requested) {
        const auto before = query();
        if (!before.at("supported").get<bool>()) throw std::runtime_error("device_volume_hardware_unsupported");
        if (!std::isfinite(requested)) throw std::runtime_error("device_volume_invalid_level");
        const double minimum = before.at("minimumDb"), maximum = before.at("maximumDb"), step = before.at("stepDb");
        // Round downward so a discrete hardware step cannot exceed the request.
        const double level = minimum + std::floor((std::clamp(requested, minimum, maximum) - minimum) / step + 1e-7) * step;
        requireResult(volume_->SetMasterVolumeLevel(static_cast<float>(std::clamp(level, minimum, maximum)), nullptr));
        return query();
    }

    nlohmann::json setMute(bool muted) {
        const auto before = query();
        if (!before.at("hardwareMute").get<bool>()) throw std::runtime_error("device_volume_hardware_mute_unsupported");
        requireResult(volume_->SetMute(muted ? TRUE : FALSE, nullptr));
        return query();
    }
private:
    bool initialized_ = false;
    ComPtr<IAudioEndpointVolume> volume_;
};
}
#endif

std::optional<int> handleDeviceVolumeCommand(const std::vector<std::string>& arguments) {
    if (arguments.size() < 2 || arguments[1] != "--device-volume") return std::nullopt;
    try {
        if (arguments.size() < 4 || arguments.size() > 5 || arguments[3].empty() || arguments[3].size() > 4096)
            throw std::runtime_error("device_volume_invalid_command");
#ifdef _WIN32
        WindowsDeviceVolume endpoint(arguments[3]);
        nlohmann::json result;
        if (arguments[2] == "query" && arguments.size() == 4) result = endpoint.query();
        else if (arguments[2] == "set-db" && arguments.size() == 5) {
            size_t consumed = 0;
            const double level = std::stod(arguments[4], &consumed);
            if (consumed != arguments[4].size()) throw std::runtime_error("device_volume_invalid_level");
            result = endpoint.setDb(level);
        } else if (arguments[2] == "set-mute" && arguments.size() == 5 && (arguments[4] == "0" || arguments[4] == "1")) {
            result = endpoint.setMute(arguments[4] == "1");
        } else throw std::runtime_error("device_volume_invalid_command");
        std::cout << result.dump() << std::endl;
        return 0;
#else
        throw std::runtime_error("device_volume_platform_unsupported");
#endif
    } catch (...) {
        // Keep device identifiers and driver details out of logs and error payloads.
        std::cout << "{\"error\":\"device_volume_unavailable\"}" << std::endl;
        return 1;
    }
}
