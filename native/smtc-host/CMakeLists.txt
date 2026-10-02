cmake_minimum_required(VERSION 3.22)

project(echo_smtc_host VERSION 0.2.0 LANGUAGES CXX RC)

# Windows resolves the media flyout's app name and icon from the SMTC process
# when no Start Menu entry matches the AUMID (typical for Steam installs), so the
# helper carries ECHO's own icon and file description.
set(ECHO_SMTC_HOST_ICON "${CMAKE_CURRENT_SOURCE_DIR}/resources/echo-app-icon.ico")
if (EXISTS "${ECHO_SMTC_HOST_ICON}")
  set(ECHO_SMTC_HOST_ICON_STATEMENT "1 ICON \"${ECHO_SMTC_HOST_ICON}\"")
else()
  message(WARNING "ECHO app icon not found at ${ECHO_SMTC_HOST_ICON}; building echo-smtc-host without an icon.")
  set(ECHO_SMTC_HOST_ICON_STATEMENT "")
endif()

configure_file(
  resources/echo-smtc-host.rc.in
  "${CMAKE_CURRENT_BINARY_DIR}/echo-smtc-host.rc"
  @ONLY
)

add_executable(echo-smtc-host WIN32
  src/main.cpp
  "${CMAKE_CURRENT_BINARY_DIR}/echo-smtc-host.rc"
)

target_compile_features(echo-smtc-host PRIVATE cxx_std_20)

target_compile_definitions(echo-smtc-host PRIVATE
  NOMINMAX
  WIN32_LEAN_AND_MEAN
  UNICODE
  _UNICODE
)

if (MSVC)
  target_compile_options(echo-smtc-host PRIVATE /permissive- /W4 /MP)
  # Keep int main() so the JSONL stdin/stdout pipes from Electron still work.
  target_link_options(echo-smtc-host PRIVATE /ENTRY:mainCRTStartup)
endif()

target_link_libraries(echo-smtc-host PRIVATE
  runtimeobject
  shell32
  user32
  windowsapp
)
