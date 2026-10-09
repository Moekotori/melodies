cmake_minimum_required(VERSION 3.24)

project(echo_audio_host VERSION 0.1.0 LANGUAGES C CXX)

# The native sources and vendor headers are UTF-8. On Windows, MSVC otherwise
# decodes them with the active ANSI code page and emits repeated C4819 warnings.
if(MSVC)
  add_compile_options(
    $<$<COMPILE_LANGUAGE:C,CXX>:/utf-8>
    $<$<COMPILE_LANGUAGE:CUDA>:-Xcompiler=/utf-8>)
endif()

option(ECHO_ENABLE_CUDA_DSP "Enable in-process CUDA FIR acceleration when a CUDA toolkit is available" ON)
option(ECHO_ENABLE_EXPERIMENTAL_SDM_SMOOTHSTEP "Enable the CPU-only experimental smoothstep SDM interpolator" OFF)
option(ECHO_ENABLE_ASAN_UBSAN "Instrument native audio targets with AddressSanitizer and UndefinedBehaviorSanitizer" OFF)
option(ECHO_ENABLE_TSAN "Instrument native audio targets with ThreadSanitizer" OFF)

if(ECHO_ENABLE_ASAN_UBSAN AND ECHO_ENABLE_TSAN)
  message(FATAL_ERROR "ECHO_ENABLE_ASAN_UBSAN and ECHO_ENABLE_TSAN require separate build trees")
endif()
if((ECHO_ENABLE_ASAN_UBSAN OR ECHO_ENABLE_TSAN) AND MSVC)
  message(FATAL_ERROR "Native sanitizer CI is supported with GCC or Clang, not MSVC")
endif()
if(ECHO_ENABLE_ASAN_UBSAN)
  add_compile_options(-fsanitize=address,undefined -fno-omit-frame-pointer)
  add_link_options(-fsanitize=address,undefined -fno-omit-frame-pointer)
endif()
if(ECHO_ENABLE_TSAN)
  add_compile_options(-fsanitize=thread -fno-omit-frame-pointer)
  add_link_options(-fsanitize=thread -fno-omit-frame-pointer)
endif()

option(ECHO_REQUIRE_PIPEWIRE "Require the native PipeWire backend for Linux builds" OFF)
set(ECHO_PIPEWIRE_FOUND FALSE)
if(UNIX AND NOT APPLE)
  find_package(PkgConfig QUIET)
  if(PkgConfig_FOUND)
    pkg_check_modules(ECHO_PIPEWIRE QUIET IMPORTED_TARGET libpipewire-0.3)
  endif()
  if(ECHO_PIPEWIRE_FOUND)
    set(ECHO_PIPEWIRE_FOUND TRUE)
  else()
    set(_echo_pipewire_sdk "${CMAKE_SOURCE_DIR}/../../out/linux-x64/pipewire-sdk/usr")
    if(EXISTS "${_echo_pipewire_sdk}/include/pipewire-0.3/pipewire/pipewire.h")
      set(ECHO_PIPEWIRE_FOUND TRUE)
      set(ECHO_PIPEWIRE_INCLUDE_DIRS
        "${_echo_pipewire_sdk}/include/pipewire-0.3"
        "${_echo_pipewire_sdk}/include/spa-0.2")
      set(ECHO_PIPEWIRE_LINK_LIBRARIES ":libpipewire-0.3.so.0")
      message(STATUS "ECHO native PipeWire shared output enabled (extracted SDK headers)")
    endif()
  endif()
  if(ECHO_PIPEWIRE_FOUND AND TARGET PkgConfig::ECHO_PIPEWIRE)
    message(STATUS "ECHO native PipeWire shared output enabled (pkg-config libpipewire-0.3)")
  elseif(NOT ECHO_PIPEWIRE_FOUND)
    if(ECHO_REQUIRE_PIPEWIRE)
      message(FATAL_ERROR "Native PipeWire is required: install libpipewire-0.3-dev and libspa-0.2-dev")
    endif()
    message(WARNING "ECHO native PipeWire shared output disabled: install libpipewire-0.3-dev or extract SDK headers to out/linux-x64/pipewire-sdk")
  endif()
endif()
include(CheckLanguage)
if(ECHO_ENABLE_CUDA_DSP)
  check_language(CUDA)
endif()

if(ECHO_ENABLE_CUDA_DSP AND CMAKE_CUDA_COMPILER)
  enable_language(CUDA)
  find_package(CUDAToolkit REQUIRED)
  add_library(echo-cuda-dsp STATIC
    src/CudaFirProcessor.cu
    src/CudaSdmProcessor.cu)
  target_compile_definitions(echo-cuda-dsp PRIVATE ECHO_AUDIO_HOST_HAS_CUDA_DSP=1)
  target_link_libraries(echo-cuda-dsp PRIVATE CUDA::cudart_static)
  set_target_properties(echo-cuda-dsp PROPERTIES
    CUDA_STANDARD 17
    CUDA_STANDARD_REQUIRED ON
    CUDA_RUNTIME_LIBRARY Static
    CUDA_ARCHITECTURES "75;86;89;120"
    POSITION_INDEPENDENT_CODE ON
  )
  if(MSVC)
    target_compile_options(echo-cuda-dsp PRIVATE
      $<$<COMPILE_LANGUAGE:CUDA>:-Xcompiler=/W3>)
  endif()
  message(STATUS "ECHO native CUDA DSP enabled: ${CMAKE_CUDA_COMPILER}")
else()
  add_library(echo-cuda-dsp STATIC
    src/CudaFirProcessorStub.cpp
    src/CudaSdmProcessorStub.cpp)
  message(WARNING "CUDA toolkit was not found; echo-audio-host will retain CPU fallback.")
endif()

target_compile_features(echo-cuda-dsp PRIVATE cxx_std_20)
target_include_directories(echo-cuda-dsp PRIVATE src ../audio-engine)
if(MSVC)
  set_property(TARGET echo-cuda-dsp PROPERTY
    MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()

if(WIN32)
  option(ECHO_ENABLE_ASIO "Enable direct ASIO SDK support in echo-audio-host" ON)
else()
  option(ECHO_ENABLE_ASIO "Enable direct ASIO SDK support in echo-audio-host" OFF)
endif()

if(APPLE)
  set(ECHO_FFMPEG_ROOT "" CACHE PATH "Root of the pinned macOS FFmpeg shared development build")
  if(NOT ECHO_FFMPEG_ROOT)
    find_package(PkgConfig REQUIRED)
    pkg_check_modules(ECHO_MACOS_FFMPEG REQUIRED IMPORTED_TARGET
      libavformat
      libavcodec
      libswresample
      libavutil)
  endif()
  find_library(ECHO_COREAUDIO_FRAMEWORK CoreAudio REQUIRED)
  find_library(ECHO_AUDIOTOOLBOX_FRAMEWORK AudioToolbox REQUIRED)
  find_library(ECHO_COREFOUNDATION_FRAMEWORK CoreFoundation REQUIRED)
endif()

set(ECHO_AUDIO_ENGINE_SOURCES
  ../audio-engine/ChannelBalanceProcessor.cpp
  ../audio-engine/CompressorProcessor.cpp
  ../audio-engine/SpatialDspProcessor.cpp
  ../audio-engine/ConvolutionProcessor.cpp
  ../audio-engine/DspChain.cpp
  ../audio-engine/DspHeadroomProcessor.cpp
  ../audio-engine/DspRackOrder.cpp
  ../audio-engine/EqMessageProtocol.cpp
  ../audio-engine/EqPresetStore.cpp
  ../audio-engine/EqProcessor.cpp
  ../audio-engine/JsonRpcProtocol.cpp
  ../audio-engine/LevelMeterProcessor.cpp
  ../audio-engine/NativeFormatProcessor.cpp
  ../audio-engine/PartitionedConvolutionEngine.cpp
  ../audio-engine/SdmNtfDesign.cpp
  ../audio-engine/SdmPreModulator.cpp
  ../audio-engine/PlaybackRateProcessor.cpp
  ../audio-engine/ReplayGainProcessor.cpp
  ../audio-engine/TruePeakLimiterProcessor.cpp
  ../audio-engine/WorkshopAudioEffectProcessor.cpp
)

set(ECHO_AUDIO_HOST_COMMON_SOURCES
  ${ECHO_AUDIO_ENGINE_SOURCES}
  src/ListenTogetherRpc.cpp
  src/ListenTogetherAudioTransport.cpp
  src/ListenTogetherOpusCodec.cpp
  src/ListenTogetherRealtimeAudio.cpp
  src/AudioDaemon.cpp
  src/AutomixTempoProcessor.cpp
  src/CachePcmProducer.cpp
  src/DopRingSource.cpp
  src/HostUtils.cpp
  src/MqaPassthroughPolicy.cpp
  src/NativeDsdRingSource.cpp
  src/NativeFirProcessor.cpp
  src/NativePlaybackPipeline.cpp
  src/NativeSdmProcessor.cpp
  src/PcmDomainDspStage.cpp
  src/PcmRingAudioSource.cpp
  src/ProcessMemoryTelemetry.cpp
  src/RawPcmInputReader.cpp
  src/RpcOutputWriter.cpp
  src/NativeDaemonBootstrap.cpp
  src/WindowsCddaReader.cpp
  src/LinuxCddaDevice.cpp
)

if(APPLE)
  list(APPEND ECHO_AUDIO_HOST_COMMON_SOURCES
    src/MacCoreAudioDopOutput.cpp)
endif()

function(configure_echo_audio_target target)
  target_compile_features(${target} PRIVATE cxx_std_20)
  target_include_directories(${target} PRIVATE src ../audio-engine)
  target_compile_definitions(${target} PRIVATE ECHO_ENABLE_ASIO=$<BOOL:${ECHO_ENABLE_ASIO}>)
  target_compile_definitions(${target} PRIVATE
    ECHO_ENABLE_EXPERIMENTAL_SDM_SMOOTHSTEP=$<BOOL:${ECHO_ENABLE_EXPERIMENTAL_SDM_SMOOTHSTEP}>)
  target_link_libraries(${target} PRIVATE echo-cuda-dsp)

  if(MSVC)
    # Keep the packaged host independent from a separately installed Visual C++
    # Redistributable. Clean Windows machines only need the DLLs we ship beside it.
    set_property(TARGET ${target} PROPERTY
      MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
  endif()

  if(WIN32)
    target_compile_definitions(${target} PRIVATE NOMINMAX)
    set(ECHO_FFMPEG_ROOT "" CACHE PATH "Root of a Windows FFmpeg shared development build")
    if(NOT ECHO_FFMPEG_ROOT)
      message(FATAL_ERROR "ECHO_FFMPEG_ROOT is required on Windows")
    endif()
    target_include_directories(${target} PRIVATE "${ECHO_FFMPEG_ROOT}/include")
    target_link_directories(${target} PRIVATE "${ECHO_FFMPEG_ROOT}/lib")
    target_link_libraries(${target} PRIVATE avformat avcodec swresample avutil)
    file(GLOB ECHO_FFMPEG_RUNTIME_DLLS "${ECHO_FFMPEG_ROOT}/bin/*.dll")
    foreach(ECHO_FFMPEG_RUNTIME_DLL IN LISTS ECHO_FFMPEG_RUNTIME_DLLS)
      add_custom_command(TARGET ${target} POST_BUILD
        COMMAND ${CMAKE_COMMAND} -E copy_if_different
          "${ECHO_FFMPEG_RUNTIME_DLL}"
          "$<TARGET_FILE_DIR:${target}>")
    endforeach()
    target_sources(${target} PRIVATE src/wasapi_exclusive.cpp src/wasapi_shared.cpp src/ks_output.cpp)
    target_link_libraries(${target} PRIVATE shell32 ole32 avrt winmm advapi32 uuid cfgmgr32 setupapi ksuser psapi)
    if(ECHO_ENABLE_ASIO)
      target_sources(${target} PRIVATE
        src/asio_host.cpp
        third_party/asio-sdk/common/asio.cpp
        third_party/asio-sdk/host/asiodrivers.cpp
        third_party/asio-sdk/host/pc/asiolist.cpp
      )
      target_include_directories(${target} PRIVATE
        third_party/asio-sdk/common
        third_party/asio-sdk/host
        third_party/asio-sdk/host/pc
      )
      target_compile_definitions(${target} PRIVATE _CRT_SECURE_NO_WARNINGS)
    endif()
  endif()

  if(UNIX AND NOT APPLE)
    target_link_libraries(${target} PRIVATE avformat avcodec swresample avutil)
    find_package(ALSA REQUIRED)
    target_include_directories(${target} PRIVATE ${ALSA_INCLUDE_DIRS})
    target_link_libraries(${target} PRIVATE ${ALSA_LIBRARIES} pthread dl)
    if(ECHO_PIPEWIRE_FOUND)
      target_compile_definitions(${target} PRIVATE ECHO_ENABLE_PIPEWIRE=1)
      if(TARGET PkgConfig::ECHO_PIPEWIRE)
        target_link_libraries(${target} PRIVATE PkgConfig::ECHO_PIPEWIRE)
      else()
        target_include_directories(${target} PRIVATE ${ECHO_PIPEWIRE_INCLUDE_DIRS})
        target_link_libraries(${target} PRIVATE ${ECHO_PIPEWIRE_LINK_LIBRARIES})
      endif()
    endif()
  endif()

  if(APPLE)
    target_sources(${target} PRIVATE
      src/MacCoreAudioDeviceInfo.cpp
      src/MacCoreAudioDeviceWatcher.cpp
      src/MacCoreAudioHogMode.cpp)
    if(ECHO_FFMPEG_ROOT)
      target_include_directories(${target} PRIVATE "${ECHO_FFMPEG_ROOT}/include")
      target_link_directories(${target} PRIVATE "${ECHO_FFMPEG_ROOT}/lib")
      target_link_libraries(${target} PRIVATE avformat avcodec swresample avutil)
      set_target_properties(${target} PROPERTIES
        BUILD_WITH_INSTALL_RPATH TRUE
        INSTALL_RPATH "@loader_path/../tools-macos/lib;@loader_path/tools/lib")
    else()
      target_link_libraries(${target} PRIVATE PkgConfig::ECHO_MACOS_FFMPEG)
    endif()
    target_link_libraries(${target} PRIVATE
      ${ECHO_COREAUDIO_FRAMEWORK}
      ${ECHO_AUDIOTOOLBOX_FRAMEWORK}
      ${ECHO_COREFOUNDATION_FRAMEWORK})
  endif()
endfunction()

add_executable(echo-audio-host)
target_sources(echo-audio-host PRIVATE ${ECHO_AUDIO_HOST_COMMON_SOURCES} src/main.cpp)
configure_echo_audio_target(echo-audio-host)
if(UNIX AND NOT APPLE)
  target_sources(echo-audio-host PRIVATE src/LinuxAlsaNativeDsdOutput.cpp src/LinuxPipeWireOutput.cpp src/LinuxAlsaDevicePolicy.cpp)
endif()

add_executable(echo-automix-features
  src/AutomixFeatureAnalysis.cpp
  tools/automix-features/main.cpp)
target_compile_features(echo-automix-features PRIVATE cxx_std_20)
target_include_directories(echo-automix-features PRIVATE ../audio-engine)
if(MSVC)
  set_property(TARGET echo-automix-features PROPERTY
    MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()

add_executable(echo-connect-src-renderer)
target_sources(echo-connect-src-renderer PRIVATE
  ../audio-engine/NativeFormatProcessor.cpp
  ../audio-engine/SdmNtfDesign.cpp
  ../audio-engine/SdmPreModulator.cpp
  src/NativeFirProcessor.cpp
  src/NativeSdmProcessor.cpp
  tools/connect-src-renderer/main.cpp)
target_compile_features(echo-connect-src-renderer PRIVATE cxx_std_20)
target_include_directories(echo-connect-src-renderer PRIVATE src ../audio-engine)
target_link_libraries(echo-connect-src-renderer PRIVATE echo-cuda-dsp)
if(MSVC)
  set_property(TARGET echo-connect-src-renderer PROPERTY
    MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()

add_executable(echo-eq-editing-tests EXCLUDE_FROM_ALL
  tests/eq_editing_tests.cpp ../audio-engine/EqProcessor.cpp)
target_compile_features(echo-eq-editing-tests PRIVATE cxx_std_20)
target_include_directories(echo-eq-editing-tests PRIVATE ../audio-engine)

if(UNIX AND NOT APPLE)
  add_executable(echo-linux-output-tests EXCLUDE_FROM_ALL tests/linux_output_tests.cpp)
  target_compile_features(echo-linux-output-tests PRIVATE cxx_std_17)
  target_include_directories(echo-linux-output-tests PRIVATE src)
  target_link_libraries(echo-linux-output-tests PRIVATE pthread)
  add_test(NAME echo-linux-output-tests COMMAND echo-linux-output-tests)
endif()

add_executable(echo-audio-engine-tests EXCLUDE_FROM_ALL)
target_sources(echo-audio-engine-tests PRIVATE ${ECHO_AUDIO_HOST_COMMON_SOURCES}
  tests/audio_engine_tests.cpp tests/listening_volume_tests.cpp tests/asio_queue_channel_tests.cpp tests/daemon_seek_end_tests.cpp)
target_compile_definitions(echo-audio-engine-tests PRIVATE ECHO_AUDIO_ENGINE_TESTS=1)
configure_echo_audio_target(echo-audio-engine-tests)
if(UNIX AND NOT APPLE)
  target_sources(echo-audio-engine-tests PRIVATE src/LinuxAlsaNativeDsdOutput.cpp src/LinuxPipeWireOutput.cpp src/LinuxAlsaDevicePolicy.cpp)
  add_dependencies(echo-audio-engine-tests echo-linux-output-tests)
endif()

add_executable(echo-coreaudio-dop-packing-tests EXCLUDE_FROM_ALL
  tests/coreaudio_dop_packing_tests.cpp)
target_compile_features(echo-coreaudio-dop-packing-tests PRIVATE cxx_std_20)
target_include_directories(echo-coreaudio-dop-packing-tests PRIVATE src)

add_executable(echo-jsonrpc-protocol-characterization-tests EXCLUDE_FROM_ALL)
target_sources(echo-jsonrpc-protocol-characterization-tests PRIVATE ${ECHO_AUDIO_HOST_COMMON_SOURCES} tests/jsonrpc_protocol_characterization.cpp)
configure_echo_audio_target(echo-jsonrpc-protocol-characterization-tests)

add_executable(echo-dsd-ring-source-characterization-tests EXCLUDE_FROM_ALL)
target_sources(echo-dsd-ring-source-characterization-tests PRIVATE
  ${ECHO_AUDIO_ENGINE_SOURCES}
  src/AudioDaemon.cpp
  src/AutomixTempoProcessor.cpp
  src/CachePcmProducer.cpp
  src/DopRingSource.cpp
  src/HostUtils.cpp
  src/NativeDsdRingSource.cpp
  src/RpcOutputWriter.cpp
  tests/dsd_ring_source_characterization.cpp
)
configure_echo_audio_target(echo-dsd-ring-source-characterization-tests)

add_executable(echo-native-playback-pipeline-tests EXCLUDE_FROM_ALL)
target_sources(echo-native-playback-pipeline-tests PRIVATE
  ${ECHO_AUDIO_HOST_COMMON_SOURCES}
  tests/native_playback_pipeline_tests.cpp)
configure_echo_audio_target(echo-native-playback-pipeline-tests)
if(ECHO_ENABLE_ASAN_UBSAN OR ECHO_ENABLE_TSAN)
  # Instrumentation distorts measured realtime admission and callback deadlines.
  # The ordinary native test target still enforces those timing budgets.
  target_compile_definitions(echo-audio-engine-tests PRIVATE ECHO_NATIVE_INSTRUMENTED_TESTS=1)
  target_compile_definitions(echo-native-playback-pipeline-tests PRIVATE ECHO_NATIVE_INSTRUMENTED_TESTS=1)
endif()

add_executable(echo-level-meter-processor-tests EXCLUDE_FROM_ALL
  ../audio-engine/LevelMeterProcessor.cpp
  tests/level_meter_processor_tests.cpp)
target_compile_features(echo-level-meter-processor-tests PRIVATE cxx_std_20)
target_include_directories(echo-level-meter-processor-tests PRIVATE src ../audio-engine)
target_compile_definitions(echo-level-meter-processor-tests PRIVATE ECHO_AUDIO_ENGINE_TESTS=1)
if(MSVC)
  set_property(TARGET echo-level-meter-processor-tests PROPERTY
    MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()
if(WIN32)
  target_compile_definitions(echo-level-meter-processor-tests PRIVATE NOMINMAX)
endif()
if(UNIX)
  find_package(Threads REQUIRED)
  target_link_libraries(echo-level-meter-processor-tests PRIVATE Threads::Threads)
endif()

add_executable(echo-cache-pcm-producer-tests EXCLUDE_FROM_ALL
  src/CachePcmProducer.cpp
  tests/cache_pcm_producer_tests.cpp)
configure_echo_audio_target(echo-cache-pcm-producer-tests)

add_executable(echo-windows-cdda-reader-tests EXCLUDE_FROM_ALL
  src/HostUtils.cpp
  src/WindowsCddaReader.cpp
  tests/windows_cdda_reader_tests.cpp
  tests/cdda_signal_integrity_tests.cpp)
target_compile_features(echo-windows-cdda-reader-tests PRIVATE cxx_std_20)
target_include_directories(echo-windows-cdda-reader-tests PRIVATE src)
if(MSVC)
  set_property(TARGET echo-windows-cdda-reader-tests PROPERTY
    MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()
if(WIN32)
  target_compile_definitions(echo-windows-cdda-reader-tests PRIVATE NOMINMAX)
endif()
target_sources(echo-windows-cdda-reader-tests PRIVATE src/LinuxCddaDevice.cpp)
if(WIN32 OR (APPLE AND ECHO_FFMPEG_ROOT))
  target_include_directories(echo-windows-cdda-reader-tests PRIVATE "${ECHO_FFMPEG_ROOT}/include")
  target_link_directories(echo-windows-cdda-reader-tests PRIVATE "${ECHO_FFMPEG_ROOT}/lib")
  target_link_libraries(echo-windows-cdda-reader-tests PRIVATE swresample avutil)
  if(WIN32)
    file(GLOB ECHO_CDDA_TEST_DLLS "${ECHO_FFMPEG_ROOT}/bin/avutil-*.dll" "${ECHO_FFMPEG_ROOT}/bin/swresample-*.dll")
    foreach(ECHO_CDDA_TEST_DLL IN LISTS ECHO_CDDA_TEST_DLLS)
      add_custom_command(TARGET echo-windows-cdda-reader-tests POST_BUILD
        COMMAND ${CMAKE_COMMAND} -E copy_if_different "${ECHO_CDDA_TEST_DLL}" "$<TARGET_FILE_DIR:echo-windows-cdda-reader-tests>")
    endforeach()
  endif()
elseif(APPLE)
  target_link_libraries(echo-windows-cdda-reader-tests PRIVATE PkgConfig::ECHO_MACOS_FFMPEG)
else()
  target_link_libraries(echo-windows-cdda-reader-tests PRIVATE swresample avutil)
endif()

foreach(_tgt echo-audio-host echo-connect-src-renderer echo-audio-engine-tests
    echo-jsonrpc-protocol-characterization-tests
    echo-dsd-ring-source-characterization-tests
    echo-native-playback-pipeline-tests
    echo-level-meter-processor-tests
    echo-cache-pcm-producer-tests
    echo-windows-cdda-reader-tests)
  if(NOT MSVC)
    target_compile_options(${_tgt} PRIVATE
      -Wno-error=float-equal
      -Wno-error=sign-conversion)
  endif()
endforeach()

add_dependencies(echo-audio-engine-tests echo-jsonrpc-protocol-characterization-tests echo-dsd-ring-source-characterization-tests)
add_dependencies(echo-audio-engine-tests echo-cache-pcm-producer-tests)
add_dependencies(echo-audio-engine-tests echo-level-meter-processor-tests)
add_dependencies(echo-audio-engine-tests echo-native-playback-pipeline-tests)
add_dependencies(echo-audio-engine-tests echo-windows-cdda-reader-tests)

enable_testing()
add_executable(echo-native-pcm-channel-tests EXCLUDE_FROM_ALL
  src/NativeDaemonBootstrap.cpp src/RpcOutputWriter.cpp tests/native_pcm_channel_tests.cpp tests/native_pcm_decode_tests.cpp)
target_compile_features(echo-native-pcm-channel-tests PRIVATE cxx_std_20)
target_include_directories(echo-native-pcm-channel-tests PRIVATE src ../audio-engine)
if(WIN32 OR (APPLE AND ECHO_FFMPEG_ROOT))
  target_include_directories(echo-native-pcm-channel-tests PRIVATE "${ECHO_FFMPEG_ROOT}/include")
  target_link_directories(echo-native-pcm-channel-tests PRIVATE "${ECHO_FFMPEG_ROOT}/lib")
  target_link_libraries(echo-native-pcm-channel-tests PRIVATE avformat avcodec swresample avutil)
elseif(APPLE)
  target_link_libraries(echo-native-pcm-channel-tests PRIVATE PkgConfig::ECHO_MACOS_FFMPEG)
else()
  target_link_libraries(echo-native-pcm-channel-tests PRIVATE avformat avcodec swresample avutil)
endif()
if(MSVC)
  set_property(TARGET echo-native-pcm-channel-tests PROPERTY
    MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
else()
  target_link_libraries(echo-native-pcm-channel-tests PRIVATE pthread)
endif()
add_dependencies(echo-audio-engine-tests echo-native-pcm-channel-tests)
add_test(NAME echo-native-pcm-channel-tests COMMAND echo-native-pcm-channel-tests "${CMAKE_CURRENT_SOURCE_DIR}/tests/fixtures")
if(APPLE AND ECHO_FFMPEG_ROOT)
  set_tests_properties(echo-native-pcm-channel-tests PROPERTIES ENVIRONMENT "DYLD_LIBRARY_PATH=${ECHO_FFMPEG_ROOT}/lib")
endif()
# Decoder-only regression: do not pull device, DSP, CUDA or ASIO targets into
# a focused seek check.
add_executable(echo-libav-seek-tests EXCLUDE_FROM_ALL tests/libav_seek_tests.cpp)
target_compile_features(echo-libav-seek-tests PRIVATE cxx_std_20)
target_include_directories(echo-libav-seek-tests PRIVATE ../audio-engine)
if(WIN32 OR (APPLE AND ECHO_FFMPEG_ROOT))
  target_include_directories(echo-libav-seek-tests PRIVATE "${ECHO_FFMPEG_ROOT}/include")
  target_link_directories(echo-libav-seek-tests PRIVATE "${ECHO_FFMPEG_ROOT}/lib")
  target_link_libraries(echo-libav-seek-tests PRIVATE avformat avcodec swresample avutil)
elseif(APPLE)
  target_link_libraries(echo-libav-seek-tests PRIVATE PkgConfig::ECHO_MACOS_FFMPEG)
else()
  target_link_libraries(echo-libav-seek-tests PRIVATE avformat avcodec swresample avutil)
endif()
if(MSVC)
  target_compile_definitions(echo-libav-seek-tests PRIVATE NOMINMAX)
  set_property(TARGET echo-libav-seek-tests PROPERTY
    MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()
if(WIN32)
  foreach(runtime_name avformat avcodec swresample avutil)
    file(GLOB seek_runtime_dlls "${ECHO_FFMPEG_ROOT}/bin/${runtime_name}-*.dll")
    foreach(runtime_dll IN LISTS seek_runtime_dlls)
      add_custom_command(TARGET echo-libav-seek-tests POST_BUILD
        COMMAND ${CMAKE_COMMAND} -E copy_if_different "${runtime_dll}"
          "$<TARGET_FILE_DIR:echo-libav-seek-tests>")
    endforeach()
  endforeach()
endif()
add_test(NAME echo-libav-seek-tests COMMAND echo-libav-seek-tests
  "${CMAKE_CURRENT_SOURCE_DIR}/tests/fixtures")
add_dependencies(echo-audio-engine-tests echo-libav-seek-tests)

add_executable(echo-track-analysis-tests EXCLUDE_FROM_ALL
  ../audio-engine/TrackAnalysis.cpp tests/track_analysis_tests.cpp)
target_compile_features(echo-track-analysis-tests PRIVATE cxx_std_20)
target_include_directories(echo-track-analysis-tests PRIVATE ../audio-engine)
if(MSVC)
  set_property(TARGET echo-track-analysis-tests PROPERTY
    MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()
add_dependencies(echo-audio-engine-tests echo-track-analysis-tests)
add_test(NAME echo-track-analysis-tests COMMAND echo-track-analysis-tests)

add_executable(echo-track-analysis
  tools/track-analysis/main.cpp
  ../audio-engine/TrackAnalysis.cpp)
target_compile_features(echo-track-analysis PRIVATE cxx_std_20)
target_include_directories(echo-track-analysis PRIVATE ../audio-engine)
if(WIN32 OR (APPLE AND ECHO_FFMPEG_ROOT))
  target_include_directories(echo-track-analysis PRIVATE "${ECHO_FFMPEG_ROOT}/include")
  target_link_directories(echo-track-analysis PRIVATE "${ECHO_FFMPEG_ROOT}/lib")
  target_link_libraries(echo-track-analysis PRIVATE avformat avcodec swresample avutil)
elseif(APPLE)
  target_link_libraries(echo-track-analysis PRIVATE PkgConfig::ECHO_MACOS_FFMPEG)
else()
  target_link_libraries(echo-track-analysis PRIVATE avformat avcodec swresample avutil)
endif()
if(MSVC)
  target_compile_definitions(echo-track-analysis PRIVATE NOMINMAX)
  set_property(TARGET echo-track-analysis PROPERTY
    MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()
if(APPLE AND ECHO_FFMPEG_ROOT)
  set_target_properties(echo-track-analysis PROPERTIES
    BUILD_WITH_INSTALL_RPATH TRUE
    INSTALL_RPATH "@loader_path/../tools-macos/lib;@loader_path/tools/lib")
elseif(APPLE)
  set_target_properties(echo-track-analysis PROPERTIES
    BUILD_WITH_INSTALL_RPATH TRUE
    INSTALL_RPATH "@loader_path")
endif()
if(WIN32)
  foreach(runtime_name avformat avcodec swresample avutil)
    file(GLOB analysis_runtime_dlls "${ECHO_FFMPEG_ROOT}/bin/${runtime_name}-*.dll")
    foreach(runtime_dll IN LISTS analysis_runtime_dlls)
      add_custom_command(TARGET echo-track-analysis POST_BUILD
        COMMAND ${CMAKE_COMMAND} -E copy_if_different "${runtime_dll}"
          "$<TARGET_FILE_DIR:echo-track-analysis>")
    endforeach()
  endforeach()
endif()
if(APPLE AND ECHO_FFMPEG_ROOT)
  set_tests_properties(echo-libav-seek-tests PROPERTIES
    ENVIRONMENT "DYLD_LIBRARY_PATH=${ECHO_FFMPEG_ROOT}/lib")
endif()

add_test(NAME echo-audio-engine-tests COMMAND echo-audio-engine-tests)
add_test(NAME echo-jsonrpc-protocol-characterization-tests COMMAND echo-jsonrpc-protocol-characterization-tests)
add_test(NAME echo-dsd-ring-source-characterization-tests COMMAND echo-dsd-ring-source-characterization-tests)
add_test(NAME echo-native-playback-pipeline-tests COMMAND echo-native-playback-pipeline-tests)
add_test(NAME echo-level-meter-processor-tests COMMAND echo-level-meter-processor-tests)
add_test(NAME echo-cache-pcm-producer-tests COMMAND echo-cache-pcm-producer-tests)
add_test(NAME echo-windows-cdda-reader-tests COMMAND echo-windows-cdda-reader-tests)

# Focused self-hosted listening codec/transport checks; no full audio suite required.
foreach(kind realtime_audio audio_transport opus_codec)
  add_executable(echo-listening-${kind}-tests EXCLUDE_FROM_ALL
    tests/listen_together_${kind}_tests.cpp
    src/ListenTogetherAudioTransport.cpp
    src/ListenTogetherOpusCodec.cpp
    src/ListenTogetherRealtimeAudio.cpp)
  configure_echo_audio_target(echo-listening-${kind}-tests)
  add_dependencies(echo-audio-engine-tests echo-listening-${kind}-tests)
  add_test(NAME echo-listening-${kind}-tests COMMAND echo-listening-${kind}-tests)
  if(APPLE AND ECHO_FFMPEG_ROOT)
    set_tests_properties(echo-listening-${kind}-tests PROPERTIES ENVIRONMENT "DYLD_LIBRARY_PATH=${ECHO_FFMPEG_ROOT}/lib")
  endif()
endforeach()
