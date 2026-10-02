cmake_minimum_required(VERSION 3.20)

project(echo_native_scanner LANGUAGES CXX)

add_executable(echo-native-scanner
  src/main.cpp
  src/music_graph.cpp
  src/metadata_file_tail.cpp
  src/ffmpeg_metadata.cpp
)

target_compile_features(echo-native-scanner PRIVATE cxx_std_17)

set(ECHO_FFMPEG_ROOT "" CACHE PATH "FFmpeg shared development root for bounded audio metadata fallback")
if(ECHO_FFMPEG_ROOT)
  target_include_directories(echo-native-scanner SYSTEM PRIVATE "${ECHO_FFMPEG_ROOT}/include")
  target_link_directories(echo-native-scanner PRIVATE "${ECHO_FFMPEG_ROOT}/lib")
  target_link_libraries(echo-native-scanner PRIVATE avformat avcodec avutil)
  target_compile_definitions(echo-native-scanner PRIVATE ECHO_SCANNER_HAS_FFMPEG=1)
elseif(UNIX)
  find_package(PkgConfig QUIET)
  if(PkgConfig_FOUND)
    pkg_check_modules(ECHO_SCAN_FFMPEG QUIET IMPORTED_TARGET libavformat libavcodec libavutil)
    if(ECHO_SCAN_FFMPEG_FOUND)
      target_link_libraries(echo-native-scanner PRIVATE PkgConfig::ECHO_SCAN_FFMPEG)
      target_compile_definitions(echo-native-scanner PRIVATE ECHO_SCANNER_HAS_FFMPEG=1)
    endif()
  endif()
endif()

if(MSVC)
  target_compile_options(echo-native-scanner PRIVATE /W4 /permissive- /EHsc /MP /utf-8)
else()
  target_compile_options(echo-native-scanner PRIVATE -Wall -Wextra -Wpedantic)
endif()
