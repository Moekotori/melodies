#include "SharedPcmMapping.h"

#if defined(_WIN32)
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <sddl.h>
#include <array>
#else
#include <sys/mman.h>
#include <sys/stat.h>
#include <fcntl.h>
#include <unistd.h>
#endif

namespace echo::audio_plugin {
namespace {
bool validName(const std::string& name) {
    // Darwin has a small POSIX shm name bound. Keep a conservative portable
    // limit, including room for the leading slash and terminator.
    if (name.size() < 24 || name.size() > 30 || name.rfind("echo-ap-", 0) != 0) return false;
    for (const char letter : name)
        if (!(letter >= 'a' && letter <= 'z') && !(letter >= '0' && letter <= '9') && letter != '-') return false;
    return true;
}

#if defined(_WIN32)
// Access is limited to the creator's user SID, independently of the process's
// default DACL. No Everyone/Authenticated Users or remote discovery endpoint.
class MappingSecurity final {
public:
    MappingSecurity() {
        HANDLE token = nullptr;
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) return;
        alignas(TOKEN_USER) std::array<unsigned char, 4096> information {};
        DWORD bytes = 0;
        const bool read = GetTokenInformation(token, TokenUser, information.data(),
            static_cast<DWORD>(information.size()), &bytes) != FALSE;
        CloseHandle(token);
        if (!read) return;
        LPWSTR sid = nullptr;
        if (!ConvertSidToStringSidW(reinterpret_cast<TOKEN_USER*>(information.data())->User.Sid, &sid)) return;
        const std::wstring dacl = std::wstring(L"D:P(A;;GA;;;") + sid + L")";
        LocalFree(sid);
        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(dacl.c_str(), SDDL_REVISION_1, &descriptor_, nullptr)) return;
        attributes_ = { sizeof(SECURITY_ATTRIBUTES), descriptor_, FALSE };
    }
    ~MappingSecurity() { if (descriptor_) LocalFree(descriptor_); }
    SECURITY_ATTRIBUTES* get() noexcept { return descriptor_ ? &attributes_ : nullptr; }
private:
    PSECURITY_DESCRIPTOR descriptor_ = nullptr;
    SECURITY_ATTRIBUTES attributes_ {};
};
#endif
}

std::unique_ptr<SharedPcmMapping> SharedPcmMapping::map(const std::string& name, bool create, std::string& error) {
    error = "audio_plugin_pcm_mapping_failed";
    if (!validName(name)) { error = "audio_plugin_pcm_name_invalid"; return nullptr; }
    auto mapping = std::unique_ptr<SharedPcmMapping>(new SharedPcmMapping());
    mapping->name_ = name;
#if defined(_WIN32)
    const std::wstring nativeName = std::wstring(L"Local\\") + std::wstring(name.begin(), name.end());
    HANDLE handle = nullptr;
    if (create) {
        MappingSecurity security;
        if (!security.get()) return nullptr;
        handle = CreateFileMappingW(INVALID_HANDLE_VALUE, security.get(), PAGE_READWRITE,
            0, static_cast<DWORD>(sizeof(PcmRegion)), nativeName.c_str());
        if (handle && GetLastError() == ERROR_ALREADY_EXISTS) {
            CloseHandle(handle);
            error = "audio_plugin_pcm_name_in_use";
            return nullptr;
        }
    } else {
        handle = OpenFileMappingW(FILE_MAP_READ | FILE_MAP_WRITE, FALSE, nativeName.c_str());
    }
    if (!handle) return nullptr;
    mapping->handle_ = handle;
    mapping->view_ = static_cast<PcmRegion*>(MapViewOfFile(handle, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, sizeof(PcmRegion)));
#else
    mapping->name_ = "/" + name;
    mapping->fd_ = shm_open(mapping->name_.c_str(), create ? O_RDWR | O_CREAT | O_EXCL : O_RDWR, 0600);
    if (mapping->fd_ < 0) { error = "audio_plugin_pcm_open_failed"; return nullptr; }
    mapping->owner_ = create;
    if (create && ftruncate(mapping->fd_, static_cast<off_t>(sizeof(PcmRegion))) != 0) {
        error = "audio_plugin_pcm_resize_failed";
        return nullptr;
    }
    struct stat info {};
    const auto pageBytes = sysconf(_SC_PAGESIZE);
    const auto logicalBytes = static_cast<off_t>(sizeof(PcmRegion));
    const auto roundedBytes = pageBytes > 0 ? ((logicalBytes + pageBytes - 1) / pageBytes) * pageBytes : 0;
    // Darwin reports the page-rounded backing size; Linux may report the
    // requested logical size. Both must cover exactly this fixed ABI region.
    if (fstat(mapping->fd_, &info) != 0 || pageBytes <= 0
        || (info.st_size != logicalBytes && info.st_size != roundedBytes)) {
        error = "audio_plugin_pcm_size_invalid";
        return nullptr;
    }
    void* view = mmap(nullptr, sizeof(PcmRegion), PROT_READ | PROT_WRITE, MAP_SHARED, mapping->fd_, 0);
    if (view != MAP_FAILED) mapping->view_ = static_cast<PcmRegion*>(view);
#endif
    if (!mapping->view_) { error = "audio_plugin_pcm_map_failed"; return nullptr; }
    error.clear();
    return mapping;
}

std::unique_ptr<SharedPcmMapping> SharedPcmMapping::create(const std::string& name,
    std::uint32_t sampleRate, std::uint32_t channels, std::string& error) {
    // Validate before creating any OS object.
    if (sampleRate < 8000 || sampleRate > 768000 || channels == 0 || channels > maximumChannels) {
        error = "audio_plugin_pcm_format_invalid";
        return nullptr;
    }
    auto mapping = map(name, true, error);
    if (mapping && !initializeRegion(mapping->region(), sampleRate, channels)) {
        error = "audio_plugin_pcm_format_invalid";
        return nullptr;
    }
    return mapping;
}

std::unique_ptr<SharedPcmMapping> SharedPcmMapping::open(const std::string& name, std::string& error) {
    auto mapping = map(name, false, error);
    if (mapping && !isValidRegion(mapping->region())) {
        error = "audio_plugin_pcm_abi_invalid";
        return nullptr;
    }
    return mapping;
}

SharedPcmMapping::~SharedPcmMapping() {
#if defined(_WIN32)
    if (view_) UnmapViewOfFile(view_);
    if (handle_) CloseHandle(static_cast<HANDLE>(handle_));
#else
    if (view_) munmap(view_, sizeof(PcmRegion));
    if (fd_ >= 0) close(fd_);
    if (owner_) shm_unlink(name_.c_str());
#endif
}

} // namespace echo::audio_plugin
