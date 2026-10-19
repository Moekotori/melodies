#pragma once

#include "PcmBlockExchange.h"
#include <memory>
#include <string>

namespace echo::audio_plugin {

// Mapping creation/open/destruction are control/worker operations, never
// device callback operations. Names are local, ASCII, and session-specific.
class SharedPcmMapping final {
public:
    static std::unique_ptr<SharedPcmMapping> create(const std::string& name,
        std::uint32_t sampleRate, std::uint32_t channels, std::string& error);
    static std::unique_ptr<SharedPcmMapping> open(const std::string& name, std::string& error);
    ~SharedPcmMapping();
    SharedPcmMapping(const SharedPcmMapping&) = delete;
    SharedPcmMapping& operator=(const SharedPcmMapping&) = delete;
    PcmRegion& region() noexcept { return *view_; }

private:
    SharedPcmMapping() = default;
    static std::unique_ptr<SharedPcmMapping> map(const std::string& name, bool create, std::string& error);
    PcmRegion* view_ = nullptr;
    std::string name_;
    bool owner_ = false;
    int fd_ = -1;
    void* handle_ = nullptr;
};

} // namespace echo::audio_plugin
