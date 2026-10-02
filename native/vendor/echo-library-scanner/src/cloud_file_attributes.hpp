#pragma once
#include <filesystem>
#include <string>
#ifdef _WIN32
#include <windows.h>
#endif

// Attribute-only query: never open the content stream of a placeholder.
inline const char* cloudFileAvailability(const std::filesystem::path& path) {
#ifdef _WIN32
  // Extended Win32 paths do not accept forward slashes.
  auto nativePath = path;
  std::wstring apiPath = nativePath.make_preferred().wstring();
  if (apiPath.rfind(L"\\\\?\\", 0) != 0) {
    if (apiPath.rfind(L"\\\\", 0) == 0) {
      apiPath = L"\\\\?\\UNC\\" + apiPath.substr(2);
    } else if (apiPath.size() >= 3 && apiPath[1] == L':' && (apiPath[2] == L'\\' || apiPath[2] == L'/')) {
      apiPath = L"\\\\?\\" + apiPath;
    }
  }
  const DWORD attributes = GetFileAttributesW(apiPath.c_str());
  if (attributes == INVALID_FILE_ATTRIBUTES) return "unknown";
  constexpr DWORD recallOnOpen = 0x00040000;
  constexpr DWORD recallOnDataAccess = 0x00400000;
  if ((attributes & (FILE_ATTRIBUTE_OFFLINE | recallOnOpen | recallOnDataAccess)) != 0) return "cloud";
#else
  (void)path;
#endif
  return "local";
}
