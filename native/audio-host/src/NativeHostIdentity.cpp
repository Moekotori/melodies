#include "NativeHostIdentity.h"
#include "NativeBuildIdentity.generated.h"
#include <array>
#include <string>
#ifdef _WIN32
#include <windows.h>
#elif defined(__APPLE__)
#include <mach-o/dyld.h>
#include <unistd.h>
#else
#include <unistd.h>
#endif

namespace echo_audio_host {
namespace {
nlohmann::json loadedExecutable() {
#ifdef _WIN32
    std::array<wchar_t, 32768> path {};
    const auto size = GetModuleFileNameW(nullptr, path.data(), static_cast<DWORD>(path.size()));
    if (!size || size >= path.size()) return nullptr;
    const int bytes = WideCharToMultiByte(CP_UTF8, 0, path.data(), size, nullptr, 0, nullptr, nullptr);
    if (bytes <= 0) return nullptr;
    std::string utf8(bytes, '\0');
    if (!WideCharToMultiByte(CP_UTF8, 0, path.data(), size, utf8.data(), bytes, nullptr, nullptr)) return nullptr;
    return utf8;
#elif defined(__APPLE__)
    std::array<char, 32768> path {};
    uint32_t size = static_cast<uint32_t>(path.size());
    return _NSGetExecutablePath(path.data(), &size) == 0 ? nlohmann::json(path.data()) : nlohmann::json(nullptr);
#else
    std::array<char, 32768> path {};
    const auto size = readlink("/proc/self/exe", path.data(), path.size());
    return size > 0 && size < static_cast<ssize_t>(path.size())
        ? nlohmann::json(std::string(path.data(), size)) : nlohmann::json(nullptr);
#endif
}
}
nlohmann::json nativeHostIdentity(bool includeLoadedExecutable) {
    static const nlohmann::json identity {
        {"schemaVersion", 1}, {"buildId", ECHO_NATIVE_BUILD_ID},
        {"buildIdKind", "production-source-and-host-configuration-sha256"},
        {"sourceDigest", ECHO_NATIVE_SOURCE_DIGEST}, {"configurationDigest", ECHO_NATIVE_CONFIGURATION_DIGEST},
        {"sourceRevision", std::string(ECHO_NATIVE_SOURCE_REVISION).empty() ? nlohmann::json(nullptr) : nlohmann::json(ECHO_NATIVE_SOURCE_REVISION)},
        {"imageRole", ECHO_NATIVE_IMAGE_ROLE}, {"protocolVersion", 1}, {"backendContractVersion", 2},
        {"compiledCapabilities", {
            {"cudaFirFp64", ECHO_NATIVE_CUDA_DSP_COMPILED != 0}, {"cpuTerminalFp64", true}, {"cpuSdmFp64", true},
            {"cudaUltimateTerminal", false}, {"sdmRuntimeHealthV1", true}, {"sdmIndependentMeasurementV1", true},
            {"independentCalibrationAdmission", false}}},
    };
    auto result = identity;
#ifdef _WIN32
    result["processId"] = GetCurrentProcessId();
#else
    result["processId"] = getpid();
#endif
    if (includeLoadedExecutable) result["loadedExecutable"] = loadedExecutable();
    return result;
}
}
